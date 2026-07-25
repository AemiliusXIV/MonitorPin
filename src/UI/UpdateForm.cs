using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using MonitorPin.Update;
using MonitorPin.Util;

namespace MonitorPin.UI;

/// <summary>
/// Tells the user an update exists and lets them choose. Nothing downloads or
/// runs without an explicit click.
/// </summary>
public sealed class UpdateForm : Form
{
    private readonly UpdateInfo _info;
    private readonly ProgressBar _progress = new() { Visible = false };
    private readonly Label _status = new() { AutoSize = true, Visible = false };
    private Button _install = null!;
    private Button _page = null!;
    private Button _skip = null!;
    private Button _later = null!;

    /// <summary>True once the installer has been started and we should quit.</summary>
    public bool InstallerLaunched { get; private set; }

    /// <summary>True if the user asked never to be told about this version again.</summary>
    public bool SkipRequested { get; private set; }

    public UpdateForm(UpdateInfo info)
    {
        _info = info;

        Text = "MonitorPin update";
        Icon = AppIconFactory.Shared;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(460, 380);

        var title = new Label
        {
            Text = $"MonitorPin {info.Version} is available",
            Left = 16, Top = 16, AutoSize = true,
            Font = new Font(Font.FontFamily, 12f, FontStyle.Bold),
        };
        Controls.Add(title);

        Controls.Add(new Label
        {
            Text = $"You have {UpdateChecker.CurrentVersion}. Your rules and settings are kept.",
            Left = 16, Top = 44, AutoSize = true, ForeColor = SystemColors.GrayText,
        });

        Controls.Add(new Label { Text = "What's new:", Left = 16, Top = 74, AutoSize = true });

        var notes = new TextBox
        {
            Left = 16, Top = 94, Width = 428, Height = 190,
            Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
            Text = string.IsNullOrWhiteSpace(info.Notes) ? "(no release notes)" : info.Notes.Replace("\n", "\r\n"),
            BackColor = SystemColors.Window,
        };
        Controls.Add(notes);

        _progress.SetBounds(16, 294, 428, 18);
        Controls.Add(_progress);
        _status.SetBounds(16, 294, 428, 18);
        Controls.Add(_status);

        int by = 330;
        _install = new Button { Text = "Download and install", Left = 16, Top = by, Width = 150, Height = 30 };
        _install.Click += OnInstall;
        _page = new Button { Text = "Release page", Left = 174, Top = by, Width = 100, Height = 30 };
        _page.Click += (_, _) => OpenPage();
        _skip = new Button { Text = "Skip this one", Left = 282, Top = by, Width = 90, Height = 30 };
        _skip.Click += (_, _) => { SkipRequested = true; DialogResult = DialogResult.Ignore; Close(); };
        _later = new Button { Text = "Later", Left = 380, Top = by, Width = 64, Height = 30, DialogResult = DialogResult.Cancel };
        Controls.AddRange(new Control[] { _install, _page, _skip, _later });
        CancelButton = _later;
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Theme.Apply(this);
    }

    private void OpenPage()
    {
        try { Process.Start(new ProcessStartInfo(_info.ReleasePage) { UseShellExecute = true }); }
        catch { }
    }

    private async void OnInstall(object? sender, EventArgs e)
    {
        SetBusy(true, "Downloading…");
        try
        {
            var progress = new Progress<int>(p => _progress.Value = Math.Clamp(p, 0, 100));
            string file = await UpdateChecker.DownloadAsync(_info, progress);

            SetBusy(true, "Starting the installer…");
            // Setup replaces the app, so hand off and quit. It closes us via its
            // AppMutex check if we haven't exited by the time it needs the file.
            Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
            InstallerLaunched = true;
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex)
        {
            SetBusy(false, null);
            MessageBox.Show(this,
                $"The update couldn't be downloaded:\n\n{ex.Message}\n\nYou can use the release page instead.",
                "MonitorPin", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void SetBusy(bool busy, string? status)
    {
        _install.Enabled = _skip.Enabled = _later.Enabled = !busy;
        _progress.Visible = busy && status == "Downloading…";
        _status.Visible = busy && !_progress.Visible;
        if (status != null) _status.Text = status;
        Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
    }
}
