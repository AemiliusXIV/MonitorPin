using System.Windows.Forms;
using MonitorPin.Interop;
using MonitorPin.Rules;

namespace MonitorPin.Hotkeys;

public enum HotkeyAction
{
    Minimize,
    NextMonitor,
    PrevMonitor,
    MinimizeApp,
    RestoreLayout,
}

/// <summary>
/// A message-only window that owns the app's global hotkeys and reports which
/// action fired.
/// </summary>
public sealed class HotkeyManager : NativeWindow, IDisposable
{
    private const int HwndMessage = -3;
    private const int BaseId = 0xB000;

    private readonly List<int> _registered = new();

    public event Action<HotkeyAction>? Pressed;

    public HotkeyManager()
    {
        CreateHandle(new CreateParams { Parent = new IntPtr(HwndMessage) });
    }

    /// <summary>Re-register the given hotkeys, dropping any previous ones.</summary>
    public void Register(IReadOnlyDictionary<HotkeyAction, HotkeySpec> specs)
    {
        UnregisterAll();
        foreach (var (action, spec) in specs)
        {
            if (spec is null || !spec.IsValid) continue;

            uint mods = NativeMethods.MOD_NOREPEAT;
            if (spec.Ctrl) mods |= NativeMethods.MOD_CONTROL;
            if (spec.Alt) mods |= NativeMethods.MOD_ALT;
            if (spec.Shift) mods |= NativeMethods.MOD_SHIFT;
            if (spec.Win) mods |= NativeMethods.MOD_WIN;

            int id = BaseId + (int)action;
            if (NativeMethods.RegisterHotKey(Handle, id, mods, spec.Key))
                _registered.Add(id);
        }
    }

    private void UnregisterAll()
    {
        foreach (int id in _registered)
            NativeMethods.UnregisterHotKey(Handle, id);
        _registered.Clear();
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == NativeMethods.WM_HOTKEY)
        {
            int id = m.WParam.ToInt32();
            if (id >= BaseId && id <= BaseId + (int)HotkeyAction.RestoreLayout)
            {
                Pressed?.Invoke((HotkeyAction)(id - BaseId));
                return;
            }
        }
        base.WndProc(ref m);
    }

    public void Dispose()
    {
        UnregisterAll();
        DestroyHandle();
    }
}
