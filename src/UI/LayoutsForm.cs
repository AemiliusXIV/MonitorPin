using System.Drawing;
using System.Windows.Forms;
using MonitorPin.Core;
using MonitorPin.Monitors;
using MonitorPin.Rules;

namespace MonitorPin.UI;

/// <summary>Save the current window arrangement as a named layout, and put it back later.</summary>
public sealed class LayoutsForm : Form
{
    private readonly RuleStore _store;
    private readonly MonitorCatalog _catalog;
    private readonly ToolTip _tips = new() { AutoPopDelay = 12000, InitialDelay = 400, ReshowDelay = 200 };

    private readonly ListBox _list = new();
    private readonly ComboBox _quick = new() { DropDownStyle = ComboBoxStyle.DropDownList };

    /// <summary>Raised when the "restore" shortcut target changes, so the tray re-reads.</summary>
    public event Action? Changed;

    public LayoutsForm(RuleStore store, MonitorCatalog catalog)
    {
        _store = store;
        _catalog = catalog;

        Text = "Window layouts";
        Icon = Util.AppIconFactory.Shared;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(460, 320);

        BuildLayout();
        RefreshList();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Theme.Apply(this);
    }

    private void BuildLayout()
    {
        Controls.Add(new Label
        {
            Left = 12, Top = 12, Width = 320, Height = 32, AutoSize = false,
            Text = "Save where your windows are now, and put them back with one click later.",
            ForeColor = SystemColors.GrayText,
        });

        _list.SetBounds(12, 48, 300, 200);
        _list.IntegralHeight = false;
        _list.DoubleClick += (_, _) => OnRestore(this, EventArgs.Empty);
        Controls.Add(_list);

        int bx = 324, by = 48;
        AddButton("Save current…", bx, ref by, OnSave, "Save where your windows are right now as a new layout.");
        AddButton("Restore", bx, ref by, OnRestore, "Move windows back to where this layout has them.");
        AddButton("Desktop shortcut", bx, ref by, OnShortcut,
            "Put a shortcut on your desktop that restores this layout when you double-click it.");
        AddButton("Rename…", bx, ref by, OnRename, "Rename the selected layout.");
        AddButton("Delete", bx, ref by, OnDelete, "Delete the selected layout.");

        var quickLabel = new Label { Text = "Shortcut restores:", Left = 12, Top = 262, AutoSize = true };
        Controls.Add(quickLabel);
        _quick.SetBounds(126, 258, 186, 24);
        _quick.SelectedIndexChanged += OnQuickChanged;
        _tips.SetToolTip(_quick, "The layout brought back by the \"restore layout\" keyboard shortcut (set that in Settings).");
        Controls.Add(_quick);

        var close = new Button { Text = "Close", DialogResult = DialogResult.OK, Left = 364, Top = 282, Width = 84, Height = 30 };
        Controls.Add(close);
        AcceptButton = close;
    }

    private void AddButton(string text, int x, ref int y, EventHandler onClick, string tip)
    {
        var b = new Button { Text = text, Left = x, Top = y, Width = 124, Height = 30 };
        b.Click += onClick;
        _tips.SetToolTip(b, tip);
        Controls.Add(b);
        y += 36;
    }

    private void RefreshList()
    {
        _list.Items.Clear();
        foreach (var l in _store.Config.Layouts)
            _list.Items.Add($"{l.Name}  ({l.Windows.Count} window{(l.Windows.Count == 1 ? "" : "s")})");

        _quick.Items.Clear();
        _quick.Items.Add("(none)");
        foreach (var l in _store.Config.Layouts) _quick.Items.Add(l.Name);
        int qi = _store.Config.QuickLayout == null ? 0 : _store.Config.Layouts.FindIndex(l => l.Name == _store.Config.QuickLayout) + 1;
        _quick.SelectedIndex = Math.Max(0, qi);
    }

    private WindowLayout? Selected =>
        _list.SelectedIndex >= 0 && _list.SelectedIndex < _store.Config.Layouts.Count
            ? _store.Config.Layouts[_list.SelectedIndex] : null;

    private void OnSave(object? sender, EventArgs e)
    {
        string? name = Prompt.Text(this, "Save layout", "Name for this layout:", SuggestName());
        if (name == null) return;

        var layout = LayoutService.Capture(name, _catalog);
        if (layout.Windows.Count == 0)
        {
            MessageBox.Show(this, "No windows to save right now.", "MonitorPin", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        int existing = _store.Config.Layouts.FindIndex(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (existing >= 0)
        {
            if (MessageBox.Show(this, $"Replace the existing \"{name}\" layout?", "MonitorPin", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            _store.Config.Layouts[existing] = layout;
        }
        else _store.Config.Layouts.Add(layout);

        _store.Save();
        RefreshList();
    }

    private void OnRestore(object? sender, EventArgs e)
    {
        if (Selected is { } l) LayoutService.Restore(l, _catalog);
    }

    private void OnShortcut(object? sender, EventArgs e)
    {
        if (Selected is not { } l) return;
        string exe = Environment.ProcessPath ?? Application.ExecutablePath;
        string? path = Util.ShortcutMaker.CreateOnDesktop(
            $"{l.Name} layout", exe, $"--layout \"{l.Name}\"", $"Restore the {l.Name} window layout");

        if (path != null)
            MessageBox.Show(this, $"Shortcut created:\n{path}\n\nDouble-click it any time to put these windows back.",
                "MonitorPin", MessageBoxButtons.OK, MessageBoxIcon.Information);
        else
            MessageBox.Show(this, "Couldn't create the shortcut.", "MonitorPin", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private void OnRename(object? sender, EventArgs e)
    {
        if (Selected is not { } l) return;
        string? name = Prompt.Text(this, "Rename layout", "New name:", l.Name);
        if (name == null || name.Equals(l.Name, StringComparison.Ordinal)) return;
        if (_store.Config.QuickLayout == l.Name) _store.Config.QuickLayout = name;
        l.Name = name;
        _store.Save();
        RefreshList();
        Changed?.Invoke();
    }

    private void OnDelete(object? sender, EventArgs e)
    {
        if (Selected is not { } l) return;
        if (MessageBox.Show(this, $"Delete the layout \"{l.Name}\"?", "MonitorPin", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        if (_store.Config.QuickLayout == l.Name) _store.Config.QuickLayout = null;
        _store.Config.Layouts.Remove(l);
        _store.Save();
        RefreshList();
        Changed?.Invoke();
    }

    private void OnQuickChanged(object? sender, EventArgs e)
    {
        string? name = _quick.SelectedIndex <= 0 ? null : _quick.SelectedItem?.ToString();
        if (name != _store.Config.QuickLayout)
        {
            _store.Config.QuickLayout = name;
            _store.Save();
            Changed?.Invoke();
        }
    }

    private string SuggestName()
    {
        for (int i = 1; ; i++)
        {
            string n = $"Layout {i}";
            if (!_store.Config.Layouts.Any(l => l.Name.Equals(n, StringComparison.OrdinalIgnoreCase))) return n;
        }
    }
}
