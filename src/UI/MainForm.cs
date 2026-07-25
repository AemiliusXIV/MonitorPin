using System.Drawing;
using System.Windows.Forms;
using MonitorPin.Monitors;
using MonitorPin.Rules;
using MonitorPin.Util;

namespace MonitorPin.UI;

/// <summary>
/// The main window: just the list of rules and the buttons to manage them.
/// Changes save immediately (no Save button), so what you see is what's running.
/// Everything else lives behind the "Settings" button (OptionsForm).
/// </summary>
public sealed class MainForm : Form
{
    public static string VersionString
    {
        get
        {
            var v = typeof(MainForm).Assembly.GetName().Version;
            return v is null ? "" : $"{v.Major}.{v.Minor}.{v.Build}";
        }
    }

    private readonly RuleStore _store;
    private readonly MonitorCatalog _catalog;
    private readonly ListView _list = new();
    private readonly ImageList _icons = new() { ImageSize = new Size(16, 16), ColorDepth = ColorDepth.Depth32Bit };
    private readonly ToolTip _tips = new() { AutoPopDelay = 12000, InitialDelay = 400, ReshowDelay = 200 };
    private bool _populating;
    private bool _toggleAllowed;

    /// <summary>Raised after any change is saved, so the tray can re-read settings.</summary>
    public event Action? Changed;

    /// <summary>Raised for "Apply rules now"; the tray runs the engine.</summary>
    public event Action? ApplyNowRequested;

    /// <summary>Raised by Settings' "Check now"; the tray runs the update check.</summary>
    public event Action? CheckUpdatesRequested;

    public MainForm(RuleStore store, MonitorCatalog catalog)
    {
        _store = store;
        _catalog = catalog;

        Text = $"MonitorPin {VersionString}";
        Icon = AppIconFactory.Shared;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(620, 420);
        MinimumSize = new Size(520, 320);

        BuildLayout();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Theme.Apply(this);
        RefreshList(); // only now the ListView handle exists, so check events don't defer
        FillLastColumn();
    }

    /// <summary>Size the trailing filler column to cover the rest of the header.</summary>
    private void FillLastColumn()
    {
        if (_list.Columns.Count == 0) return;
        int used = 0;
        for (int i = 0; i < _list.Columns.Count - 1; i++) used += _list.Columns[i].Width;
        int remaining = _list.ClientSize.Width - used;
        _list.Columns[^1].Width = Math.Max(0, remaining);
    }

    private void BuildLayout()
    {
        var menu = new MenuStrip();
        var file = new ToolStripMenuItem("&File");
        file.DropDownItems.Add(new ToolStripMenuItem("&Import rules…", null, OnImport));
        file.DropDownItems.Add(new ToolStripMenuItem("&Export rules…", null, OnExport));
        file.DropDownItems.Add(new ToolStripSeparator());
        file.DropDownItems.Add(new ToolStripMenuItem("&Close", null, (_, _) => Close()));
        var help = new ToolStripMenuItem("&Help");
        help.DropDownItems.Add(new ToolStripMenuItem("Check for &updates…", null, (_, _) => CheckUpdatesRequested?.Invoke()));
        help.DropDownItems.Add(new ToolStripMenuItem("&About MonitorPin", null, (_, _) => { using var a = new AboutForm(); a.ShowDialog(this); }));
        menu.Items.Add(file);
        menu.Items.Add(help);
        MainMenuStrip = menu;
        Controls.Add(menu);

        var intro = new Label
        {
            Text = "Rules send apps to the screen and size you want when they open.",
            Left = 12, Top = 34, AutoSize = true, ForeColor = SystemColors.GrayText,
        };
        Controls.Add(intro);

        _list.SetBounds(12, 58, 460, 350);
        _list.View = View.Details;
        _list.FullRowSelect = true;
        _list.CheckBoxes = true;
        _list.GridLines = true;
        _list.MultiSelect = false;
        _list.ShowItemToolTips = true;
        _list.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
        // Owner-draw so the app icon sits by the name in the App column, leaving
        // "On" as just the checkbox. Only the App cell is custom; the rest default.
        _list.OwnerDraw = true;
        _list.DrawColumnHeader += (_, e) => e.DrawDefault = true;
        _list.DrawItem += (_, _) => { };
        _list.DrawSubItem += OnDrawSubItem;
        _list.Columns.Add("On", 28);
        _list.Columns.Add("App", 150);
        _list.Columns.Add("Sends to", 150);
        _list.Columns.Add("Size", 72);
        _list.Columns.Add("Front", 40);
        // Trailing filler: the header gap past the last real column is drawn white
        // even in dark mode, so this soaks up the remaining width with a themed header.
        _list.Columns.Add("", 0);
        _list.Resize += (_, _) => FillLastColumn();
        _list.DoubleClick += (_, _) => EditSelected();
        // A ListView toggles the checkbox on a double-click anywhere on the row,
        // so opening the editor would also flip the rule on/off. Only let the
        // checkbox itself (or the space bar) change it.
        _list.MouseDown += (_, e) =>
            _toggleAllowed = _list.HitTest(e.Location).Location == ListViewHitTestLocations.StateImage;
        _list.KeyDown += (_, e) => { if (e.KeyCode == Keys.Space) _toggleAllowed = true; };
        _list.ItemCheck += OnItemCheck;
        _list.ItemChecked += OnItemChecked;
        Controls.Add(_list);

        const AnchorStyles tr = AnchorStyles.Top | AnchorStyles.Right;
        int bx = 486, bw = 122, by = 58;
        AddButton("Add rule…", bx, ref by, bw, tr, OnAdd,
            "Create a new rule: pick an app and where it should go.");
        AddButton("Edit…", bx, ref by, bw, tr, (_, _) => EditSelected(),
            "Change the selected rule.");
        AddButton("Remove", bx, ref by, bw, tr, OnRemove,
            "Delete the selected rule.");
        by += 14;
        AddButton("Apply now", bx, ref by, bw, tr, (_, _) => ApplyNowRequested?.Invoke(),
            "Run your rules against every window that's already open, without relaunching them.");
        AddButton("Layouts…", bx, ref by, bw, tr, (_, _) => OpenLayouts(),
            "Save where your windows are now, and put them back later.");
        by += 14;
        AddButton("Settings…", bx, ref by, bw, tr, (_, _) => OpenOptions(),
            "Startup, shortcuts, and diagnostics.");

        var close = new Button
        {
            Text = "Close", Width = bw, Height = 30, Left = bx, Top = ClientSize.Height - 42,
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right,
        };
        close.Click += (_, _) => Close();
        Controls.Add(close);
    }

    private void AddButton(string text, int x, ref int y, int w, AnchorStyles anchor, EventHandler onClick, string tip)
    {
        var b = new Button { Text = text, Left = x, Top = y, Width = w, Height = 30, Anchor = anchor };
        b.Click += onClick;
        _tips.SetToolTip(b, tip);
        Controls.Add(b);
        y += 34;
    }

    private void RefreshList()
    {
        // Realize the handle first: adding checked items to an unrealized list
        // defers the check events until Show, past the guard below, which would
        // let the double-click veto wrongly clear (and save) every rule.
        if (!_list.IsHandleCreated) _list.CreateControl();

        _populating = true;
        _list.Items.Clear();
        foreach (var r in _store.Config.Rules)
        {
            var item = new ListViewItem(new[]
            {
                "",
                AppInfo.FriendlyName(r.Process, r.ExePath),
                DescribeMonitor(r.Monitor),
                DescribeState(r),
                r.ForceForeground ? "yes" : "",
            })
            {
                Tag = r,
                Checked = r.Enabled,
                ImageKey = EnsureIcon(r),
            };
            if (IsMonitorMissing(r.Monitor))
            {
                item.ForeColor = Color.Firebrick;
                item.ToolTipText = "This rule's monitor isn't connected right now, so it won't move anything. Edit the rule to pick a screen that's plugged in.";
            }
            _list.Items.Add(item);
        }
        _populating = false;
        FillLastColumn(); // a scrollbar may have appeared/vanished, changing the width
    }

    private bool IsMonitorMissing(MonitorMatch m)
        => m.Mode == MonitorMatchMode.ByHardwareId
           && !_catalog.Entries.Any(e => e.HardwareKey.Equals(m.HardwareKey, StringComparison.OrdinalIgnoreCase));

    /// <summary>Add the app's icon to the image list (once) and return its key.</summary>
    private string EnsureIcon(Rule r)
    {
        string key = r.ExePath ?? r.Process;
        if (string.IsNullOrEmpty(key)) return "";
        if (!_icons.Images.ContainsKey(key))
        {
            var icon = AppInfo.IconFor(r.Process, r.ExePath);
            if (icon == null) return "";
            _icons.Images.Add(key, icon);
        }
        return key;
    }

    private const int OnColumn = 0;
    private const int AppColumn = 1;

    /// <summary>
    /// Draw the "On" checkbox and the App cell (icon + name) ourselves; let the
    /// other columns draw default. Owner-draw only changes painting, not the
    /// checkbox behaviour, which the ListView still handles natively.
    /// </summary>
    private void OnDrawSubItem(object? sender, DrawListViewSubItemEventArgs e)
    {
        if (e.Item is null || (e.ColumnIndex != OnColumn && e.ColumnIndex != AppColumn))
        {
            e.DrawDefault = true;
            return;
        }

        bool sel = e.Item.Selected;
        Color bg = sel ? SystemColors.Highlight : _list.BackColor;
        Color fg = sel ? SystemColors.HighlightText : e.Item.ForeColor;
        using (var b = new SolidBrush(bg)) e.Graphics.FillRectangle(b, e.Bounds);

        if (e.ColumnIndex == OnColumn)
        {
            var state = e.Item.Checked
                ? System.Windows.Forms.VisualStyles.CheckBoxState.CheckedNormal
                : System.Windows.Forms.VisualStyles.CheckBoxState.UncheckedNormal;
            var glyph = CheckBoxRenderer.GetGlyphSize(e.Graphics, state);
            int gx = e.Bounds.Left + (e.Bounds.Width - glyph.Width) / 2;
            int gy = e.Bounds.Top + (e.Bounds.Height - glyph.Height) / 2;
            CheckBoxRenderer.DrawCheckBox(e.Graphics, new Point(gx, gy), state);
            return;
        }

        // App column: icon then name.
        int x = e.Bounds.Left + 3;
        if (!string.IsNullOrEmpty(e.Item.ImageKey) && _icons.Images[e.Item.ImageKey] is { } img)
        {
            e.Graphics.DrawImage(img, x, e.Bounds.Top + (e.Bounds.Height - 16) / 2, 16, 16);
            x += 20;
        }
        TextRenderer.DrawText(e.Graphics, e.SubItem!.Text, _list.Font,
            new Rectangle(x, e.Bounds.Top, e.Bounds.Right - x, e.Bounds.Height),
            fg, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
    }

    private string DescribeMonitor(MonitorMatch m)
    {
        if (m.Mode == MonitorMatchMode.Cursor) return "Where the mouse is";
        if (m.Mode == MonitorMatchMode.ByRole) return $"{m.Role} screen";
        var entry = _catalog.Entries.FirstOrDefault(e => e.HardwareKey.Equals(m.HardwareKey, StringComparison.OrdinalIgnoreCase));
        if (entry != null) return MonitorNaming.Label(entry, _store.Config);
        return $"(disconnected: {m.HardwareKey})"; // flagged so it's obvious why nothing moved
    }

    private static string DescribeState(Rule r) => r.State switch
    {
        TargetState.Maximized => "Maximized",
        TargetState.Normal => "Normal",
        TargetState.Minimized => "Minimized",
        TargetState.CustomSize => "Custom",
        _ => r.State.ToString(),
    };

    /// <summary>Veto toggles that didn't come from the checkbox or the space bar.</summary>
    private void OnItemCheck(object? sender, ItemCheckEventArgs e)
    {
        if (_populating) return;
        if (!_toggleAllowed) e.NewValue = e.CurrentValue;
        _toggleAllowed = false;
    }

    private void OnItemChecked(object? sender, ItemCheckedEventArgs e)
    {
        if (_populating) return;
        if (e.Item.Tag is Rule r && r.Enabled != e.Item.Checked)
        {
            r.Enabled = e.Item.Checked;
            Persist();
        }
    }

    private void OnAdd(object? sender, EventArgs e)
    {
        using var editor = new RuleEditorForm(_catalog, _store.Config, null);
        if (editor.ShowDialog(this) == DialogResult.OK)
        {
            _store.Config.Rules.Add(editor.Result);
            Persist();
            RefreshList();
        }
    }

    private void EditSelected()
    {
        if (_list.SelectedItems.Count == 0) return;
        var rule = (Rule)_list.SelectedItems[0].Tag!;
        using var editor = new RuleEditorForm(_catalog, _store.Config, rule);
        if (editor.ShowDialog(this) == DialogResult.OK)
        {
            Persist();
            RefreshList();
        }
    }

    private void OnRemove(object? sender, EventArgs e)
    {
        if (_list.SelectedItems.Count == 0) return;
        var rule = (Rule)_list.SelectedItems[0].Tag!;
        string name = AppInfo.FriendlyName(rule.Process, rule.ExePath);
        if (MessageBox.Show(this, $"Remove the rule for {name}?", "MonitorPin",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            return;
        _store.Config.Rules.Remove(rule);
        Persist();
        RefreshList();
    }

    private void OpenLayouts()
    {
        using var f = new LayoutsForm(_store, _catalog);
        f.Changed += () => Changed?.Invoke(); // re-register the restore-layout shortcut
        f.ShowDialog(this);
        Changed?.Invoke();
    }

    private void OpenOptions()
    {
        using var opt = new OptionsForm(_store, _catalog);
        opt.CheckNowRequested += () => CheckUpdatesRequested?.Invoke();
        var result = opt.ShowDialog(this);
        // Monitor naming and keyboard shortcuts save from their own sub-dialogs,
        // regardless of how Settings closes, so always refresh the list and let the
        // tray re-read (re-register hotkeys, update menu).
        RefreshList();
        Changed?.Invoke();
        if (result == DialogResult.OK)
        {
            // OptionsForm has already re-resolved the theme; repaint this window so a
            // light<->dark switch shows up straight away instead of on next open.
            Theme.Apply(this);
            Invalidate(true);
        }
    }

    private void OnExport(object? sender, EventArgs e)
    {
        if (_store.Config.Rules.Count == 0)
        {
            MessageBox.Show(this, "There are no rules to export yet.", "MonitorPin", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }
        using var dlg = new SaveFileDialog { Filter = RuleIo.FileFilter, FileName = RuleIo.DefaultName };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            RuleIo.Export(dlg.FileName, _store.Config.Rules);
            MessageBox.Show(this, $"Exported {_store.Config.Rules.Count} rule(s).", "MonitorPin", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Couldn't save the file:\n{ex.Message}", "MonitorPin", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void OnImport(object? sender, EventArgs e)
    {
        using var dlg = new OpenFileDialog { Filter = RuleIo.FileFilter };
        if (dlg.ShowDialog(this) != DialogResult.OK) return;

        List<Rule> incoming;
        try { incoming = RuleIo.Import(dlg.FileName); }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"That file couldn't be read as MonitorPin rules:\n{ex.Message}", "MonitorPin", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }
        if (incoming.Count == 0)
        {
            MessageBox.Show(this, "That file had no rules in it.", "MonitorPin", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Add to the current rules, or replace them?
        var choice = MessageBox.Show(this,
            $"Found {incoming.Count} rule(s).\n\nYes  = add them to your current rules\nNo   = replace your current rules\nCancel = do nothing",
            "Import rules", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (choice == DialogResult.Cancel) return;

        if (choice == DialogResult.No) _store.Config.Rules.Clear();
        _store.Config.Rules.AddRange(incoming);
        Persist();
        RefreshList();
    }

    private void Persist()
    {
        _store.Save();
        Changed?.Invoke();
    }
}
