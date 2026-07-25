using System.Drawing;
using System.Windows.Forms;

namespace MonitorPin.UI;

/// <summary>A tiny themed "type a name" dialog.</summary>
public static class Prompt
{
    public static string? Text(IWin32Window owner, string title, string message, string initial = "")
    {
        using var f = new Form
        {
            Text = title,
            Icon = Util.AppIconFactory.Shared,
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterParent,
            MaximizeBox = false,
            MinimizeBox = false,
            ClientSize = new Size(360, 116),
        };
        var lbl = new Label { Text = message, Left = 12, Top = 14, Width = 336, AutoSize = false, Height = 18 };
        var box = new TextBox { Left = 12, Top = 38, Width = 336, Text = initial };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Left = 180, Top = 74, Width = 80, Height = 28 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Left = 268, Top = 74, Width = 80, Height = 28 };
        f.Controls.AddRange(new Control[] { lbl, box, ok, cancel });
        f.AcceptButton = ok;
        f.CancelButton = cancel;
        f.Load += (_, _) => Theme.Apply(f);
        box.SelectAll();

        return f.ShowDialog(owner) == DialogResult.OK && box.Text.Trim().Length > 0 ? box.Text.Trim() : null;
    }
}
