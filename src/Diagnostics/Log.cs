namespace MonitorPin.Diagnostics;

/// <summary>
/// Lightweight event log. Keeps a rolling in-memory buffer of recent lines, and
/// optionally mirrors to a file. Two file modes:
///   - "dev always-on": enabled by a --devlog flag / env var (or a DEBUG build),
///     so we can watch output live while testing.
///   - "capture": user-started from the UI to record a repro; off by default.
/// On a shipped build with no flag, nothing touches disk until the user saves a
/// report or starts a capture.
/// </summary>
public static class Log
{
    private const int Capacity = 1000;

    private static readonly LinkedList<string> _buffer = new();
    private static readonly object _lock = new();

    private static string? _devFile;      // always-on dev logging

    public static bool DevLoggingOn => _devFile != null;

    /// <summary>
    /// What a saved report actually contains. Shown to the user before they share
    /// one, which is more honest (and more useful) than a toggle that quietly makes
    /// reports useless for the troubleshooting they exist for.
    /// </summary>
    public const string ReportContentsNotice =
        "The report lists what MonitorPin did recently: the app names and window titles of "
        + "windows your rules matched, which screen they were moved to, and any window you "
        + "force-minimized. It does not contain keystrokes, passwords, or anything you typed, "
        + "and it does not list windows that no rule matched.";

    public static string LogDir() => System.IO.Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MonitorPin", "logs");

    /// <summary>A copy of the recent activity lines, for a crash report.</summary>
    public static IReadOnlyList<string> Snapshot()
    {
        lock (_lock) return _buffer.ToList();
    }

    /// <summary>Turn on always-on file logging (dev/testing). Idempotent.</summary>
    public static void EnableDevLogging()
    {
        lock (_lock)
        {
            if (_devFile != null) return;
            Directory.CreateDirectory(LogDir());
            _devFile = System.IO.Path.Combine(LogDir(), $"dev-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        }
        Line("[log] dev logging enabled");
    }

    public static void Line(string msg)
    {
        string entry = $"{DateTime.Now:HH:mm:ss.fff}  {msg}";
        lock (_lock)
        {
            _buffer.AddLast(entry);
            while (_buffer.Count > Capacity) _buffer.RemoveFirst();

            TryAppend(_devFile, entry);
        }
        System.Diagnostics.Debug.WriteLine(entry);
    }

    /// <summary>Format a window for a log line.</summary>
    public static string Win(string process, string title)
        => !string.IsNullOrEmpty(title) ? $"{process} \"{title}\"" : process;

    /// <summary>Flush the current buffer to a timestamped report file; returns its path.</summary>
    public static string SaveReport()
    {
        Directory.CreateDirectory(LogDir());
        string path = System.IO.Path.Combine(LogDir(), $"report-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        var header = new[]
        {
            $"MonitorPin report - {DateTime.Now.ToString(System.Globalization.CultureInfo.CurrentCulture)}",
            ReportContentsNotice,
            new string('-', 72),
        };
        lock (_lock)
        {
            File.WriteAllLines(path, header.Concat(_buffer));
        }
        return path;
    }

    private static void TryAppend(string? file, string line)
    {
        if (file == null) return;
        try { File.AppendAllText(file, line + Environment.NewLine); } catch { /* best effort */ }
    }
}
