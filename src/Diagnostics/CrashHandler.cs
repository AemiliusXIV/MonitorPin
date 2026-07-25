using System.Text;
using System.Windows.Forms;

namespace MonitorPin.Diagnostics;

/// <summary>
/// Catches unhandled exceptions, writes a crash report next to the logs, and
/// tells the user where it is so they can send it. Everything stays local; the
/// app never phones home.
/// </summary>
public static class CrashHandler
{
    private static bool _handling;

    public static void Install()
    {
        // Route WinForms UI-thread exceptions to us instead of the default dialog.
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => Handle(e.Exception, fatal: false);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Handle(e.ExceptionObject as Exception, fatal: true);
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            Log.Line($"[task-error] {e.Exception.GetBaseException().Message}");
            e.SetObserved();
        };
    }

    private static void Handle(Exception? ex, bool fatal)
    {
        if (ex == null || _handling) { if (fatal) Environment.Exit(1); return; }
        _handling = true;
        try
        {
            string path = Write(ex);
            Log.Line($"[crash] {ex.GetType().Name}: {ex.Message} -> {path}");
            Notify(path, fatal);
        }
        catch { /* never let the crash handler crash */ }
        finally
        {
            if (fatal) Environment.Exit(1);
            _handling = false; // a caught UI-thread exception may not be fatal
        }
    }

    private static string Write(Exception ex)
    {
        Directory.CreateDirectory(Log.LogDir());
        string path = Path.Combine(Log.LogDir(), $"crash-{DateTime.Now:yyyyMMdd-HHmmss}.log");

        var v = typeof(CrashHandler).Assembly.GetName().Version;
        var sb = new StringBuilder();
        sb.AppendLine($"MonitorPin crash report - {DateTime.Now.ToString(System.Globalization.CultureInfo.CurrentCulture)}");
        sb.AppendLine($"Version : {v?.ToString(3)}");
        sb.AppendLine($"Windows : {Environment.OSVersion.Version} (64-bit OS: {Environment.Is64BitOperatingSystem})");
        sb.AppendLine($".NET    : {Environment.Version}");
        sb.AppendLine(Log.ReportContentsNotice);
        sb.AppendLine(new string('-', 72));
        sb.AppendLine("Error:");
        sb.AppendLine(ex.ToString()); // full stack, including inner exceptions
        sb.AppendLine();
        sb.AppendLine("Recent activity:");
        foreach (var line in Log.Snapshot()) sb.AppendLine(line);

        File.WriteAllText(path, sb.ToString());
        return path;
    }

    private static void Notify(string path, bool fatal)
    {
        try
        {
            string msg = "MonitorPin ran into a problem"
                + (fatal ? " and has to close.\n\n" : ".\n\n")
                + "A report was saved so you can send it to the developer:\n"
                + path + "\n\nOpen it now?";
            if (MessageBox.Show(msg, "MonitorPin", MessageBoxButtons.YesNo, MessageBoxIcon.Error) == DialogResult.Yes)
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch { }
    }
}
