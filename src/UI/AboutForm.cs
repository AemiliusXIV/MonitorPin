using System.Drawing;
using System.Windows.Forms;
using MonitorPin.Util;

namespace MonitorPin.UI;

public sealed class AboutForm : Form
{
    public AboutForm()
    {
        Text = "About MonitorPin";
        Icon = AppIconFactory.Shared;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(400, 210);

        var pic = new PictureBox
        {
            Image = AppIconFactory.Create(48).ToBitmap(),
            SizeMode = PictureBoxSizeMode.AutoSize,
            Left = 20, Top = 20,
        };
        Controls.Add(pic);

        var title = new Label
        {
            Text = $"MonitorPin {MainForm.VersionString}",
            Left = 84, Top = 22, AutoSize = true,
            Font = new Font(Font.FontFamily, 12f, FontStyle.Bold),
        };
        Controls.Add(title);

        var blurb = new Label
        {
            Left = 84, Top = 52, Width = 292, Height = 96,
            Text = "Sends your apps to the right screen and size when they open, "
                 + "and can force-minimize apps that won't minimize on their own.\r\n\r\n"
                 + "Runs quietly in the tray. Free to use.",
        };
        Controls.Add(blurb);

        var ok = new Button
        {
            Text = "OK", DialogResult = DialogResult.OK,
            Width = 84, Height = 30, Left = 296, Top = 166,
        };
        Controls.Add(ok);
        AcceptButton = ok;
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        Theme.Apply(this);
    }
}
