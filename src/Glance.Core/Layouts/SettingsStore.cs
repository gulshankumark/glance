using System.Text.Json;

namespace Glance.Core.Layouts;

public sealed class SettingsStore
{
    private readonly GlancePaths _paths;
    private readonly object _gate = new();
    private AppSettings _current;

    public SettingsStore(GlancePaths paths)
    {
        _paths = paths;
        _current = Load();
    }

    public event Action? Changed;

    public AppSettings Current { get { lock (_gate) return _current; } }

    /// <summary>Set when settings.json could not be used and defaults are in effect.</summary>
    public string? LoadError { get; private set; }

    /// <summary>
    /// Applies <paramref name="change"/> to a copy, validates it, writes atomically, then publishes it. An exception from
    /// <paramref name="change"/> leaves everything as it was. <paramref name="committed"/> runs with the new settings
    /// after they are saved, still under the settings lock (for files derived from settings, such as kestrel.json).
    /// </summary>
    public void Update(Action<AppSettings> change, Action<AppSettings>? committed = null)
    {
        System.Runtime.ExceptionServices.ExceptionDispatchInfo? committedFailure = null;
        lock (_gate)
        {
            var copy = Clone(_current);
            change(copy);
            var errors = copy.Validate();
            if (errors.Count > 0) throw new ArgumentException(string.Join("; ", errors));
            AtomicFile.Write(_paths.Settings, JsonSerializer.Serialize(copy, GlanceJson.Options));
            _current = copy;
            try { committed?.Invoke(copy); }
            catch (Exception ex) { committedFailure = System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(ex); }
        }
        try { Changed?.Invoke(); } catch { /* subscriber faults must not break callers */ }
        committedFailure?.Throw();   // the settings did change; the caller still learns the derived step failed
    }

    /// <summary>Runs <paramref name="action"/> with the current settings under the settings lock, so no update interleaves.</summary>
    public void WithCurrent(Action<AppSettings> action)
    {
        lock (_gate) action(_current);
    }

    private static AppSettings Clone(AppSettings s) =>
        JsonSerializer.Deserialize<AppSettings>(JsonSerializer.Serialize(s, GlanceJson.Options), GlanceJson.Options)!;

    private AppSettings Load()
    {
        if (!File.Exists(_paths.Settings)) return new AppSettings();
        try
        {
            var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(_paths.Settings), GlanceJson.Options)
                    ?? throw new JsonException("settings file is empty");
            var errors = s.Validate();
            if (errors.Count > 0) throw new JsonException(string.Join("; ", errors));
            return s;
        }
        catch (Exception ex)
        {
            LoadError = ex.Message;
            try { File.Copy(_paths.Settings, _paths.Settings + ".corrupt", overwrite: true); } catch { }
            return new AppSettings();
        }
    }
}
