using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using MonitorPin.Core;
using MonitorPin.Interop;
using MonitorPin.Monitors;
using MonitorPin.Rules;
using MonitorPin.Util;

namespace MonitorPin.UI;

/// <summary>Modal editor for a single rule, in plain language with tooltips.</summary>
public sealed class RuleEditorForm : Form
{
    private sealed class MonitorChoice
    {
        public required string Label { get; init; }
        public required string HardwareKey { get; init; }
        public required MonitorRole Role { get; init; }
        public bool IsCursor { get; init; }
        public override string ToString() => Label;
    }

    private sealed class ProcApp
    {
        public required string Process { get; init; }   // no extension
        public string? ExePath { get; init; }
        public required string Display { get; init; }
        public Icon? Icon { get; init; }
        public override string ToString() => Display;
    }

    private readonly MonitorCatalog _catalog;
    private readonly AppConfig _config;
    private readonly Rule _rule;
    private readonly ToolTip _tips = new() { AutoPopDelay = 12000, InitialDelay = 400, ReshowDelay = 200 };

    private readonly ComboBox _process = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        DrawMode = DrawMode.OwnerDrawFixed,
        ItemHeight = 20,
    };
    private Button _browse = null!;
    private readonly TextBox _title = new();
    private readonly ComboBox _monitor = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _state = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _scope = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _foreground = new() { Text = "Bring it to the front when it opens" };
    private readonly CheckBox _aggressive = new() { Text = "Keep re-applying (for apps that fight back)" };
    private readonly CheckBox _enabled = new() { Text = "Turn this rule on" };
    private readonly CheckBox _byPosition = new() { Text = "Match the screen by position (works on other PCs)" };

    private readonly NumericUpDown _x = NewNum();
    private readonly NumericUpDown _y = NewNum();
    private readonly NumericUpDown _w = NewNum();
    private readonly NumericUpDown _h = NewNum();
    private readonly GroupBox _customBox = new() { Text = "Custom size (measured from the monitor's top-left corner)" };

    public Rule Result => _rule;

    public RuleEditorForm(MonitorCatalog catalog, AppConfig config, Rule? existing)
    {
        _catalog = catalog;
        _config = config;
        _rule = existing ?? new Rule();

        Text = existing == null ? "Add rule" : "Edit rule";
        Icon = AppIconFactory.Shared;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(468, 534);

        BuildLayout();
        PopulateProcesses();
        PopulateMonitors();
        LoadFromRule();

        if (existing != null)
        {
            // Editing: lock the app so it can't be swapped by accident.
            _process.Enabled = false;
            _browse.Visible = false;
            _tips.SetToolTip(_process, "The app this rule controls. To point a rule at a different app, make a new rule.");
        }
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Theme.Apply(this);
    }

    private static NumericUpDown NewNum() => new() { Minimum = -100000, Maximum = 100000, Width = 80 };

    private void BuildLayout()
    {
        int x = 16, y = 16, ctlX = 150, ctlW = 302;

        AddLabel("Application:", x, y + 3);
        _process.SetBounds(ctlX, y, ctlW - 74, 24);
        _process.DrawItem += OnDrawProcessItem;
        _tips.SetToolTip(_process, "The app this rule controls. Pick one that's open now, or use Browse to point at its program.");
        Controls.Add(_process);
        _browse = new Button { Text = "Browse…", Left = ctlX + ctlW - 68, Top = y - 1, Width = 68, Height = 26 };
        _browse.Click += OnBrowse;
        _tips.SetToolTip(_browse, "Choose the app's program file if it isn't running right now.");
        Controls.Add(_browse);
        y += 38;

        AddLabel("Only affect windows whose title contains (optional):", x, y);
        y += 20;
        _title.SetBounds(x, y, 436, 24);
        _tips.SetToolTip(_title,
            "Leave blank to affect every window of this app. Type part of a title to affect only matching "
            + "windows — handy when one app opens several kinds of window.");
        Controls.Add(_title);
        y += 38;

        AddLabel("Send it to this screen:", x, y + 3);
        _monitor.SetBounds(ctlX, y, ctlW, 24);
        _tips.SetToolTip(_monitor, "Which screen to move the app to. \"Where my mouse is\" uses whichever screen the pointer is on when the app opens.");
        // Hide (not disable) when it doesn't apply: a disabled control's text dims
        // toward the background, which is unreadable on a dark theme.
        _monitor.SelectedIndexChanged += (_, _) => _byPosition.Visible = !((_monitor.SelectedItem as MonitorChoice)?.IsCursor ?? false);
        Controls.Add(_monitor);
        y += 28;

        _byPosition.SetBounds(ctlX, y, ctlW, 22);
        _tips.SetToolTip(_byPosition,
            "Off: the rule sticks to this exact monitor, even if you rearrange your screens.\r\n"
            + "On: the rule follows the screen's position instead (for example \"the right-hand screen\"), "
            + "so it still makes sense if you swap a monitor out, or if you share this rule with someone "
            + "whose screens are different.");
        Controls.Add(_byPosition);
        y += 32;

        AddLabel("Size it like this:", x, y + 3);
        _state.SetBounds(ctlX, y, 170, 24);
        _state.Items.AddRange(new object[] { "Maximized (fill the screen)", "Normal", "Minimized (to taskbar)", "Custom size" });
        _state.SelectedIndexChanged += (_, _) => UpdateCustomEnabled();
        _tips.SetToolTip(_state, "How the window should appear once it's moved.");
        Controls.Add(_state);
        y += 38;

        AddLabel("Which windows:", x, y + 3);
        _scope.SetBounds(ctlX, y, 170, 24);
        _scope.Items.AddRange(new object[] { "Only the first window", "Every window" });
        _tips.SetToolTip(_scope, "Affect just the first window the app opens, or every window it opens.");
        Controls.Add(_scope);
        y += 42;

        _foreground.SetBounds(ctlX, y, ctlW, 22);
        _tips.SetToolTip(_foreground, "Some apps open behind other windows. Turn this on to make it appear in front.");
        Controls.Add(_foreground);
        y += 26;
        _aggressive.SetBounds(ctlX, y, ctlW, 22);
        _tips.SetToolTip(_aggressive,
            "Most apps only need a nudge when they open.\r\n"
            + "A few launchers and games keep shoving\r\n"
            + "themselves back for a few seconds.\r\n\r\n"
            + "Turn this on to keep re-applying the rule for\r\n"
            + "longer and win that fight. Leave it off unless\r\n"
            + "an app won't stay put.");
        Controls.Add(_aggressive);
        y += 26;
        _enabled.SetBounds(ctlX, y, ctlW, 22);
        _tips.SetToolTip(_enabled, "Turn this rule off to stop it working without deleting it.");
        Controls.Add(_enabled);
        y += 34;

        _customBox.SetBounds(x, y, 436, 96);
        AddChild(_customBox, "X:", 12, 28);
        _x.SetBounds(36, 25, 70, 24); _customBox.Controls.Add(_x);
        AddChild(_customBox, "Y:", 120, 28);
        _y.SetBounds(142, 25, 70, 24); _customBox.Controls.Add(_y);
        AddChild(_customBox, "W:", 226, 28);
        _w.SetBounds(252, 25, 70, 24); _customBox.Controls.Add(_w);
        AddChild(_customBox, "H:", 336, 28);
        _h.SetBounds(360, 25, 64, 24); _customBox.Controls.Add(_h);
        var grab = new Button { Text = "Use current position", Left = 12, Top = 58, Width = 180, Height = 26 };
        grab.Click += OnGrab;
        _tips.SetToolTip(grab, "Fill these boxes from where this app's window is sitting right now.");
        _customBox.Controls.Add(grab);
        Controls.Add(_customBox);
        y += 106;

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 276, Top = y, Width = 84, Height = 30 };
        ok.Click += OnOk;
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 368, Top = y, Width = 84, Height = 30 };
        Controls.Add(ok);
        Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;
    }

    private void OnDrawProcessItem(object? sender, DrawItemEventArgs e)
    {
        e.DrawBackground();
        if (e.Index >= 0 && _process.Items[e.Index] is ProcApp a)
        {
            int textX = e.Bounds.Left + 3;
            if (a.Icon != null)
            {
                int y = e.Bounds.Top + (e.Bounds.Height - 16) / 2;
                e.Graphics.DrawIcon(a.Icon, new Rectangle(e.Bounds.Left + 3, y, 16, 16));
                textX = e.Bounds.Left + 24;
            }
            TextRenderer.DrawText(e.Graphics, a.Display, _process.Font,
                new Rectangle(textX, e.Bounds.Top, e.Bounds.Width - textX, e.Bounds.Height),
                e.ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }
        e.DrawFocusRectangle();
    }

    private void AddLabel(string text, int x, int y)
        => Controls.Add(new Label { Text = text, Left = x, Top = y, AutoSize = true });

    private static void AddChild(Control parent, string text, int x, int y)
        => parent.Controls.Add(new Label { Text = text, Left = x, Top = y, AutoSize = true });

    private void PopulateProcesses()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var apps = new List<ProcApp>();
        try
        {
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.MainWindowHandle == IntPtr.Zero || string.IsNullOrEmpty(p.MainWindowTitle)) continue;
                    if (!seen.Add(p.ProcessName)) continue;
                    string? path = null;
                    try { path = p.MainModule?.FileName; } catch { }
                    apps.Add(new ProcApp
                    {
                        Process = p.ProcessName,
                        ExePath = path,
                        Display = AppInfo.FriendlyName(p.ProcessName, path),
                        Icon = AppInfo.IconFor(p.ProcessName, path),
                    });
                }
                catch { }
            }
        }
        catch { }

        foreach (var a in apps.OrderBy(a => a.Display, StringComparer.OrdinalIgnoreCase))
            _process.Items.Add(a);
    }

    private void PopulateMonitors()
    {
        // The real screens first, named per the user's chosen naming style.
        foreach (var e in _catalog.Entries)
            _monitor.Items.Add(new MonitorChoice { Label = MonitorNaming.Label(e, _config), HardwareKey = e.HardwareKey, Role = e.Role });
        // "Where my mouse is" last: it's the niche option, not the default.
        _monitor.Items.Add(new MonitorChoice { Label = "Where my mouse is", HardwareKey = "", Role = MonitorRole.Primary, IsCursor = true });
    }

    private void LoadFromRule()
    {
        // Ensure the rule's own app is selectable even if it isn't running now.
        if (!string.IsNullOrEmpty(_rule.Process))
        {
            bool present = _process.Items.Cast<ProcApp>().Any(a => a.Process.Equals(_rule.Process, StringComparison.OrdinalIgnoreCase));
            var mine = new ProcApp
            {
                Process = _rule.Process,
                ExePath = _rule.ExePath,
                Display = _rule.DisplayName ?? AppInfo.FriendlyName(_rule.Process, _rule.ExePath),
                Icon = AppInfo.IconFor(_rule.Process, _rule.ExePath),
            };
            if (!present) _process.Items.Insert(0, mine);
            SelectProcess(_rule.Process);
        }

        _title.Text = _rule.TitleContains ?? "";
        _state.SelectedIndex = (int)_rule.State;
        _scope.SelectedIndex = (int)_rule.ApplyTo;
        _foreground.Checked = _rule.ForceForeground;
        _aggressive.Checked = _rule.Aggressive;
        _enabled.Checked = _rule.Enabled;

        // Select whichever live screen this rule points at, by cursor, key or position.
        _byPosition.Checked = _rule.Monitor.Mode == MonitorMatchMode.ByRole;
        int idx = -1;
        for (int i = 0; i < _monitor.Items.Count; i++)
        {
            if (_monitor.Items[i] is not MonitorChoice mc) continue;
            bool hit = _rule.Monitor.Mode switch
            {
                MonitorMatchMode.Cursor => mc.IsCursor,
                MonitorMatchMode.ByRole => !mc.IsCursor && mc.Role == _rule.Monitor.Role,
                _ => !mc.IsCursor && mc.HardwareKey.Equals(_rule.Monitor.HardwareKey, StringComparison.OrdinalIgnoreCase),
            };
            if (hit) { idx = i; break; }
        }
        if (idx < 0)
        {
            // New rule (nothing matched): default to the main screen, not the cursor.
            idx = _monitor.Items.Cast<MonitorChoice>().ToList()
                .FindIndex(mc => !mc.IsCursor && mc.Role == MonitorRole.Primary);
            if (idx < 0) idx = 0;
        }
        _monitor.SelectedIndex = idx;

        if (_rule.Size != null)
        {
            _x.Value = _rule.Size.X; _y.Value = _rule.Size.Y;
            _w.Value = _rule.Size.Width; _h.Value = _rule.Size.Height;
        }
        UpdateCustomEnabled();
    }

    private void SelectProcess(string process)
    {
        for (int i = 0; i < _process.Items.Count; i++)
            if (_process.Items[i] is ProcApp a && a.Process.Equals(process, StringComparison.OrdinalIgnoreCase)) { _process.SelectedIndex = i; return; }
    }

    private void UpdateCustomEnabled()
        => _customBox.Visible = _state.SelectedIndex == (int)TargetState.CustomSize;

    private void OnBrowse(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog { Filter = "Programs (*.exe)|*.exe|All files (*.*)|*.*" };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        string proc = Path.GetFileNameWithoutExtension(dlg.FileName);
        var app = new ProcApp
        {
            Process = proc,
            ExePath = dlg.FileName,
            Display = AppInfo.FriendlyName(proc, dlg.FileName),
            Icon = AppInfo.IconFor(proc, dlg.FileName),
        };

        int existing = -1;
        for (int i = 0; i < _process.Items.Count; i++)
            if (_process.Items[i] is ProcApp a && a.Process.Equals(proc, StringComparison.OrdinalIgnoreCase)) { existing = i; break; }
        if (existing >= 0) _process.Items[existing] = app; else _process.Items.Insert(0, app);
        SelectProcess(proc);
    }

    private void OnGrab(object? sender, EventArgs e)
    {
        string proc = (_process.SelectedItem as ProcApp)?.Process ?? "";
        IntPtr hwnd = FindWindowForProcess(proc);
        if (hwnd == IntPtr.Zero) hwnd = NativeMethods.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;

        var bounds = WindowController.GetBounds(hwnd);
        var mon = _catalog.FromWindow(hwnd);
        if (mon == null) return;

        _x.Value = Clamp(bounds.Left - mon.Bounds.Left);
        _y.Value = Clamp(bounds.Top - mon.Bounds.Top);
        _w.Value = Clamp(bounds.Width);
        _h.Value = Clamp(bounds.Height);

        for (int i = 0; i < _monitor.Items.Count; i++)
            if (_monitor.Items[i] is MonitorChoice mc && mc.HardwareKey == mon.HardwareKey) { _monitor.SelectedIndex = i; break; }

        _state.SelectedIndex = (int)TargetState.CustomSize;
        UpdateCustomEnabled();
    }

    private static decimal Clamp(int v) => Math.Max(-100000, Math.Min(100000, v));

    private static IntPtr FindWindowForProcess(string processName)
    {
        if (string.IsNullOrEmpty(processName)) return IntPtr.Zero;
        try
        {
            foreach (var p in Process.GetProcessesByName(processName))
                if (p.MainWindowHandle != IntPtr.Zero) return p.MainWindowHandle;
        }
        catch { }
        return IntPtr.Zero;
    }

    private void OnOk(object? sender, EventArgs e)
    {
        if (_process.SelectedItem is not ProcApp app)
        {
            MessageBox.Show(this, "Choose an application first.", "MonitorPin", MessageBoxButtons.OK, MessageBoxIcon.Information);
            DialogResult = DialogResult.None;
            return;
        }

        _rule.Process = app.Process;
        _rule.ExePath = app.ExePath;
        _rule.DisplayName = app.Display;
        _rule.TitleContains = string.IsNullOrWhiteSpace(_title.Text) ? null : _title.Text.Trim();
        _rule.State = (TargetState)_state.SelectedIndex;
        _rule.ApplyTo = (ApplyScope)_scope.SelectedIndex;
        _rule.ForceForeground = _foreground.Checked;
        _rule.Aggressive = _aggressive.Checked;
        _rule.Enabled = _enabled.Checked;

        var choice = (MonitorChoice)_monitor.SelectedItem!;
        _rule.Monitor = choice.IsCursor
            ? new MonitorMatch { Mode = MonitorMatchMode.Cursor }
            : _byPosition.Checked
                ? new MonitorMatch { Mode = MonitorMatchMode.ByRole, Role = choice.Role }
                : new MonitorMatch { Mode = MonitorMatchMode.ByHardwareId, HardwareKey = choice.HardwareKey };

        if (_rule.State == TargetState.CustomSize)
            _rule.Size = new SizeSpec { X = (int)_x.Value, Y = (int)_y.Value, Width = (int)_w.Value, Height = (int)_h.Value };
    }
}
