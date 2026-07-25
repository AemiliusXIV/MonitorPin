using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;
using MonitorPin.Interop;
using MonitorPin.Rules;

namespace MonitorPin.UI;

/// <summary>
/// Dark/light theming for the app's windows.
///
/// WinForms has no real dark mode on .NET 8, so this recolours controls by hand
/// and asks the shell to dark-style the bits that ignore BackColor (list view
/// headers, scrollbars) plus the title bar via DWM.
/// </summary>
public static class Theme
{
    public static bool IsDark { get; private set; }

    // Palette roughly matching Windows' own dark surfaces.
    public static Color Back => IsDark ? Color.FromArgb(0x20, 0x20, 0x20) : SystemColors.Control;
    public static Color Surface => IsDark ? Color.FromArgb(0x2B, 0x2B, 0x2B) : SystemColors.Window;
    public static Color Text => IsDark ? Color.FromArgb(0xF0, 0xF0, 0xF0) : SystemColors.ControlText;
    public static Color DimText => IsDark ? Color.FromArgb(0xA0, 0xA0, 0xA0) : SystemColors.GrayText;
    public static Color Control => IsDark ? Color.FromArgb(0x33, 0x33, 0x33) : SystemColors.Control;
    public static Color Border => IsDark ? Color.FromArgb(0x3F, 0x3F, 0x46) : SystemColors.ControlDark;

    /// <summary>Decide dark vs light from the setting, following Windows if asked to.</summary>
    public static void Resolve(AppearanceMode mode)
        => IsDark = mode switch
        {
            AppearanceMode.Dark => true,
            AppearanceMode.Light => false,
            _ => WindowsUsesDarkApps(),
        };

    private static bool WindowsUsesDarkApps()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int v && v == 0;
        }
        catch { return false; }
    }

    /// <summary>Apply the current theme to a form and everything inside it.</summary>
    public static void Apply(Form form)
    {
        form.BackColor = Back;
        form.ForeColor = Text;
        ApplyTitleBar(form);
        foreach (Control c in form.Controls) ApplyTo(c);
    }

    private static void ApplyTitleBar(Form form)
    {
        if (!form.IsHandleCreated) return;
        int on = IsDark ? 1 : 0;
        // Newer attribute first; older Windows 10 builds use 19.
        if (NativeMethods.DwmSetWindowAttribute(form.Handle, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int)) != 0)
            NativeMethods.DwmSetWindowAttribute(form.Handle, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE_OLD, ref on, sizeof(int));
    }

    private static void ApplyTo(Control c)
    {
        switch (c)
        {
            case ListView lv:
                lv.BackColor = Surface;
                lv.ForeColor = Text;
                lv.BorderStyle = BorderStyle.FixedSingle;
                // Grid lines are drawn in a fixed light colour, so on a dark list
                // they glare over every empty row. Drop them when dark.
                lv.GridLines = !IsDark;
                // Headers and scrollbars ignore BackColor; the shell theme handles them.
                if (lv.IsHandleCreated)
                    NativeMethods.SetWindowTheme(lv.Handle, IsDark ? "DarkMode_Explorer" : "Explorer", null);
                // SetWindowTheme drops the native image list, so the row icons vanish;
                // re-attach it.
                var img = lv.SmallImageList;
                if (img != null) { lv.SmallImageList = null; lv.SmallImageList = img; }
                break;

            case TextBox tb:
                tb.BackColor = IsDark ? Surface : SystemColors.Window;
                tb.ForeColor = Text;
                tb.BorderStyle = BorderStyle.FixedSingle;
                break;

            case ComboBox cb:
                cb.BackColor = IsDark ? Control : SystemColors.Window;
                cb.ForeColor = Text;
                cb.FlatStyle = IsDark ? FlatStyle.Flat : FlatStyle.Standard;
                break;

            case NumericUpDown nud:
                nud.BackColor = IsDark ? Control : SystemColors.Window;
                nud.ForeColor = Text;
                break;

            case Button b:
                b.BackColor = Control;
                b.ForeColor = Text;
                b.FlatStyle = IsDark ? FlatStyle.Flat : FlatStyle.Standard;
                b.FlatAppearance.BorderColor = Border;
                break;

            case GroupBox gb:
                gb.ForeColor = Text;
                gb.BackColor = Back;
                // The default etched border draws light in dark mode; flat is subtler.
                gb.FlatStyle = IsDark ? FlatStyle.Flat : FlatStyle.System;
                break;

            case MenuStrip ms:
                ms.BackColor = Back;
                ms.ForeColor = Text;
                // Default renderer in light mode; only swap to the dark colours when dark.
                ms.Renderer = IsDark
                    ? new ToolStripProfessionalRenderer(new DarkColors())
                    : new ToolStripProfessionalRenderer();
                // Item text colour isn't inherited, so set it on each item and submenu.
                ThemeMenuItems(ms.Items);
                break;

            case Label lbl:
                // Keep intentionally-dimmed labels dim, just readable on dark.
                lbl.ForeColor = lbl.ForeColor == SystemColors.GrayText || lbl.ForeColor == DimText ? DimText : Text;
                lbl.BackColor = Color.Transparent;
                break;

            case ProgressBar:
                break; // themed by the OS; leave alone

            default:
                c.BackColor = Back;
                c.ForeColor = Text;
                break;
        }

        foreach (Control child in c.Controls) ApplyTo(child);
    }

    private static void ThemeMenuItems(ToolStripItemCollection items)
    {
        foreach (ToolStripItem it in items)
        {
            it.ForeColor = Text;
            it.BackColor = IsDark ? Surface : SystemColors.Control;
            if (it is ToolStripMenuItem mi && mi.HasDropDownItems)
                ThemeMenuItems(mi.DropDownItems);
        }
    }

    /// <summary>Colours for menus, which don't follow BackColor on their own.</summary>
    private sealed class DarkColors : ProfessionalColorTable
    {
        public override Color MenuItemSelected => Color.FromArgb(0x3D, 0x3D, 0x3D);
        public override Color MenuItemBorder => Color.FromArgb(0x50, 0x50, 0x50);
        public override Color MenuBorder => Color.FromArgb(0x50, 0x50, 0x50);
        public override Color MenuItemSelectedGradientBegin => Color.FromArgb(0x3D, 0x3D, 0x3D);
        public override Color MenuItemSelectedGradientEnd => Color.FromArgb(0x3D, 0x3D, 0x3D);
        public override Color MenuItemPressedGradientBegin => Color.FromArgb(0x2B, 0x2B, 0x2B);
        public override Color MenuItemPressedGradientEnd => Color.FromArgb(0x2B, 0x2B, 0x2B);
        public override Color ToolStripDropDownBackground => Color.FromArgb(0x2B, 0x2B, 0x2B);
        public override Color ImageMarginGradientBegin => Color.FromArgb(0x2B, 0x2B, 0x2B);
        public override Color ImageMarginGradientMiddle => Color.FromArgb(0x2B, 0x2B, 0x2B);
        public override Color ImageMarginGradientEnd => Color.FromArgb(0x2B, 0x2B, 0x2B);
        public override Color SeparatorDark => Color.FromArgb(0x50, 0x50, 0x50);
        public override Color SeparatorLight => Color.FromArgb(0x50, 0x50, 0x50);
    }
}
