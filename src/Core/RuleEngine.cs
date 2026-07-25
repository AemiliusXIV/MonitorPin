using MonitorPin.Diagnostics;
using MonitorPin.Interop;
using MonitorPin.Monitors;
using MonitorPin.Rules;

namespace MonitorPin.Core;

/// <summary>
/// Ties window events to rules. On an eligible window it finds a matching rule
/// and applies it, then re-applies on a short schedule to beat apps that set
/// their own geometry a moment after showing.
/// </summary>
public sealed class RuleEngine
{
    // Re-apply offsets after the initial placement. Empirical; tune per app.
    private static readonly int[] ReapplyDelaysMs = { 150, 500, 1200 };

    // "Try harder" schedule for apps that keep repositioning themselves.
    private static readonly int[] AggressiveDelaysMs = { 150, 400, 800, 1500, 2500, 4000, 6000 };

    private readonly RuleStore _store;
    private readonly MonitorCatalog _catalog;

    // hwnd -> last time we applied, for throttling and first-window tracking.
    private readonly Dictionary<IntPtr, DateTime> _applied = new();
    private readonly HashSet<IntPtr> _firstWindowDone = new();

    private SynchronizationContext? _ui;
    private readonly List<System.Threading.Timer> _pending = new();

    public RuleEngine(RuleStore store, MonitorCatalog catalog)
    {
        _store = store;
        _catalog = catalog;
    }

    /// <summary>Give the engine the UI context so re-apply timers marshal back to it.</summary>
    public void CaptureUiContext(SynchronizationContext ui) => _ui = ui;

    public void RefreshMonitors() => _catalog.Refresh();

    public void OnWindowEvent(IntPtr hwnd, uint eventType)
    {
        if (!_store.Config.Enabled) return;

        var rule = MatchRule(hwnd);
        if (rule == null) return;
        if (ShouldSkipWindow(hwnd, rule)) return;

        if (rule.ApplyTo == ApplyScope.FirstWindow)
        {
            if (_firstWindowDone.Contains(hwnd)) return;
        }
        else
        {
            // AllWindows: throttle so a burst of focus events doesn't thrash.
            if (_applied.TryGetValue(hwnd, out var last) &&
                (DateTime.UtcNow - last).TotalMilliseconds < 1500)
                return;
        }

        Log.Line($"[event 0x{eventType:X}] match {Log.Win(WindowController.GetProcessName(hwnd), WindowController.GetTitle(hwnd))} -> rule '{rule.Process}'");
        ApplyRule(hwnd, rule);
        _firstWindowDone.Add(hwnd);
        _applied[hwnd] = DateTime.UtcNow;

        ScheduleReapplies(hwnd, rule);
        PruneIfNeeded();
    }

    /// <summary>
    /// Apply rules to every currently-open window. Used by "Apply now" (user
    /// action, may bring windows forward) and the startup sweep (placement only,
    /// so we don't yank focus around while the desktop is still loading).
    /// </summary>
    public int ApplyToAllOpenWindows(bool suppressForeground = false)
    {
        int applied = 0;
        string tag = suppressForeground ? "sweep" : "apply-now";
        NativeMethods.EnumWindows((h, _) =>
        {
            if (WindowController.IsEligibleTopLevel(h))
            {
                var rule = MatchRule(h);
                if (rule != null && !ShouldSkipWindow(h, rule))
                {
                    Log.Line($"[{tag}] {Log.Win(WindowController.GetProcessName(h), WindowController.GetTitle(h))} -> rule '{rule.Process}'");
                    ApplyRule(h, rule, suppressForeground: suppressForeground);
                    _firstWindowDone.Add(h);
                    _applied[h] = DateTime.UtcNow;
                    applied++;
                }
            }
            return true;
        }, IntPtr.Zero);
        Log.Line($"[{tag}] applied {applied} window(s)");
        return applied;
    }

    /// <summary>
    /// Leave secondary windows alone. A rule for a browser shouldn't maximize the
    /// little sign-in pop-up it opens, and a fixed-size window can't be maximized
    /// at all, so forcing it would just look broken.
    /// </summary>
    private static bool ShouldSkipWindow(IntPtr hwnd, Rule rule)
    {
        if (WindowController.IsOwnedPopup(hwnd))
        {
            Log.Line($"[skip] pop-up/dialog window of '{rule.Process}' left alone");
            return true;
        }
        if (rule.State == TargetState.Maximized && !WindowController.CanMaximize(hwnd))
        {
            Log.Line($"[skip] '{rule.Process}' window can't be maximized, left alone");
            return true;
        }
        return false;
    }

    private Rule? MatchRule(IntPtr hwnd)
    {
        string proc = WindowController.GetProcessName(hwnd);
        if (string.IsNullOrEmpty(proc)) return null;
        string title = WindowController.GetTitle(hwnd);

        foreach (var rule in _store.Config.Rules)
        {
            if (!rule.Enabled) continue;
            if (!proc.Equals(rule.NormalizedProcess(), StringComparison.OrdinalIgnoreCase)) continue;
            if (!string.IsNullOrEmpty(rule.TitleContains) &&
                title.IndexOf(rule.TitleContains, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            return rule;
        }
        return null;
    }

    private void ApplyRule(IntPtr hwnd, Rule rule, bool isReapply = false, bool suppressForeground = false)
    {
        var mon = _catalog.Resolve(rule.Monitor);
        if (mon == null)
        {
            _catalog.Refresh();
            mon = _catalog.Resolve(rule.Monitor);
            if (mon == null)
            {
                if (!isReapply) Log.Line($"[apply] rule '{rule.Process}': target monitor not found, skipped");
                return;
            }
        }

        // Re-apply passes and the startup sweep never steal focus; only a fresh
        // first placement triggered by the user or a window opening may.
        bool bringToFront = !isReapply && !suppressForeground && rule.ForceForeground;
        bool changed = WindowController.Apply(hwnd, mon, rule.State, rule.Size, bringToFront);

        if (changed)
            Log.Line($"[apply] rule '{rule.Process}' -> {mon.Label} [{rule.State}] fg={bringToFront}");
        else if (!isReapply)
            Log.Line($"[apply] rule '{rule.Process}': already correct, left as-is");
    }

    private void ScheduleReapplies(IntPtr hwnd, Rule rule)
    {
        foreach (int delay in rule.Aggressive ? AggressiveDelaysMs : ReapplyDelaysMs)
        {
            System.Threading.Timer? t = null;
            t = new System.Threading.Timer(_ =>
            {
                void Run()
                {
                    if (Interop.NativeMethods.IsWindow(hwnd) && MatchRule(hwnd) == rule)
                        ApplyRule(hwnd, rule, isReapply: true);
                    lock (_pending) { _pending.Remove(t!); }
                    t!.Dispose();
                }
                if (_ui != null) _ui.Post(_ => Run(), null);
                else Run();
            }, null, delay, System.Threading.Timeout.Infinite);
            lock (_pending) { _pending.Add(t); }
        }
    }

    private void PruneIfNeeded()
    {
        if (_applied.Count < 128) return;
        foreach (var h in _applied.Keys.Where(h => !Interop.NativeMethods.IsWindow(h)).ToList())
        {
            _applied.Remove(h);
            _firstWindowDone.Remove(h);
        }
    }
}
