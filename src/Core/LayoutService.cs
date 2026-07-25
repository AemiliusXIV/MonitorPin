using MonitorPin.Diagnostics;
using MonitorPin.Interop;
using MonitorPin.Monitors;
using MonitorPin.Rules;
using MonitorPin.Util;

namespace MonitorPin.Core;

/// <summary>Snapshots the current window arrangement and puts it back.</summary>
public static class LayoutService
{
    /// <summary>Capture every eligible open window into a named layout.</summary>
    public static WindowLayout Capture(string name, MonitorCatalog catalog)
    {
        var layout = new WindowLayout { Name = name };

        NativeMethods.EnumWindows((h, _) =>
        {
            if (!WindowController.IsEligibleTopLevel(h)) return true;

            var mon = catalog.FromWindow(h);
            if (mon == null) return true;

            string proc = WindowController.GetProcessName(h);
            if (string.IsNullOrEmpty(proc)) return true;

            var b = WindowController.GetBounds(h);
            layout.Windows.Add(new LayoutWindow
            {
                Process = proc,
                DisplayName = AppInfo.FriendlyName(proc, null),
                Title = WindowController.GetTitle(h),
                MonitorKey = mon.HardwareKey,
                X = b.Left - mon.Bounds.Left,
                Y = b.Top - mon.Bounds.Top,
                Width = b.Width,
                Height = b.Height,
                State = WindowController.GetState(h),
            });
            return true;
        }, IntPtr.Zero);

        return layout;
    }

    /// <summary>Put the windows in a layout back where they were. Returns how many moved.</summary>
    public static int Restore(WindowLayout layout, MonitorCatalog catalog)
    {
        // Snapshot the open windows once, so each is used for at most one entry.
        var open = new List<IntPtr>();
        NativeMethods.EnumWindows((h, _) => { if (WindowController.IsEligibleTopLevel(h)) open.Add(h); return true; }, IntPtr.Zero);

        var used = new HashSet<IntPtr>();
        int moved = 0;

        foreach (var w in layout.Windows)
        {
            IntPtr target = FindWindow(open, used, w);
            if (target == IntPtr.Zero) continue;

            var mon = catalog.Resolve(new MonitorMatch { Mode = MonitorMatchMode.ByHardwareId, HardwareKey = w.MonitorKey })
                      ?? catalog.Entries.FirstOrDefault(e => e.IsPrimary);
            if (mon == null) continue;

            WindowController.PlaceExact(target, mon, w.State, w.X, w.Y, w.Width, w.Height);
            used.Add(target);
            moved++;
        }

        Log.Line($"[layout] restored '{layout.Name}': {moved}/{layout.Windows.Count} window(s)");
        return moved;
    }

    private static IntPtr FindWindow(List<IntPtr> open, HashSet<IntPtr> used, LayoutWindow w)
    {
        // Prefer a same-process window whose title matches; fall back to any of that process.
        IntPtr fallback = IntPtr.Zero;
        foreach (var h in open)
        {
            if (used.Contains(h)) continue;
            if (!WindowController.GetProcessName(h).Equals(w.Process, StringComparison.OrdinalIgnoreCase)) continue;

            if (!string.IsNullOrEmpty(w.Title) &&
                WindowController.GetTitle(h).Equals(w.Title, StringComparison.OrdinalIgnoreCase))
                return h;

            if (fallback == IntPtr.Zero) fallback = h;
        }
        return fallback;
    }
}
