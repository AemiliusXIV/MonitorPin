using System.Windows.Forms;
using MonitorPin.Diagnostics;
using MonitorPin.Startup;
using MonitorPin.UI;

namespace MonitorPin;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Always-on file logging for dev/testing: --devlog flag or MONITORPIN_DEVLOG=1
        // (also on for DEBUG builds). Shipped copies pass neither, so nothing hits disk.
        bool devLog = args.Contains("--devlog", StringComparer.OrdinalIgnoreCase)
                      || Environment.GetEnvironmentVariable("MONITORPIN_DEVLOG") == "1";
#if DEBUG
        devLog = true;
#endif
        if (devLog) Log.EnableDevLogging();

        // Elevated one-off actions used by the startup toggle (relaunch-as-admin).
        if (args.Length > 0)
        {
            string exe = Environment.ProcessPath ?? Application.ExecutablePath;
            switch (args[0])
            {
                case "--install-task":
                    Environment.Exit(StartupInstaller.Install(exe) ? 0 : 1);
                    return;
                case "--uninstall-task":
                    Environment.Exit(StartupInstaller.Uninstall() ? 0 : 1);
                    return;
            }
        }

        // A desktop shortcut can ask for a saved layout: --layout "Work"
        string? layout = null;
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i].Equals("--layout", StringComparison.OrdinalIgnoreCase))
                layout = args[i + 1];

        // Single instance. Note the access-denied case: when the logon task has
        // already started MonitorPin elevated, this normal-privilege launch can't
        // open that mutex at all. That means "already running", not an error, so
        // hand our request over rather than dying with an exception dialog.
        System.Threading.Mutex mutex;
        bool isNew;
        try
        {
            mutex = new System.Threading.Mutex(true, "MonitorPin.SingleInstance", out isNew);
        }
        catch (UnauthorizedAccessException)
        {
            HandOff(layout);
            return;
        }

        using (mutex)
        {
            if (!isNew)
            {
                HandOff(layout);
                return;
            }

            // Best-effort global marker purely so the installer's AppMutex check
            // can see us and close us before replacing the exe. Creating a Global\
            // object needs a privilege standard users don't have, so failure is
            // expected and harmless; the session mutex above covers the usual case.
            System.Threading.Mutex? globalMutex = null;
            try { globalMutex = new System.Threading.Mutex(true, @"Global\MonitorPin.SingleInstance", out _); }
            catch { /* not elevated, or no privilege */ }

            // --show: open the window on start (manual launch / installer / shortcut).
            // The logon task launches without it, so a normal sign-in stays silent.
            RunApp(args.Contains("--show", StringComparer.OrdinalIgnoreCase), layout);
            globalMutex?.Dispose();
        }
    }

    /// <summary>Give our request to the copy that's already running, then get out of the way.</summary>
    private static void HandOff(string? layout)
    {
        if (layout != null) SingleInstanceWindow.TrySendCommand("layout:" + layout);
        else SingleInstanceWindow.TryWakeExisting();
    }

    private static void RunApp(bool showOnStartup, string? layout)
    {
        ApplicationConfiguration.Initialize();
        Diagnostics.CrashHandler.Install();
        Application.Run(new TrayApplicationContext(showOnStartup, layout));
    }
}
