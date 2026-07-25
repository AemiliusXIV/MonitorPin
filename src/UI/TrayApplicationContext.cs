using System.Drawing;
using System.Windows.Forms;
using Microsoft.Win32;
using MonitorPin.Core;
using MonitorPin.Diagnostics;
using MonitorPin.Hotkeys;
using MonitorPin.Interop;
using MonitorPin.Monitors;
using MonitorPin.Rules;
using MonitorPin.Startup;

namespace MonitorPin.UI;

/// <summary>
/// Owns the tray icon and the whole running system: config, monitor catalog,
/// the window-event listener + rule engine, and the force-minimize hotkey.
/// </summary>
public sealed class TrayApplicationContext : ApplicationContext
{
    private readonly RuleStore _store = new();
    private readonly MonitorCatalog _catalog = new();
    private readonly RuleEngine _engine;
    private readonly WinEventListener _listener = new();
    private readonly HotkeyManager _hotkeys = new();
    private readonly SingleInstanceWindow _ipc = new();
    private readonly NotifyIcon _tray;
    private readonly SynchronizationContext _ui;

    private ToolStripMenuItem _enabledItem = null!;
    private ToolStripMenuItem _startupItem = null!;
    private IntPtr _foregroundAtMenuOpen;
    private MainForm? _settings;
    private System.Windows.Forms.Timer? _updateTimer;

    public TrayApplicationContext(bool showOnStartup = false, string? startupLayout = null)
    {
        // Marshals work back to this (UI) thread. Deliberately not falling back to
        // the plain SynchronizationContext: at this point Application.Run hasn't
        // installed one yet, and the default posts to the thread pool, where
        // creating windows throws.
        _ui = SynchronizationContext.Current as WindowsFormsSynchronizationContext
              ?? new WindowsFormsSynchronizationContext();

        _store.Load();
        Theme.Resolve(_store.Config.Appearance);
        Log.Line($"[startup] MonitorPin up, elevated={StartupInstaller.IsElevated()}, rules={_store.Config.Rules.Count}, dark={Theme.IsDark}");
        _catalog.Refresh();
        Log.Line($"[startup] {_catalog.Entries.Count} monitor(s): {string.Join(", ", _catalog.Entries.Select(e => e.Label))}");

        _engine = new RuleEngine(_store, _catalog);
        _engine.CaptureUiContext(_ui);

        _listener.WindowEvent += _engine.OnWindowEvent;
        _listener.Start();

        // Launch order shouldn't matter: sweep everything already open so apps
        // that started before us still get placed. Deferred so the catalog and
        // desktop have settled; placement only, no focus stealing on boot.
        _ui.Post(_ =>
        {
            if (_store.Config.Enabled)
            {
                int n = _engine.ApplyToAllOpenWindows(suppressForeground: true);
                Log.Line($"[startup] initial sweep placed {n} window(s)");
            }
        }, null);

        // Update check, well after startup so a cold boot isn't competing with it.
        _updateTimer = new System.Windows.Forms.Timer { Interval = 20000 };
        _updateTimer.Tick += async (_, _) =>
        {
            _updateTimer!.Stop();
            await CheckUpdatesAsync(userAsked: false);
        };
        _updateTimer.Start();

        _hotkeys.Pressed += OnHotkey;
        RegisterHotkeys();

        // Clicking the shortcut again should show the window, not do nothing.
        _ipc.ShowRequested += () => _ui.Post(_ => OpenSettings(), null);
        // A layout shortcut launched while we're already running hands its request here.
        _ipc.CommandReceived += cmd => _ui.Post(_ => RunCommand(cmd), null);

        _store.Changed += OnConfigChangedOnDisk;
        _store.StartWatching();

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;

        _tray = new NotifyIcon
        {
            Icon = Util.AppIconFactory.Shared,
            Text = "MonitorPin",
            Visible = true,
            ContextMenuStrip = BuildMenu(),
        };
        _tray.DoubleClick += (_, _) => OpenSettings();

        // Show the window when launched by hand (installer "start now", the Start
        // Menu shortcut) or on a genuinely fresh install. The logon task passes no
        // --show, so a normal sign-in stays quietly in the tray.
        if (showOnStartup || _store.WasFreshInstall)
            _ui.Post(_ => OpenSettings(), null);

        // Launched straight from a layout shortcut with nothing else running.
        if (!string.IsNullOrEmpty(startupLayout))
            _ui.Post(_ => RestoreLayoutByName(startupLayout), null);
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();

        menu.Items.Add(new ToolStripMenuItem("Rules and settings…", null, (_, _) => OpenSettings()));
        menu.Items.Add(new ToolStripMenuItem("Apply rules now", null, (_, _) => ApplyNow()));
        menu.Items.Add(new ToolStripMenuItem("Minimize the current window", null, (_, _) =>
            WindowController.ForceMinimize(_foregroundAtMenuOpen)));
        menu.Items.Add(new ToolStripSeparator());

        _enabledItem = new ToolStripMenuItem("Rules active", null, (_, _) => ToggleEnabled())
        {
            Checked = _store.Config.Enabled,
            CheckOnClick = true,
        };
        menu.Items.Add(_enabledItem);

        _startupItem = new ToolStripMenuItem("Start automatically with Windows", null, (_, _) => ToggleStartup())
        {
            Checked = StartupInstaller.IsInstalled(),
            CheckOnClick = false,
        };
        menu.Items.Add(_startupItem);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(new ToolStripMenuItem("Check for updates…", null, async (_, _) => await CheckUpdatesAsync(userAsked: true)));
        menu.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => ExitApp()));

        // Capture the real foreground window before the menu grabs focus.
        menu.Opening += (_, _) =>
        {
            _foregroundAtMenuOpen = NativeMethods.GetForegroundWindow();
            _startupItem.Checked = StartupInstaller.IsInstalled();
            _enabledItem.Checked = _store.Config.Enabled;
        };

        return menu;
    }

    private void RegisterHotkeys()
    {
        _hotkeys.Register(new Dictionary<HotkeyAction, HotkeySpec>
        {
            [HotkeyAction.Minimize] = _store.Config.MinimizeHotkey,
            [HotkeyAction.NextMonitor] = _store.Config.NextMonitorHotkey,
            [HotkeyAction.PrevMonitor] = _store.Config.PrevMonitorHotkey,
            [HotkeyAction.MinimizeApp] = _store.Config.MinimizeAppHotkey,
            [HotkeyAction.RestoreLayout] = _store.Config.RestoreLayoutHotkey,
        });
    }

    private void OnHotkey(HotkeyAction action)
    {
        switch (action)
        {
            case HotkeyAction.Minimize:
                IntPtr fg = NativeMethods.GetForegroundWindow();
                Log.Line($"[hotkey] force-minimize {Log.Win(WindowController.GetProcessName(fg), WindowController.GetTitle(fg))}");
                WindowController.ForceMinimize(fg);
                break;

            case HotkeyAction.NextMonitor:
                MoveForegroundToMonitor(+1);
                break;

            case HotkeyAction.PrevMonitor:
                MoveForegroundToMonitor(-1);
                break;

            case HotkeyAction.MinimizeApp:
                MinimizeNamedApp(_store.Config.MinimizeAppProcess);
                break;

            case HotkeyAction.RestoreLayout:
                RestoreQuickLayout();
                break;
        }
    }

    private void RestoreQuickLayout() => RestoreLayoutByName(_store.Config.QuickLayout);

    private void RestoreLayoutByName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return;
        var layout = _store.Config.Layouts.FirstOrDefault(l => l.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (layout != null) LayoutService.Restore(layout, _catalog);
        else Log.Line($"[layout] no layout named '{name}'");
    }

    /// <summary>Handle a command handed over by another launch (currently "layout:Name").</summary>
    private void RunCommand(string command)
    {
        if (command.StartsWith("layout:", StringComparison.OrdinalIgnoreCase))
            RestoreLayoutByName(command["layout:".Length..]);
    }

    private void MoveForegroundToMonitor(int direction)
    {
        IntPtr fg = NativeMethods.GetForegroundWindow();
        if (fg == IntPtr.Zero || !WindowController.IsEligibleTopLevel(fg)) return;
        var current = _catalog.FromWindow(fg);
        if (current == null) return;
        var target = _catalog.Step(current, direction);
        if (target == null) return;
        Log.Line($"[hotkey] move {WindowController.GetProcessName(fg)} -> {target.Label}");
        WindowController.MoveToMonitor(fg, current, target);
    }

    private void MinimizeNamedApp(string? process)
    {
        if (string.IsNullOrWhiteSpace(process)) return;
        string name = process.Trim();
        if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
        try
        {
            foreach (var p in System.Diagnostics.Process.GetProcessesByName(name))
            {
                if (p.MainWindowHandle != IntPtr.Zero)
                {
                    Log.Line($"[hotkey] force-minimize by name: {name}");
                    WindowController.ForceMinimize(p.MainWindowHandle);
                    return;
                }
            }
        }
        catch { }
    }

    /// <summary>
    /// Look for a newer release. On startup this is throttled to once a day and
    /// only runs if the user left the setting on; "Check for updates…" forces it.
    /// Nothing is downloaded or run without an explicit click in the dialog.
    /// </summary>
    private async Task CheckUpdatesAsync(bool userAsked)
    {
        if (!userAsked)
        {
            if (!_store.Config.CheckForUpdates) return;
            var last = _store.Config.LastUpdateCheckUtc;
            if (last != null && (DateTime.UtcNow - last.Value).TotalHours < 24) return;
        }

        Update.UpdateInfo? info;
        try
        {
            info = await Update.UpdateChecker.CheckAsync();
            _store.Config.LastUpdateCheckUtc = DateTime.UtcNow;
            _store.Save();
        }
        catch (Exception ex)
        {
            Log.Line($"[update] check failed: {ex.Message}");
            if (userAsked)
                MessageBox.Show("Couldn't reach GitHub to check for updates.\n\n" + ex.Message,
                    "MonitorPin", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (info == null)
        {
            Log.Line("[update] up to date");
            if (userAsked)
                MessageBox.Show($"You're up to date (version {Update.UpdateChecker.CurrentVersion}).",
                    "MonitorPin", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        // Respect a version the user chose to skip (unless they asked explicitly).
        if (!userAsked && _store.Config.SkippedVersion == info.Version.ToString())
        {
            Log.Line($"[update] {info.Version} available but skipped by user");
            return;
        }

        Log.Line($"[update] {info.Version} available");
        using var dlg = new UpdateForm(info);
        dlg.ShowDialog();

        if (dlg.SkipRequested)
        {
            _store.Config.SkippedVersion = info.Version.ToString();
            _store.Save();
        }
        if (dlg.InstallerLaunched)
            ExitApp(); // release the exe so setup can replace it
    }

    private void ApplyNow()
    {
        // Placement only. "Bring to the front" is about how an app *opens*; a
        // tidy-up click shouldn't yank focus around (and several such rules would
        // fight each other for it).
        int n = _engine.ApplyToAllOpenWindows(suppressForeground: true);
        _tray.ShowBalloonTip(2000, "MonitorPin",
            n == 0 ? "No open windows matched a rule." : $"Applied rules to {n} window(s).",
            ToolTipIcon.Info);
    }

    private void ToggleEnabled()
    {
        _store.Config.Enabled = _enabledItem.Checked;
        _store.Save();
    }

    private void ToggleStartup()
    {
        bool target = !StartupInstaller.IsInstalled();
        string exe = Environment.ProcessPath ?? Application.ExecutablePath;
        bool ok = StartupInstaller.RequestSetEnabled(target, exe);
        _startupItem.Checked = StartupInstaller.IsInstalled();
        if (!ok)
            _tray.ShowBalloonTip(3000, "MonitorPin",
                "Couldn't change the startup task (elevation declined?).", ToolTipIcon.Warning);
    }

    private void OpenSettings()
    {
        if (_settings != null && !_settings.IsDisposed)
        {
            _settings.Activate();
            return;
        }
        try
        {
            _settings = new MainForm(_store, _catalog);
            _settings.Changed += () =>
            {
                RegisterHotkeys();
                _enabledItem.Checked = _store.Config.Enabled;
                _startupItem.Checked = StartupInstaller.IsInstalled();
            };
            _settings.ApplyNowRequested += ApplyNow;
            _settings.CheckUpdatesRequested += async () => await CheckUpdatesAsync(userAsked: true);
            _settings.FormClosed += (_, _) => _settings = null;
            _settings.Show();
            _settings.Activate();
        }
        catch (Exception ex)
        {
            Log.Line($"[error] opening window: {ex}");
            _settings = null;
        }
    }

    private void ReloadFromDisk()
    {
        _store.Load();
        _catalog.Refresh();
        RegisterHotkeys();
        _enabledItem.Checked = _store.Config.Enabled;
    }

    private void OnConfigChangedOnDisk()
    {
        _ui.Post(_ =>
        {
            RegisterHotkeys();
            _enabledItem.Checked = _store.Config.Enabled;
        }, null);
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        => _ui.Post(_ => _catalog.Refresh(), null);

    private void ExitApp()
    {
        _tray.Visible = false;
        Dispose(true);
        ExitThread();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            _updateTimer?.Dispose();
            _listener.Dispose();
            _hotkeys.Dispose();
            _ipc.Dispose();
            _store.Dispose();
            _tray.Dispose();
        }
        base.Dispose(disposing);
    }
}
