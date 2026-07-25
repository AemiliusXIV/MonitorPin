using MonitorPin.Interop;

namespace MonitorPin.Core;

/// <summary>
/// Watches for windows appearing / gaining focus via SetWinEventHook. Callbacks
/// arrive on the thread that installs the hooks (the UI thread here), so the
/// engine can touch UI state directly without marshaling.
/// </summary>
public sealed class WinEventListener : IDisposable
{
    // Keep delegate referenced for the hook's lifetime, or the GC eats it.
    private readonly NativeMethods.WinEventProc _proc;
    private IntPtr _showHook;
    private IntPtr _foregroundHook;

    /// <summary>Raised for an eligible top-level window on SHOW or FOREGROUND.</summary>
    public event Action<IntPtr, uint>? WindowEvent;

    public WinEventListener()
    {
        _proc = OnWinEvent;
    }

    public void Start()
    {
        _showHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_OBJECT_SHOW, NativeMethods.EVENT_OBJECT_SHOW,
            IntPtr.Zero, _proc, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);

        _foregroundHook = NativeMethods.SetWinEventHook(
            NativeMethods.EVENT_SYSTEM_FOREGROUND, NativeMethods.EVENT_SYSTEM_FOREGROUND,
            IntPtr.Zero, _proc, 0, 0, NativeMethods.WINEVENT_OUTOFCONTEXT);
    }

    private void OnWinEvent(IntPtr hook, uint eventType, IntPtr hwnd,
        int idObject, int idChild, uint thread, uint time)
    {
        // Only real top-level window events, not child/caret/etc.
        if (hwnd == IntPtr.Zero) return;
        if (idObject != NativeMethods.OBJID_WINDOW || idChild != NativeMethods.CHILDID_SELF) return;

        if (!WindowController.IsEligibleTopLevel(hwnd)) return;

        WindowEvent?.Invoke(hwnd, eventType);
    }

    public void Dispose()
    {
        if (_showHook != IntPtr.Zero) NativeMethods.UnhookWinEvent(_showHook);
        if (_foregroundHook != IntPtr.Zero) NativeMethods.UnhookWinEvent(_foregroundHook);
        _showHook = _foregroundHook = IntPtr.Zero;
    }
}
