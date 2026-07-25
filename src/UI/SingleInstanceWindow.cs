using System.Runtime.InteropServices;
using System.Windows.Forms;
using MonitorPin.Interop;

namespace MonitorPin.UI;

/// <summary>
/// A hidden top-level window that a second launch can find and poke, so clicking
/// the shortcut while MonitorPin is already running opens its window instead of
/// doing nothing.
///
/// It has to be a real top-level window (not message-only) so FindWindow can see
/// it, and it explicitly allows the wake-up message through UIPI: when the
/// running instance was started elevated by the logon task, a normal-privilege
/// click would otherwise be silently dropped.
/// </summary>
internal sealed class SingleInstanceWindow : NativeWindow, IDisposable
{
    // Found by window title, not class name: WinForms' NativeWindow expects
    // CreateParams.ClassName to name an *existing* class to subclass, so an
    // invented name fails with "Window class name is not valid" (error 1411).
    // A unique caption on an otherwise ordinary hidden window works fine.
    private const string WindowTitle = "MonitorPin_IpcWindow_7F3A2B";
    private const string MessageName = "MonitorPin_ShowWindow";

    /// <summary>Registered message id; identical in every process on this desktop.</summary>
    public static readonly uint ShowMessage = NativeMethods.RegisterWindowMessage(MessageName);

    public event Action? ShowRequested;

    /// <summary>A command string handed over by a second launch (e.g. "layout:Work").</summary>
    public event Action<string>? CommandReceived;

    public SingleInstanceWindow()
    {
        CreateHandle(new CreateParams
        {
            Caption = WindowTitle,
            X = 0, Y = 0, Width = 0, Height = 0,
            Style = 0,            // not visible, no frame
            ExStyle = 0x00000080, // WS_EX_TOOLWINDOW: keep it off the taskbar
        });

        // Let both messages through UIPI: a shortcut runs at normal privilege but
        // the running copy is usually elevated, and messages would be dropped.
        if (ShowMessage != 0)
            NativeMethods.ChangeWindowMessageFilterEx(Handle, ShowMessage, NativeMethods.MSGFLT_ALLOW, IntPtr.Zero);
        NativeMethods.ChangeWindowMessageFilterEx(Handle, NativeMethods.WM_COPYDATA, NativeMethods.MSGFLT_ALLOW, IntPtr.Zero);
    }

    /// <summary>Hand a command to the running instance. False if there isn't one.</summary>
    public static bool TrySendCommand(string command)
    {
        IntPtr hwnd = NativeMethods.FindWindow(null, WindowTitle);
        if (hwnd == IntPtr.Zero) return false;

        IntPtr buffer = Marshal.StringToHGlobalUni(command);
        try
        {
            var cds = new NativeMethods.COPYDATASTRUCT
            {
                dwData = IntPtr.Zero,
                cbData = (command.Length + 1) * 2,
                lpData = buffer,
            };
            NativeMethods.SendMessage(hwnd, NativeMethods.WM_COPYDATA, IntPtr.Zero, ref cds);
            return true;
        }
        finally { Marshal.FreeHGlobal(buffer); }
    }

    /// <summary>Called by a second launch: wake the running instance, if there is one.</summary>
    public static bool TryWakeExisting()
    {
        IntPtr hwnd = NativeMethods.FindWindow(null, WindowTitle);
        if (hwnd == IntPtr.Zero || ShowMessage == 0) return false;
        return NativeMethods.PostMessage(hwnd, ShowMessage, IntPtr.Zero, IntPtr.Zero);
    }

    protected override void WndProc(ref Message m)
    {
        if (m.Msg == ShowMessage)
        {
            ShowRequested?.Invoke();
            return;
        }
        if (m.Msg == NativeMethods.WM_COPYDATA)
        {
            var cds = Marshal.PtrToStructure<NativeMethods.COPYDATASTRUCT>(m.LParam);
            string? text = cds.lpData != IntPtr.Zero ? Marshal.PtrToStringUni(cds.lpData) : null;
            if (!string.IsNullOrEmpty(text)) CommandReceived?.Invoke(text);
            m.Result = IntPtr.Zero;
            return;
        }
        base.WndProc(ref m);
    }

    public void Dispose() => DestroyHandle();
}
