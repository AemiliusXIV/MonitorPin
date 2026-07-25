using System.Drawing;
using System.Windows.Forms;
using MonitorPin.Monitors;
using MonitorPin.Rules;

namespace MonitorPin.UI;

/// <summary>Choose how screens are named, and set a custom name per screen.</summary>
public sealed class MonitorsForm : Form
{
    private readonly RuleStore _store;
    private readonly MonitorCatalog _catalog;
    private readonly ToolTip _tips = new() { AutoPopDelay = 12000, InitialDelay = 400, ReshowDelay = 200 };

    private readonly ComboBox _style = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Dictionary<string, TextBox> _aliasBoxes = new();
    private readonly Dictionary<string, Label> _previewLabels = new();

    public MonitorsForm(RuleStore store, MonitorCatalog catalog)
    {
        _store = store;
        _catalog = catalog;

        Text = "Monitor names";
        Icon = Util.AppIconFactory.Shared;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;

        BuildLayout();
        _style.SelectedIndex = (int)store.Config.MonitorNaming;
        UpdatePreviews();
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Theme.Apply(this);
    }

    private void BuildLayout()
    {
        int x = 16, y = 16;

        Controls.Add(new Label { Text = "Show my screens as:", Left = x, Top = y + 3, AutoSize = true });
        _style.SetBounds(150, y, 200, 24);
        _style.Items.AddRange(new object[] { "Their position (left, right…)", "Their name (from Windows)", "A name I choose" });
        _style.SelectedIndexChanged += (_, _) => UpdatePreviews();
        _tips.SetToolTip(_style,
            "Position: names each screen by where it sits.\r\n"
            + "Name: uses the model name Windows reports for each monitor.\r\n"
            + "A name I choose: type your own name for each screen below.");
        Controls.Add(_style);
        y += 40;

        // Column headers
        Controls.Add(Header("Screen", x, y));
        Controls.Add(Header("Custom name", 190, y));
        Controls.Add(Header("Shown as", 330, y));
        y += 22;

        foreach (var m in _catalog.Entries)
        {
            string identity = string.IsNullOrEmpty(m.WindowsName)
                ? Cap(m.PositionLabel)
                : $"{Cap(m.PositionLabel)} - {m.WindowsName}";
            Controls.Add(new Label { Text = identity, Left = x, Top = y + 4, Width = 168, AutoEllipsis = true });

            var box = new TextBox { Left = 190, Top = y, Width = 130 };
            if (_store.Config.MonitorAliases.TryGetValue(m.HardwareKey, out var a)) box.Text = a;
            box.TextChanged += (_, _) => UpdatePreviews();
            _aliasBoxes[m.HardwareKey] = box;
            Controls.Add(box);

            var preview = new Label { Left = 330, Top = y + 4, Width = 150, AutoEllipsis = true };
            _previewLabels[m.HardwareKey] = preview;
            Controls.Add(preview);

            y += 30;
        }

        y += 8;
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 300, Top = y, Width = 84, Height = 30 };
        ok.Click += OnOk;
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 392, Top = y, Width = 84, Height = 30 };
        Controls.Add(ok);
        Controls.Add(cancel);
        AcceptButton = ok;
        CancelButton = cancel;

        ClientSize = new Size(492, y + 44);
    }

    private static Label Header(string text, int x, int y)
        => new() { Text = text, Left = x, Top = y, AutoSize = true, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold) };

    private void UpdatePreviews()
    {
        // Preview against a throwaway config reflecting the in-progress choices.
        var preview = new AppConfig
        {
            MonitorNaming = (MonitorNamingStyle)_style.SelectedIndex,
            MonitorAliases = _aliasBoxes.ToDictionary(kv => kv.Key, kv => kv.Value.Text),
        };
        foreach (var m in _catalog.Entries)
            if (_previewLabels.TryGetValue(m.HardwareKey, out var lbl))
                lbl.Text = MonitorNaming.Label(m, preview);
    }

    private void OnOk(object? sender, EventArgs e)
    {
        _store.Config.MonitorNaming = (MonitorNamingStyle)_style.SelectedIndex;
        _store.Config.MonitorAliases = _aliasBoxes
            .Where(kv => !string.IsNullOrWhiteSpace(kv.Value.Text))
            .ToDictionary(kv => kv.Key, kv => kv.Value.Text.Trim());
        _store.Save();
    }

    private static string Cap(string s) => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..];
}
