using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using MonitorPin.Diagnostics;
using MonitorPin.Monitors;
using MonitorPin.Rules;
using MonitorPin.Startup;
using MonitorPin.Update;

namespace MonitorPin.UI;

/// <summary>Settings + diagnostics, kept off the main window. Applies on OK.</summary>
public sealed class OptionsForm : Form
{
    private readonly RuleStore _store;
    private readonly MonitorCatalog _catalog;
    private readonly ToolTip _tips = new() { AutoPopDelay = 15000, InitialDelay = 400, ReshowDelay = 200 };

    private readonly CheckBox _active = new() { Text = "Keep my window rules turned on" };
    private readonly CheckBox _startup = new() { Text = "Start with Windows, before other apps (recommended)" };
    private readonly CheckBox _updates = new() { Text = "Tell me when an update is available" };
    private readonly ComboBox _appearance = new() { DropDownStyle = ComboBoxStyle.DropDownList };

    private bool _startupWasOn;

    /// <summary>Raised by the "Check now" button; the tray runs the check.</summary>
    public event Action? CheckNowRequested;

    public OptionsForm(RuleStore store, MonitorCatalog catalog)
    {
        _store = store;
        _catalog = catalog;

        Text = "MonitorPin - Settings";
        Icon = Util.AppIconFactory.Shared;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(452, 408);

        BuildLayout();

        _appearance.SelectedIndex = (int)store.Config.Appearance;
        _active.Checked = store.Config.Enabled;
        _startupWasOn = StartupInstaller.IsInstalled();
        _startup.Checked = _startupWasOn;
        _startup.CheckedChanged += OnStartupToggled;
        _updates.Checked = store.Config.CheckForUpdates;
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Theme.Apply(this);
    }

    private void BuildLayout()
    {
        int x = 16, y = 14, w = 420;

        var general = new GroupBox { Text = "General", Left = x, Top = y, Width = w, Height = 154 };
        _active.SetBounds(14, 24, 390, 22);
        _tips.SetToolTip(_active, "Turn every window rule on or off at once, without deleting any of them.");

        _startup.SetBounds(14, 52, 390, 22);
        _tips.SetToolTip(_startup,
            "Starts MonitorPin quietly as soon as you sign in, using a Windows scheduled task.\r\n\r\n"
            + "Windows deliberately holds normal startup apps back for a few seconds, so this way "
            + "MonitorPin is ready before your other apps open, and it can control apps that run as "
            + "administrator. There's no admin pop-up at sign-in.\r\n\r\n"
            + "Turn this off and MonitorPin only runs when you start it yourself.");

        _updates.SetBounds(14, 82, 250, 22);
        _tips.SetToolTip(_updates,
            "Checks github.com once a day and tells you if a newer version exists. Nothing is ever "
            + "downloaded or installed without you clicking to allow it. Turn this off and MonitorPin "
            + "will not contact the internet at all.");
        var checkNow = MakeButton("Check now", 286, 80, 118, "Look for a newer version right now.");
        checkNow.Click += (_, _) => CheckNowRequested?.Invoke();

        var appLabel = new Label { Text = "Appearance:", Left = 14, Top = 116, AutoSize = true };
        _appearance.SetBounds(96, 112, 168, 24);
        _appearance.Items.AddRange(new object[] { "Follow Windows", "Light", "Dark" });
        _tips.SetToolTip(_appearance,
            "Light or dark windows. \"Follow Windows\" matches whatever you've set Windows itself to. "
            + "Applies as soon as you click OK.");
        var monNames = MakeButton("Monitor names…", 286, 112, 118, "Choose how your screens are named, or give them your own names.");
        monNames.Click += (_, _) => { using var f = new MonitorsForm(_store, _catalog); f.ShowDialog(this); };

        general.Controls.AddRange(new Control[] { _active, _startup, _updates, checkNow, appLabel, _appearance, monNames });
        Controls.Add(general);
        y += 166;

        var hkBox = new GroupBox { Text = "Keyboard shortcuts", Left = x, Top = y, Width = w, Height = 60 };
        var scBtn = MakeButton("Set keyboard shortcuts…", 14, 22, 200,
            "Minimize a window, move a window between screens, and minimize a game by name.");
        scBtn.Click += (_, _) => { using var f = new ShortcutsForm(_store); f.ShowDialog(this); };
        hkBox.Controls.Add(scBtn);
        Controls.Add(hkBox);
        y += 72;

        var diag = new GroupBox { Text = "Something not working?", Left = x, Top = y, Width = w, Height = 98 };
        diag.Controls.Add(new Label
        {
            Left = 14, Top = 20, Width = 398, Height = 30, AutoSize = false,
            Text = "Save a report of recent activity to help track down a problem.",
            ForeColor = SystemColors.GrayText,
        });
        var saveReport = MakeButton("Save a report…", 14, 56, 130, "Write recent activity to a file you can look at or share.");
        saveReport.Click += OnSaveReport;
        diag.Controls.Add(saveReport);
        var openLogs = MakeButton("Open logs folder", 152, 56, 130, "Open the folder where saved reports are kept.");
        openLogs.Click += (_, _) => OpenLogsFolder();
        diag.Controls.Add(openLogs);
        Controls.Add(diag);
        y += 110;

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 260, Top = y, Width = 84, Height = 30 };
        ok.Click += OnOk;
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 352, Top = y, Width = 84, Height = 30 };
        Controls.Add(ok);
        Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    private Button MakeButton(string text, int x, int y, int w, string tip)
    {
        var b = new Button { Text = text, Left = x, Top = y, Width = w, Height = 28 };
        _tips.SetToolTip(b, tip);
        return b;
    }

    /// <summary>Turning startup off has non-obvious consequences; say so once, here.</summary>
    private void OnStartupToggled(object? sender, EventArgs e)
    {
        if (_startup.Checked || !_startupWasOn) return;

        var answer = MessageBox.Show(this,
            "Without this, MonitorPin won't be running when you sign in.\n\n"
            + "That means your rules won't apply to anything that opens at start-up, and nothing "
            + "will be arranged until you open MonitorPin yourself. A copy you start by hand also "
            + "can't control apps that run as administrator, so the minimize shortcut may not work "
            + "on some games.\n\nTurn it off anyway?",
            "Start with Windows", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);

        if (answer != DialogResult.Yes)
            _startup.Checked = true; // put it back
    }

    private void OnSaveReport(object? sender, EventArgs e)
    {
        string path = Log.SaveReport();

        string msg = $"Saved to:\n{path}\n\n{Log.ReportContentsNotice}\n\n"
                   + "Have a look before sharing it. Open it now?";
        var answer = MessageBox.Show(this, msg, "Report saved", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
        if (answer == DialogResult.Yes) TryOpen(path);

        var issue = MessageBox.Show(this,
            "Do you want to report this problem on GitHub?\n\n"
            + "That opens the issues page in your browser. You can attach the report file there.",
            "Report a problem", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (issue == DialogResult.Yes)
            TryOpen($"https://github.com/{UpdateChecker.Owner}/{UpdateChecker.Repo}/issues");
    }

    private void OpenLogsFolder()
    {
        Directory.CreateDirectory(Log.LogDir());
        TryOpen(Log.LogDir());
    }

    private static void TryOpen(string path)
    {
        try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
        catch { }
    }

    private void OnOk(object? sender, EventArgs e)
    {
        _store.Config.Enabled = _active.Checked;
        _store.Config.CheckForUpdates = _updates.Checked;
        _store.Config.Appearance = (AppearanceMode)_appearance.SelectedIndex;
        Theme.Resolve(_store.Config.Appearance);

        bool installed = StartupInstaller.IsInstalled();
        if (_startup.Checked != installed)
        {
            string exe = Environment.ProcessPath ?? Application.ExecutablePath;
            StartupInstaller.RequestSetEnabled(_startup.Checked, exe);
        }

        _store.Save();
    }
}
