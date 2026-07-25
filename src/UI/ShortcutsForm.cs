using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using MonitorPin.Rules;

namespace MonitorPin.UI;

/// <summary>Bind the global keyboard shortcuts.</summary>
public sealed class ShortcutsForm : Form
{
    private readonly RuleStore _store;
    private readonly ToolTip _tips = new() { AutoPopDelay = 12000, InitialDelay = 400, ReshowDelay = 200 };

    private HotkeySpec _min;
    private HotkeySpec _next;
    private HotkeySpec _prev;
    private HotkeySpec _restore;
    private HotkeySpec _minApp;
    private readonly ComboBox _appProcess = new() { DropDownStyle = ComboBoxStyle.DropDown };

    public ShortcutsForm(RuleStore store)
    {
        _store = store;
        _min = Clone(store.Config.MinimizeHotkey);
        _next = Clone(store.Config.NextMonitorHotkey);
        _prev = Clone(store.Config.PrevMonitorHotkey);
        _restore = Clone(store.Config.RestoreLayoutHotkey);
        _minApp = Clone(store.Config.MinimizeAppHotkey);

        Text = "Keyboard shortcuts";
        Icon = Util.AppIconFactory.Shared;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(500, 300);

        BuildLayout();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Theme.Apply(this);
    }

    private void BuildLayout()
    {
        int y = 16;
        AddRow("Minimize the current window", y,
            "Forces the window you're using down to the taskbar, even a fullscreen game.",
            () => _min, v => _min = v); y += 34;
        AddRow("Move the current window to the next screen", y,
            "Sends the window you're using to the next monitor.",
            () => _next, v => _next = v); y += 34;
        AddRow("Move the current window to the previous screen", y,
            "Sends the window you're using to the previous monitor.",
            () => _prev, v => _prev = v); y += 34;
        AddRow("Restore my quick layout", y,
            "Brings back the window layout you picked under \"Layouts\" in the main window.",
            () => _restore, v => _restore = v); y += 44;

        var grp = new GroupBox { Text = "Minimize a specific app", Left = 16, Top = y, Width = 468, Height = 92 };
        grp.Controls.Add(new Label { Text = "App:", Left = 14, Top = 28, AutoSize = true });
        _appProcess.SetBounds(50, 24, 200, 24);
        PopulateProcesses();
        _appProcess.Text = _store.Config.MinimizeAppProcess ?? "";
        _tips.SetToolTip(_appProcess, "The app to minimize, even when it isn't the window you're using. Pick one that's running, or type its name.");
        grp.Controls.Add(_appProcess);

        var box = MakeHotkeyBox(() => _minApp, v => _minApp = v);
        box.SetBounds(300, 24, 100, 24);
        grp.Controls.Add(new Label { Text = "Shortcut:", Left = 262, Top = 28, AutoSize = true, Visible = false });
        grp.Controls.Add(box);
        var clr = new Button { Text = "Clear", Left = 404, Top = 23, Width = 54, Height = 26 };
        clr.Click += (_, _) => { _minApp = new HotkeySpec(); box.Text = _minApp.ToString(); };
        grp.Controls.Add(clr);
        grp.Controls.Add(new Label
        {
            Left = 14, Top = 56, Width = 440, Height = 28, AutoSize = false, ForeColor = SystemColors.GrayText,
            Text = "Useful for a game that grabs the keyboard, so the plain minimize shortcut can't reach it.",
        });
        Controls.Add(grp);
        y += 104;

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 308, Top = y, Width = 84, Height = 30 };
        ok.Click += OnOk;
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 400, Top = y, Width = 84, Height = 30 };
        Controls.Add(ok);
        Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;
        ClientSize = new Size(500, y + 44);
    }

    private void AddRow(string label, int y, string tip, Func<HotkeySpec> get, Action<HotkeySpec> set)
    {
        Controls.Add(new Label { Text = label, Left = 16, Top = y + 4, Width = 300, AutoSize = false });
        var box = MakeHotkeyBox(get, set);
        box.SetBounds(322, y, 100, 24);
        _tips.SetToolTip(box, tip);
        Controls.Add(box);
        var clear = new Button { Text = "Clear", Left = 426, Top = y - 1, Width = 58, Height = 26 };
        clear.Click += (_, _) => { set(new HotkeySpec()); box.Text = get().ToString(); };
        Controls.Add(clear);
    }

    private TextBox MakeHotkeyBox(Func<HotkeySpec> get, Action<HotkeySpec> set)
    {
        var box = new TextBox { ReadOnly = true, Cursor = Cursors.Hand, Text = get().ToString() };
        box.GotFocus += (_, _) => box.Text = "Press keys…";
        box.LostFocus += (_, _) => box.Text = get().ToString();
        box.KeyDown += (_, e) =>
        {
            e.SuppressKeyPress = true;
            e.Handled = true;
            var code = e.KeyCode;
            if (code is Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LWin or Keys.RWin) return;
            set(new HotkeySpec
            {
                Ctrl = e.Control,
                Alt = e.Alt,
                Shift = e.Shift,
                Win = (ModifierKeys & Keys.LWin) != 0 || (ModifierKeys & Keys.RWin) != 0,
                Key = (uint)code,
                KeyName = code.ToString(),
            });
            box.Text = get().ToString();
        };
        return box;
    }

    private void PopulateProcesses()
    {
        try
        {
            var names = Process.GetProcesses()
                .Where(p => { try { return p.MainWindowHandle != IntPtr.Zero && !string.IsNullOrEmpty(p.MainWindowTitle); } catch { return false; } })
                .Select(p => { try { return p.ProcessName; } catch { return null; } })
                .Where(n => n != null)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n)
                .Cast<object>()
                .ToArray();
            _appProcess.Items.AddRange(names);
        }
        catch { }
    }

    private void OnOk(object? sender, EventArgs e)
    {
        _store.Config.MinimizeHotkey = _min;
        _store.Config.NextMonitorHotkey = _next;
        _store.Config.PrevMonitorHotkey = _prev;
        _store.Config.RestoreLayoutHotkey = _restore;
        _store.Config.MinimizeAppHotkey = _minApp;
        _store.Config.MinimizeAppProcess = string.IsNullOrWhiteSpace(_appProcess.Text) ? null : _appProcess.Text.Trim();
        _store.Save();
    }

    private static HotkeySpec Clone(HotkeySpec s) => new()
    {
        Ctrl = s.Ctrl, Alt = s.Alt, Shift = s.Shift, Win = s.Win, Key = s.Key, KeyName = s.KeyName,
    };
}
