using System.Text.Json;

namespace MonitorPin.Rules;

/// <summary>
/// Loads and saves the config, and watches the file so a hand-edit or a Save
/// from the settings window hot-reloads the running engine.
/// </summary>
public sealed class RuleStore : IDisposable
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    private readonly string _dir;
    private readonly string _path;
    private FileSystemWatcher? _watcher;
    private DateTime _lastWriteHandled = DateTime.MinValue;

    public AppConfig Config { get; private set; } = new();

    /// <summary>True when rules.json was absent at Load time (fresh install / new machine).</summary>
    public bool WasFreshInstall { get; private set; }

    /// <summary>Fired (on a background thread) when the file changes on disk.</summary>
    public event Action? Changed;

    public string Path => _path;

    public RuleStore()
    {
        _dir = System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "MonitorPin");
        _path = System.IO.Path.Combine(_dir, "rules.json");
    }

    public void Load()
    {
        try
        {
            Directory.CreateDirectory(_dir);
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                Config = JsonSerializer.Deserialize<AppConfig>(json, JsonOpts) ?? new AppConfig();
                WasFreshInstall = false;
            }
            else
            {
                Config = new AppConfig();
                WasFreshInstall = true;
                Save(); // seed an empty file so the folder/file exist
            }
        }
        catch
        {
            // A corrupt file shouldn't take the tool down; start empty.
            Config = new AppConfig();
        }
    }

    public void Save()
    {
        Directory.CreateDirectory(_dir);
        var json = JsonSerializer.Serialize(Config, JsonOpts);
        _lastWriteHandled = DateTime.UtcNow;
        File.WriteAllText(_path, json);
    }

    public void StartWatching()
    {
        try
        {
            _watcher = new FileSystemWatcher(_dir, "rules.json")
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true,
            };
            _watcher.Changed += OnFileChanged;
        }
        catch
        {
            // Watching is a nicety; the tool still works without it.
        }
    }

    private void OnFileChanged(object sender, FileSystemEventArgs e)
    {
        // Debounce: editors and our own Save can fire several events in a burst.
        var now = DateTime.UtcNow;
        if ((now - _lastWriteHandled).TotalMilliseconds < 400) return;
        _lastWriteHandled = now;

        try
        {
            Thread.Sleep(120); // let the writer finish
            var json = File.ReadAllText(_path);
            var cfg = JsonSerializer.Deserialize<AppConfig>(json, JsonOpts);
            if (cfg != null)
            {
                Config = cfg;
                Changed?.Invoke();
            }
        }
        catch
        {
            // Ignore transient read errors mid-write.
        }
    }

    public void Dispose()
    {
        _watcher?.Dispose();
    }
}
