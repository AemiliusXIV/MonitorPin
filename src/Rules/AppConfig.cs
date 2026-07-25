using System.Text.Json.Serialization;

namespace MonitorPin.Rules;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AppearanceMode
{
    FollowWindows,
    Light,
    Dark,
}

/// <summary>
/// The whole persisted config: the rule list plus a few app-level settings.
/// Serialized to %AppData%\MonitorPin\rules.json.
/// </summary>
public sealed class AppConfig
{
    public int Version { get; set; } = 1;

    public List<Rule> Rules { get; set; } = new();

    /// <summary>Global force-minimize hotkey (foreground window).</summary>
    public HotkeySpec MinimizeHotkey { get; set; } = HotkeySpec.Default();

    /// <summary>Move the current window to the next/previous screen.</summary>
    public HotkeySpec NextMonitorHotkey { get; set; } = new();
    public HotkeySpec PrevMonitorHotkey { get; set; } = new();

    /// <summary>Force-minimize a specific app by name, even if it isn't focused.</summary>
    public HotkeySpec MinimizeAppHotkey { get; set; } = new();
    public string? MinimizeAppProcess { get; set; }

    /// <summary>Saved window arrangements.</summary>
    public List<WindowLayout> Layouts { get; set; } = new();

    /// <summary>Name of the layout the restore-layout shortcut brings back.</summary>
    public string? QuickLayout { get; set; }
    public HotkeySpec RestoreLayoutHotkey { get; set; } = new();

    /// <summary>Master switch; when false the placement engine ignores everything.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Light or dark windows, or whatever Windows itself is set to.</summary>
    public AppearanceMode Appearance { get; set; } = AppearanceMode.FollowWindows;

    /// <summary>How screens are labelled in the rules list and editor.</summary>
    public MonitorNamingStyle MonitorNaming { get; set; } = MonitorNamingStyle.Position;

    /// <summary>Custom monitor names, keyed by the monitor's stable hardware key.</summary>
    public Dictionary<string, string> MonitorAliases { get; set; } = new();

    /// <summary>Check GitHub for a newer version on startup. Contacts github.com when on.</summary>
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>Throttles the startup check to once a day.</summary>
    public DateTime? LastUpdateCheckUtc { get; set; }

    /// <summary>A version the user chose to skip; we won't nag about this one again.</summary>
    public string? SkippedVersion { get; set; }
}

public sealed class HotkeySpec
{
    public bool Ctrl { get; set; }
    public bool Alt { get; set; }
    public bool Shift { get; set; }
    public bool Win { get; set; }

    /// <summary>Virtual-key code of the main key.</summary>
    public uint Key { get; set; }

    /// <summary>Friendly label for the main key, shown in the UI (e.g. "Down").</summary>
    public string KeyName { get; set; } = "";

    public static HotkeySpec Default() => new()
    {
        Ctrl = true,
        Alt = true,
        Shift = false,
        Win = false,
        Key = 0x28, // VK_DOWN
        KeyName = "Down",
    };

    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsValid => Key != 0 && (Ctrl || Alt || Shift || Win);

    public override string ToString()
    {
        if (!IsValid) return "(not set)";
        var parts = new List<string>();
        if (Ctrl) parts.Add("Ctrl");
        if (Alt) parts.Add("Alt");
        if (Shift) parts.Add("Shift");
        if (Win) parts.Add("Win");
        parts.Add(string.IsNullOrEmpty(KeyName) ? $"0x{Key:X2}" : KeyName);
        return string.Join(" + ", parts);
    }
}
