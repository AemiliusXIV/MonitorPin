using System.Text.Json.Serialization;

namespace MonitorPin.Rules;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum TargetState
{
    Maximized,
    Normal,
    Minimized,
    CustomSize,
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ApplyScope
{
    FirstWindow,
    AllWindows,
}

/// <summary>
/// How a rule points at a monitor. Precise keys are best on a fixed rig;
/// role/position keys stay meaningful when the config lands on a different setup.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MonitorMatchMode
{
    ByHardwareId,
    ByRole,
    Cursor, // whichever screen the mouse is on when the window opens
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MonitorRole
{
    Primary,
    Left,
    Right,
    Top,
    Bottom,
    Middle,
}

/// <summary>How monitors are labelled in the UI.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))]
public enum MonitorNamingStyle
{
    Position,     // "Right screen (main)"
    WindowsName,  // the model name Windows shows, e.g. "Odyssey G7"
    Custom,       // a name you choose
}

public sealed class MonitorMatch
{
    public MonitorMatchMode Mode { get; set; } = MonitorMatchMode.ByHardwareId;

    /// <summary>Stable key = "HARDWAREID#index" (index disambiguates duplicate models by position).</summary>
    public string? HardwareKey { get; set; }

    public MonitorRole Role { get; set; } = MonitorRole.Primary;
}

public sealed class SizeSpec
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

public sealed class Rule
{
    /// <summary>Process name to match, e.g. "spotify" or "spotify.exe". Extension optional, case-insensitive.</summary>
    public string Process { get; set; } = "";

    /// <summary>Friendly name for display (e.g. "Discord"). Cosmetic; matching still uses Process.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Full exe path captured when the rule was made, used for the name/icon. Optional.</summary>
    public string? ExePath { get; set; }

    /// <summary>Optional case-insensitive substring the window title must contain.</summary>
    public string? TitleContains { get; set; }

    public MonitorMatch Monitor { get; set; } = new();

    public TargetState State { get; set; } = TargetState.Maximized;

    public SizeSpec? Size { get; set; }

    public bool ForceForeground { get; set; }

    public ApplyScope ApplyTo { get; set; } = ApplyScope.FirstWindow;

    /// <summary>Keep re-applying longer, for apps that reposition themselves aggressively.</summary>
    public bool Aggressive { get; set; }

    public bool Enabled { get; set; } = true;

    public string NormalizedProcess()
    {
        var p = Process.Trim();
        if (p.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            p = p[..^4];
        return p;
    }
}
