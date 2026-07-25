using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace MonitorPin.Util;

/// <summary>
/// Draws MonitorPin's own icon (a monitor with a location pin) at runtime, so we
/// don't ship a binary asset. Also writes a multi-size .ico for the exe/installer.
/// </summary>
public static class AppIconFactory
{
    private static Icon? _shared;

    /// <summary>Cached icon for the tray and windows.</summary>
    public static Icon Shared => _shared ??= Create(32);

    public static Icon Create(int size)
    {
        using var bmp = DrawBitmap(size);
        return Icon.FromHandle(bmp.GetHicon());
    }

    private static Bitmap DrawBitmap(int s)
    {
        var bmp = new Bitmap(s, s, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.SmoothingMode = SmoothingMode.AntiAlias;
        g.Clear(Color.Transparent);

        var accent = Color.FromArgb(0x2D, 0x7D, 0xF6); // blue
        var screen = Color.FromArgb(0x14, 0x18, 0x25); // near-black screen
        var pin = Color.FromArgb(0xFF, 0x5A, 0x5F);    // red pin

        float m = s * 0.08f;
        var body = new RectangleF(m, m * 1.4f, s - 2 * m, (s - 2 * m) * 0.70f);
        float r = s * 0.12f;

        using (var b = new SolidBrush(accent))
            FillRounded(g, body, r, b);

        var inner = RectangleF.Inflate(body, -s * 0.075f, -s * 0.075f);
        using (var b = new SolidBrush(screen))
            FillRounded(g, inner, r * 0.5f, b);

        // Stand + base
        float standW = body.Width * 0.10f, standH = s * 0.10f;
        var stand = new RectangleF(body.Left + body.Width / 2 - standW / 2, body.Bottom, standW, standH);
        var foot = new RectangleF(body.Left + body.Width * 0.28f, stand.Bottom, body.Width * 0.44f, s * 0.055f);
        using (var b = new SolidBrush(accent))
        {
            g.FillRectangle(b, stand);
            FillRounded(g, foot, foot.Height / 2, b);
        }

        // Location pin on the screen
        float pinD = s * 0.30f;
        float px = inner.Left + inner.Width * 0.5f - pinD / 2;
        float py = inner.Top + inner.Height * 0.16f;
        using (var pb = new SolidBrush(pin))
        {
            g.FillEllipse(pb, px, py, pinD, pinD);
            var tip = new[]
            {
                new PointF(px + pinD * 0.5f, py + pinD * 1.15f),
                new PointF(px + pinD * 0.18f, py + pinD * 0.72f),
                new PointF(px + pinD * 0.82f, py + pinD * 0.72f),
            };
            g.FillPolygon(pb, tip);
        }
        using (var wb = new SolidBrush(screen))
            g.FillEllipse(wb, px + pinD * 0.32f, py + pinD * 0.26f, pinD * 0.36f, pinD * 0.36f);

        return bmp;
    }

    private static void FillRounded(Graphics g, RectangleF rect, float radius, Brush brush)
    {
        using var path = new GraphicsPath();
        float d = radius * 2;
        path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
        path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
        path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        g.FillPath(brush, path);
    }

    /// <summary>Write a PNG-based .ico with the usual sizes (used for the exe icon).</summary>
    public static void SaveIco(string path)
    {
        int[] sizes = { 16, 32, 48, 256 };
        var pngs = new List<byte[]>();
        foreach (int s in sizes)
        {
            using var bmp = DrawBitmap(s);
            using var ms = new MemoryStream();
            bmp.Save(ms, ImageFormat.Png);
            pngs.Add(ms.ToArray());
        }

        using var fs = new FileStream(path, FileMode.Create);
        using var w = new BinaryWriter(fs);
        w.Write((short)0);            // reserved
        w.Write((short)1);            // type = icon
        w.Write((short)sizes.Length); // image count

        int offset = 6 + 16 * sizes.Length;
        for (int i = 0; i < sizes.Length; i++)
        {
            int s = sizes[i];
            w.Write((byte)(s >= 256 ? 0 : s)); // width
            w.Write((byte)(s >= 256 ? 0 : s)); // height
            w.Write((byte)0);                  // palette
            w.Write((byte)0);                  // reserved
            w.Write((short)1);                 // planes
            w.Write((short)32);                // bpp
            w.Write(pngs[i].Length);           // size in bytes
            w.Write(offset);                   // offset
            offset += pngs[i].Length;
        }
        foreach (var png in pngs) w.Write(png);
    }
}
