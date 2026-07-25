using System.Diagnostics;
using System.Drawing;

namespace MonitorPin.Util;

/// <summary>
/// Resolves a friendly display name and icon for an app, from its exe path or
/// process name. Results are cached; icons are extracted once per path.
/// </summary>
public static class AppInfo
{
    private static readonly Dictionary<string, string> NameCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, Icon?> IconCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Best readable name: exe FileDescription/ProductName, else a tidied process name.</summary>
    public static string FriendlyName(string process, string? exePath)
    {
        string proc = StripExe(process);
        exePath ??= ResolveExePath(proc);

        if (exePath != null && File.Exists(exePath))
        {
            if (NameCache.TryGetValue(exePath, out var cached)) return cached;
            string name = FromVersionInfo(exePath) ?? Prettify(proc);
            NameCache[exePath] = name;
            return name;
        }
        return Prettify(proc);
    }

    /// <summary>Small icon for the app, or null. Caller must not dispose (cached).</summary>
    public static Icon? IconFor(string process, string? exePath)
    {
        exePath ??= ResolveExePath(StripExe(process));
        if (exePath == null || !File.Exists(exePath)) return null;
        if (IconCache.TryGetValue(exePath, out var cached)) return cached;

        Icon? icon = null;
        try { icon = Icon.ExtractAssociatedIcon(exePath); }
        catch { /* some paths refuse; leave null */ }
        IconCache[exePath] = icon;
        return icon;
    }

    /// <summary>Full path of a running process by name, if we can read it.</summary>
    public static string? ResolveExePath(string processName)
    {
        string name = StripExe(processName);
        try
        {
            foreach (var p in Process.GetProcessesByName(name))
            {
                try { if (p.MainModule?.FileName is { } f) return f; }
                catch { /* access denied for some; try next */ }
            }
        }
        catch { }
        return null;
    }

    private static string? FromVersionInfo(string exePath)
    {
        try
        {
            var vi = FileVersionInfo.GetVersionInfo(exePath);
            string? d = Clean(vi.FileDescription) ?? Clean(vi.ProductName);
            return d;
        }
        catch { return null; }
    }

    private static string? Clean(string? s)
        => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static string StripExe(string s)
    {
        s = s.Trim();
        return s.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? s[..^4] : s;
    }

    private static string Prettify(string proc)
    {
        if (string.IsNullOrEmpty(proc)) return proc;
        // "discord" -> "Discord", "my_app" -> "My app"
        string s = proc.Replace('_', ' ').Replace('-', ' ');
        return char.ToUpper(s[0]) + s[1..];
    }
}
