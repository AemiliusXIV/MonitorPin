using System.Diagnostics;
using System.Drawing;
using System.Text;
using MonitorPin.Interop;
using MonitorPin.Monitors;
using MonitorPin.Rules;

namespace MonitorPin.Core;

/// <summary>
/// The concrete window actions: move to a monitor, set state, force foreground,
/// force-minimize. All the Win32 sequencing lives here.
/// </summary>
public static class WindowController
{
    /// <summary>
    /// Move/size a window per a rule. Returns true if it actually changed the
    /// window's placement (false = already correct, left untouched — this is what
    /// stops the re-apply passes from flickering a window that's already right).
    /// </summary>
    public static bool Apply(IntPtr hwnd, MonitorEntry monitor, TargetState state, SizeSpec? size, bool bringToFront)
    {
        if (!NativeMethods.IsWindow(hwnd)) return false;

        bool changed = false;
        switch (state)
        {
            case TargetState.Minimized:
                if (!IsMinimized(hwnd))
                {
                    // Put it on the target screen first, so restoring brings it back
                    // where the rule says, then send it to the taskbar.
                    RestoreIfNeeded(hwnd);
                    PlaceInsideMonitor(hwnd, monitor, fillWorkArea: false);
                    NativeMethods.ShowWindow(hwnd, NativeMethods.SW_MINIMIZE);
                    changed = true;
                }
                return changed; // never pull a minimized window forward

            case TargetState.Maximized:
                if (!(IsMaximized(hwnd) && IsOnMonitor(hwnd, monitor)))
                {
                    RestoreIfNeeded(hwnd);
                    PlaceInsideMonitor(hwnd, monitor, fillWorkArea: false);
                    NativeMethods.ShowWindow(hwnd, NativeMethods.SW_MAXIMIZE);
                    changed = true;
                }
                break;

            case TargetState.Normal:
                if (!(IsRestored(hwnd) && IsOnMonitor(hwnd, monitor)))
                {
                    RestoreIfNeeded(hwnd);
                    PlaceInsideMonitor(hwnd, monitor, fillWorkArea: false);
                    changed = true;
                }
                break;

            case TargetState.CustomSize:
                if (!IsCustomSatisfied(hwnd, monitor, size))
                {
                    RestoreIfNeeded(hwnd);
                    ApplyCustomSize(hwnd, monitor, size);
                    changed = true;
                }
                break;
        }

        if (bringToFront)
            ForceForeground(hwnd);

        return changed;
    }

    private static bool IsOnMonitor(IntPtr hwnd, MonitorEntry monitor)
        => NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST) == monitor.Handle;

    private static int ShowState(IntPtr hwnd)
    {
        var wp = new WINDOWPLACEMENT { length = System.Runtime.InteropServices.Marshal.SizeOf<WINDOWPLACEMENT>() };
        return NativeMethods.GetWindowPlacement(hwnd, ref wp) ? wp.showCmd : 0;
    }

    private static bool IsMaximized(IntPtr hwnd) => ShowState(hwnd) == NativeMethods.SW_MAXIMIZE;
    private static bool IsMinimized(IntPtr hwnd) => ShowState(hwnd) == NativeMethods.SW_SHOWMINIMIZED;
    private static bool IsRestored(IntPtr hwnd) => ShowState(hwnd) == NativeMethods.SW_SHOWNORMAL;

    /// <summary>Coarse window state, for saving a layout.</summary>
    public static TargetState GetState(IntPtr hwnd) => ShowState(hwnd) switch
    {
        NativeMethods.SW_SHOWMINIMIZED => TargetState.Minimized,
        NativeMethods.SW_MAXIMIZE => TargetState.Maximized,
        _ => TargetState.Normal,
    };

    /// <summary>Restore a window to an exact rect (monitor-relative) and state.</summary>
    public static void PlaceExact(IntPtr hwnd, MonitorEntry monitor, TargetState state, int x, int y, int w, int h)
    {
        if (!NativeMethods.IsWindow(hwnd)) return;

        if (state == TargetState.Minimized)
        {
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_MINIMIZE);
            return;
        }

        RestoreIfNeeded(hwnd);
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOP,
            monitor.Bounds.Left + x, monitor.Bounds.Top + y, w, h,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);

        if (state == TargetState.Maximized)
            NativeMethods.ShowWindow(hwnd, NativeMethods.SW_MAXIMIZE);
    }

    private static bool IsCustomSatisfied(IntPtr hwnd, MonitorEntry monitor, SizeSpec? size)
    {
        if (size == null) return false;
        if (IsMaximized(hwnd) || IsMinimized(hwnd)) return false;
        if (!NativeMethods.GetWindowRect(hwnd, out var r)) return false;
        int wantX = monitor.Bounds.Left + size.X;
        int wantY = monitor.Bounds.Top + size.Y;
        const int tol = 4; // ignore sub-pixel / border rounding
        return Math.Abs(r.Left - wantX) <= tol && Math.Abs(r.Top - wantY) <= tol
            && Math.Abs(r.Width - size.Width) <= tol && Math.Abs(r.Height - size.Height) <= tol;
    }

    private static void RestoreIfNeeded(IntPtr hwnd)
    {
        var wp = new WINDOWPLACEMENT { length = System.Runtime.InteropServices.Marshal.SizeOf<WINDOWPLACEMENT>() };
        if (NativeMethods.GetWindowPlacement(hwnd, ref wp))
        {
            if (wp.showCmd == NativeMethods.SW_SHOWMINIMIZED || wp.showCmd == NativeMethods.SW_MAXIMIZE)
                NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);
        }
    }

    private static void PlaceInsideMonitor(IntPtr hwnd, MonitorEntry monitor, bool fillWorkArea)
    {
        var work = monitor.WorkArea;
        if (fillWorkArea)
        {
            NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOP,
                work.Left, work.Top, work.Width, work.Height,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
            return;
        }

        // Keep current size, center it on the target monitor so a following
        // maximize lands on the right screen.
        if (!NativeMethods.GetWindowRect(hwnd, out var r)) return;
        int w = Math.Min(r.Width, work.Width);
        int h = Math.Min(r.Height, work.Height);
        int x = work.Left + Math.Max(0, (work.Width - w) / 2);
        int y = work.Top + Math.Max(0, (work.Height - h) / 2);
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOP, x, y, w, h,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    private static void ApplyCustomSize(IntPtr hwnd, MonitorEntry monitor, SizeSpec? size)
    {
        if (size == null)
        {
            PlaceInsideMonitor(hwnd, monitor, fillWorkArea: false);
            return;
        }
        // Size offsets are stored relative to the target monitor's top-left.
        int x = monitor.Bounds.Left + size.X;
        int y = monitor.Bounds.Top + size.Y;
        NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOP, x, y, size.Width, size.Height,
            NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
    }

    /// <summary>
    /// The "opens in the background" fix. Windows resists a plain
    /// SetForegroundWindow, so we attach input threads and nudge past the lock.
    /// </summary>
    public static void ForceForeground(IntPtr hwnd)
    {
        if (!NativeMethods.IsWindow(hwnd)) return;

        IntPtr fg = NativeMethods.GetForegroundWindow();
        if (fg == hwnd) return;

        uint fgThread = NativeMethods.GetWindowThreadProcessId(fg, out _);
        uint thisThread = NativeMethods.GetCurrentThreadId();

        NativeMethods.AllowSetForegroundWindow(NativeMethods.ASFW_ANY);

        bool attached = false;
        if (fgThread != 0 && fgThread != thisThread)
            attached = NativeMethods.AttachThreadInput(thisThread, fgThread, true);

        // ALT tap releases the foreground lock in most cases.
        NativeMethods.keybd_event(NativeMethods.VK_MENU, 0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VK_MENU, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);

        NativeMethods.BringWindowToTop(hwnd);
        NativeMethods.SetForegroundWindow(hwnd);

        if (attached)
            NativeMethods.AttachThreadInput(thisThread, fgThread, false);
    }

    /// <summary>Force-minimize even a non-responsive fullscreen window.</summary>
    public static void ForceMinimize(IntPtr hwnd)
    {
        if (!NativeMethods.IsWindow(hwnd)) return;
        NativeMethods.ShowWindow(hwnd, NativeMethods.SW_FORCEMINIMIZE);
    }

    /// <summary>
    /// Move a window from one monitor to another, keeping its relative position
    /// and its maximized/normal state, then bring it forward.
    /// </summary>
    public static void MoveToMonitor(IntPtr hwnd, MonitorEntry from, MonitorEntry to)
    {
        if (!NativeMethods.IsWindow(hwnd) || from.Handle == to.Handle) return;

        bool wasMax = IsMaximized(hwnd);
        if (wasMax) NativeMethods.ShowWindow(hwnd, NativeMethods.SW_RESTORE);

        if (NativeMethods.GetWindowRect(hwnd, out var r))
        {
            int newX = to.Bounds.Left + (r.Left - from.Bounds.Left);
            int newY = to.Bounds.Top + (r.Top - from.Bounds.Top);
            // Keep a grabbable strip on-screen.
            newX = Math.Min(newX, to.WorkArea.Right - 120);
            newY = Math.Min(newY, to.WorkArea.Bottom - 40);
            NativeMethods.SetWindowPos(hwnd, NativeMethods.HWND_TOP, newX, newY, r.Width, r.Height,
                NativeMethods.SWP_NOZORDER | NativeMethods.SWP_NOACTIVATE);
        }

        if (wasMax) NativeMethods.ShowWindow(hwnd, NativeMethods.SW_MAXIMIZE);
        ForceForeground(hwnd);
    }

    // ---- Introspection helpers --------------------------------------------

    /// <summary>
    /// True for secondary windows: login pop-ups, dialogs, pickers. They're owned
    /// by a main window, and a rule meant for the app itself shouldn't reshape them.
    /// </summary>
    public static bool IsOwnedPopup(IntPtr hwnd)
        => NativeMethods.GetWindow(hwnd, NativeMethods.GW_OWNER) != IntPtr.Zero;

    /// <summary>False for fixed-size windows, which can't meaningfully be maximized.</summary>
    public static bool CanMaximize(IntPtr hwnd)
    {
        long style = (uint)NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_STYLE);
        return (style & NativeMethods.WS_MAXIMIZEBOX) != 0;
    }

    public static bool IsEligibleTopLevel(IntPtr hwnd)
    {
        if (!NativeMethods.IsWindow(hwnd)) return false;
        if (NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT) != hwnd) return false;
        if (!NativeMethods.IsWindowVisible(hwnd)) return false;

        long style = (uint)NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_STYLE);
        if ((style & NativeMethods.WS_CHILD) != 0) return false;

        long ex = (uint)NativeMethods.GetWindowLong(hwnd, NativeMethods.GWL_EXSTYLE);
        bool toolWindow = (ex & NativeMethods.WS_EX_TOOLWINDOW) != 0;
        bool appWindow = (ex & NativeMethods.WS_EX_APPWINDOW) != 0;
        if (toolWindow && !appWindow) return false;

        return true;
    }

    public static string GetProcessName(IntPtr hwnd)
    {
        NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
        if (pid == 0) return "";
        try
        {
            using var p = Process.GetProcessById((int)pid);
            return p.ProcessName; // no extension
        }
        catch
        {
            return "";
        }
    }

    public static string GetTitle(IntPtr hwnd)
    {
        int len = NativeMethods.GetWindowTextLength(hwnd);
        if (len <= 0) return "";
        var sb = new StringBuilder(len + 1);
        NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static Rectangle GetBounds(IntPtr hwnd)
        => NativeMethods.GetWindowRect(hwnd, out var r) ? r.ToRectangle() : Rectangle.Empty;
}
