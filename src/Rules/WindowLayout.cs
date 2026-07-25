namespace MonitorPin.Rules;

/// <summary>A saved snapshot of where a set of windows were, restorable later.</summary>
public sealed class WindowLayout
{
    public string Name { get; set; } = "";
    public List<LayoutWindow> Windows { get; set; } = new();
}

/// <summary>One window in a saved layout.</summary>
public sealed class LayoutWindow
{
    public string Process { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? Title { get; set; }

    /// <summary>Stable key of the monitor it was on.</summary>
    public string MonitorKey { get; set; } = "";

    /// <summary>Position/size relative to that monitor's top-left.</summary>
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public TargetState State { get; set; } = TargetState.Normal;
}
