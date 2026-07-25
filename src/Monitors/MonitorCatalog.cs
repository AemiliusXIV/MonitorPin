using System.Drawing;
using MonitorPin.Interop;
using MonitorPin.Rules;

namespace MonitorPin.Monitors;

public sealed class MonitorEntry
{
    public required IntPtr Handle { get; init; }
    public required Rectangle Bounds { get; init; }      // full monitor rect (virtual-screen coords)
    public required Rectangle WorkArea { get; init; }    // minus taskbar
    public required bool IsPrimary { get; init; }
    public required string DeviceName { get; init; }     // \\.\DISPLAYn
    public required string HardwareId { get; init; }     // e.g. ABC1234 (PnP id + product)
    public required string HardwareKey { get; init; }    // stable: HardwareId#dupIndex

    /// <summary>Where it sits, in words: "left screen", "middle screen", etc.</summary>
    public required string PositionLabel { get; init; }

    /// <summary>Short position word: "left", "right", "middle", "top", "bottom", "main".</summary>
    public required string PositionWord { get; init; }

    /// <summary>The name Windows shows for the panel (e.g. "Odyssey G7"), if known.</summary>
    public required string WindowsName { get; init; }

    /// <summary>True when another connected monitor shares this Windows name.</summary>
    public bool NameDuplicated { get; set; }

    /// <summary>Best portable description of this monitor, for rules that travel.</summary>
    public required MonitorRole Role { get; init; }

    /// <summary>Default label (by position), used as a fallback for the naming options.</summary>
    public required string Label { get; init; }

    public Point Center => new(Bounds.Left + Bounds.Width / 2, Bounds.Top + Bounds.Height / 2);
}

/// <summary>
/// Enumerates the machine's monitors and works out, purely from their on-screen
/// rectangles, where each one sits. Nothing here is specific to any setup: the
/// same code names the screens correctly on a machine it has never seen.
/// </summary>
public sealed class MonitorCatalog
{
    // 3-letter PnP manufacturer prefixes -> readable brand, for nicer labels.
    private static readonly Dictionary<string, string> VendorPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["SAM"] = "Samsung",
        ["MSI"] = "MSI",
        ["DEL"] = "Dell",
        ["GSM"] = "LG",
        ["LGD"] = "LG",
        ["ACR"] = "Acer",
        ["ACI"] = "Asus",
        ["AUS"] = "Asus",
        ["BNQ"] = "BenQ",
        ["HPN"] = "HP",
        ["LEN"] = "Lenovo",
        ["AOC"] = "AOC",
        ["VSC"] = "ViewSonic",
        ["GBT"] = "Gigabyte",
    };

    public IReadOnlyList<MonitorEntry> Entries { get; private set; } = Array.Empty<MonitorEntry>();

    public void Refresh()
    {
        var raw = new List<(IntPtr handle, MONITORINFOEX info)>();

        NativeMethods.MonitorEnumProc cb = (IntPtr hMon, IntPtr hdc, ref RECT r, IntPtr data) =>
        {
            var mi = new MONITORINFOEX { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFOEX>() };
            if (NativeMethods.GetMonitorInfo(hMon, ref mi))
                raw.Add((hMon, mi));
            return true;
        };
        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, cb, IntPtr.Zero);
        if (raw.Count == 0) { Entries = Array.Empty<MonitorEntry>(); return; }

        var items = raw.Select(r => new
        {
            r.handle,
            r.info,
            hwId = ResolveHardwareId(r.info.szDevice),
            rect = r.info.rcMonitor.ToRectangle(),
            primary = (r.info.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0,
        }).ToList();

        // Duplicate models (two identical panels) get an index so their keys differ.
        var dupIndex = new Dictionary<IntPtr, int>();
        foreach (var g in items.GroupBy(i => i.hwId, StringComparer.OrdinalIgnoreCase))
        {
            int n = 0;
            foreach (var i in g.OrderBy(i => i.rect.Top).ThenBy(i => i.rect.Left))
                dupIndex[i.handle] = n++;
        }

        // Which way is this setup laid out? Whichever axis the screens spread along.
        var centers = items.Select(i => new Point(i.rect.Left + i.rect.Width / 2, i.rect.Top + i.rect.Height / 2)).ToList();
        int spreadX = centers.Max(c => c.X) - centers.Min(c => c.X);
        int spreadY = centers.Max(c => c.Y) - centers.Min(c => c.Y);
        bool horizontal = spreadX >= spreadY;

        var ordered = horizontal
            ? items.OrderBy(i => i.rect.Left + i.rect.Width / 2).ToList()
            : items.OrderBy(i => i.rect.Top + i.rect.Height / 2).ToList();

        var friendly = Interop.DisplayConfig.GetFriendlyNames();

        var entries = new List<MonitorEntry>();
        for (int idx = 0; idx < ordered.Count; idx++)
        {
            var i = ordered[idx];
            string word = PositionWord(idx, ordered.Count, horizontal);
            string posLabel = PositionLabelFor(word);
            var role = RoleFor(i.primary, idx, ordered.Count, horizontal);
            string brand = BrandOf(i.hwId);
            string winName = friendly.TryGetValue(i.info.szDevice, out var f) ? f : "";

            string label = ordered.Count == 1
                ? $"{brand} (main)"
                : $"{brand} - {posLabel}" + (i.primary ? " (main)" : "");

            entries.Add(new MonitorEntry
            {
                Handle = i.handle,
                Bounds = i.rect,
                WorkArea = i.info.rcWork.ToRectangle(),
                IsPrimary = i.primary,
                DeviceName = i.info.szDevice,
                HardwareId = i.hwId,
                HardwareKey = $"{i.hwId}#{dupIndex[i.handle]}",
                PositionLabel = posLabel,
                PositionWord = word,
                WindowsName = winName,
                Role = role,
                Label = label,
            });
        }

        // Flag monitors whose Windows name is shared (two identical panels), so a
        // name-based label can add the position to tell them apart.
        foreach (var e in entries)
            e.NameDuplicated = !string.IsNullOrEmpty(e.WindowsName)
                && entries.Count(o => o.WindowsName.Equals(e.WindowsName, StringComparison.OrdinalIgnoreCase)) > 1;

        Entries = entries;
    }

    private static string PositionWord(int index, int count, bool horizontal)
    {
        if (count == 1) return "main";
        if (index == 0) return horizontal ? "left" : "top";
        if (index == count - 1) return horizontal ? "right" : "bottom";
        if (count == 3) return "middle";
        return $"#{index + 1}"; // 4+ monitors: no natural word for the inner ones
    }

    private static string PositionLabelFor(string word)
        => word == "main" ? "main screen" : word.StartsWith('#') ? $"screen {word[1..]}" : $"{word} screen";

    private static MonitorRole RoleFor(bool primary, int index, int count, bool horizontal)
    {
        if (primary) return MonitorRole.Primary;
        if (count == 1) return MonitorRole.Primary;
        if (index == 0) return horizontal ? MonitorRole.Left : MonitorRole.Top;
        if (index == count - 1) return horizontal ? MonitorRole.Right : MonitorRole.Bottom;
        return MonitorRole.Middle;
    }

    private static string BrandOf(string hwId)
        => hwId.Length >= 3 && VendorPrefixes.TryGetValue(hwId[..3], out var v) ? v : hwId;

    private static string ResolveHardwareId(string adapterDeviceName)
    {
        var dd = new DISPLAY_DEVICE { cb = System.Runtime.InteropServices.Marshal.SizeOf<DISPLAY_DEVICE>() };
        if (NativeMethods.EnumDisplayDevices(adapterDeviceName, 0, ref dd, 0))
        {
            // DeviceID looks like: MONITOR\ABC1234\{GUID}\0004
            var parts = dd.DeviceID.Split('\\');
            if (parts.Length >= 2 && parts[0].Equals("MONITOR", StringComparison.OrdinalIgnoreCase))
                return parts[1];
        }
        return adapterDeviceName.Replace("\\\\.\\", "").Replace("\\", "");
    }

    /// <summary>Resolve a rule's monitor match against the current live monitors.</summary>
    public MonitorEntry? Resolve(MonitorMatch match)
    {
        if (Entries.Count == 0) return null;

        if (match.Mode == MonitorMatchMode.Cursor)
            return FromCursor();

        if (match.Mode == MonitorMatchMode.ByHardwareId && !string.IsNullOrEmpty(match.HardwareKey))
        {
            var exact = Entries.FirstOrDefault(e => e.HardwareKey.Equals(match.HardwareKey, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;

            var idPart = match.HardwareKey.Split('#')[0];
            return Entries.FirstOrDefault(e => e.HardwareId.Equals(idPart, StringComparison.OrdinalIgnoreCase));
        }

        // Position-based: works on a setup we've never seen.
        var byRole = Entries.FirstOrDefault(e => e.Role == match.Role);
        if (byRole != null) return byRole;

        return match.Role switch
        {
            MonitorRole.Primary => Entries.FirstOrDefault(e => e.IsPrimary) ?? Entries[0],
            MonitorRole.Left => Entries.OrderBy(e => e.Center.X).First(),
            MonitorRole.Right => Entries.OrderByDescending(e => e.Center.X).First(),
            MonitorRole.Top => Entries.OrderBy(e => e.Center.Y).First(),
            MonitorRole.Bottom => Entries.OrderByDescending(e => e.Center.Y).First(),
            MonitorRole.Middle => Entries.OrderBy(e => Math.Abs(e.Center.X - Entries.Average(x => x.Center.X))).First(),
            _ => Entries.FirstOrDefault(e => e.IsPrimary) ?? Entries[0],
        };
    }

    public MonitorEntry? FromWindow(IntPtr hwnd)
    {
        var hMon = NativeMethods.MonitorFromWindow(hwnd, NativeMethods.MONITOR_DEFAULTTONEAREST);
        return Entries.FirstOrDefault(e => e.Handle == hMon);
    }

    /// <summary>The monitor the mouse pointer is currently on.</summary>
    public MonitorEntry? FromCursor()
    {
        if (!NativeMethods.GetCursorPos(out var p)) return null;
        var hMon = NativeMethods.MonitorFromPoint(p, NativeMethods.MONITOR_DEFAULTTONEAREST);
        return Entries.FirstOrDefault(e => e.Handle == hMon) ?? Entries.FirstOrDefault(e => e.IsPrimary);
    }

    /// <summary>The next/previous monitor in left-to-right (or top-to-bottom) order.</summary>
    public MonitorEntry? Step(MonitorEntry current, int direction)
    {
        if (Entries.Count < 2) return null;
        int idx = -1;
        for (int i = 0; i < Entries.Count; i++)
            if (Entries[i].Handle == current.Handle) { idx = i; break; }
        if (idx < 0) return null;
        int t = ((idx + direction) % Entries.Count + Entries.Count) % Entries.Count;
        return Entries[t];
    }
}
