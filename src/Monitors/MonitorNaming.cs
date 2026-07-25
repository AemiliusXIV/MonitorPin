using MonitorPin.Rules;

namespace MonitorPin.Monitors;

/// <summary>Turns a monitor into the label to show, per the user's naming choice.</summary>
public static class MonitorNaming
{
    public static string Label(MonitorEntry e, AppConfig cfg)
    {
        string main = e.IsPrimary ? " (main)" : "";

        switch (cfg.MonitorNaming)
        {
            case MonitorNamingStyle.Custom:
                if (cfg.MonitorAliases.TryGetValue(e.HardwareKey, out var alias) && !string.IsNullOrWhiteSpace(alias))
                    return alias.Trim();
                return ByPosition(e, main); // no alias set yet -> fall back

            case MonitorNamingStyle.WindowsName:
                string name = HasRealName(e.WindowsName) ? e.WindowsName : Brand(e);
                string disambig = e.NameDuplicated ? $" ({e.PositionWord})" : "";
                return name + disambig + main;

            case MonitorNamingStyle.Position:
            default:
                return ByPosition(e, main);
        }
    }

    private static string ByPosition(MonitorEntry e, string main)
        => Capitalize(e.PositionLabel) + main;

    private static bool HasRealName(string s)
        => !string.IsNullOrWhiteSpace(s) && s.IndexOf("Generic", StringComparison.OrdinalIgnoreCase) < 0;

    private static string Brand(MonitorEntry e)
    {
        // Fall back to the brand pulled from the hardware id (the label starts with it).
        int dash = e.Label.IndexOf(" - ", StringComparison.Ordinal);
        return dash > 0 ? e.Label[..dash] : e.HardwareId;
    }

    private static string Capitalize(string s)
        => string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..];
}
