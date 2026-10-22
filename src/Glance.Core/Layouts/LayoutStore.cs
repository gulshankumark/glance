using System.Collections.Concurrent;
using System.Text.Json;

namespace Glance.Core.Layouts;

/// <summary>
/// Layouts live as layouts/{Name}.json. Hand-edited or half-written files never replace the last good in-memory
/// version; the problem is recorded in <see cref="Errors"/>. Nothing here throws on a watcher/background thread.
/// </summary>
public sealed class LayoutStore : IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(250);

    private readonly GlancePaths _paths;
    private readonly TimeProvider _time;
    private readonly object _gate = new();
    private readonly Dictionary<string, Layout> _layouts = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _lastText = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> _errors = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ITimer> _pending = new(StringComparer.OrdinalIgnoreCase);
    private FileSystemWatcher? _watcher;
    private bool _disposed;

    public LayoutStore(GlancePaths paths, TimeProvider time)
    {
        _paths = paths;
        _time = time;
        foreach (var file in Directory.EnumerateFiles(paths.Layouts, "*.json"))
            Reload(Path.GetFileNameWithoutExtension(file));
    }

    public event Action<string>? Changed;

    public IReadOnlyList<string> Names
    {
        get { lock (_gate) return _layouts.Keys.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList(); }
    }

    public IReadOnlyDictionary<string, string> Errors =>
        new Dictionary<string, string>(_errors, StringComparer.OrdinalIgnoreCase);

    public Layout? Get(string name)
    {
        lock (_gate) return _layouts.TryGetValue(name ?? "", out var l) ? l : null;
    }

    /// <summary>Validates and writes the layout. Throws <see cref="ArgumentException"/> if it is invalid.</summary>
    public void Save(Layout l)
    {
        var errors = LayoutValidator.Validate(l);
        if (errors.Count > 0) throw new ArgumentException(string.Join("; ", errors));
        var text = JsonSerializer.Serialize(l, GlanceJson.Options);
        lock (_gate)
        {
            AtomicFile.Write(PathFor(l.Name), text);
            _layouts[l.Name] = l;
            _lastText[l.Name] = text;
            _errors.TryRemove(l.Name, out _);
        }
        RaiseChanged(l.Name);
    }

    public bool Delete(string name)
    {
        if (!LayoutValidator.IsValidName(name)) return false;
        bool existed;
        lock (_gate)
        {
            var path = PathFor(name);
            existed = File.Exists(path);
            if (existed) File.Delete(path);
            existed |= _layouts.Remove(name);
            _lastText.Remove(name);
            _errors.TryRemove(name, out _);
        }
        if (existed) RaiseChanged(name);
        return existed;
    }

    /// <summary>Re-reads one layout file. Never throws; on any failure the previous good version is kept.</summary>
    public void Reload(string name)
    {
        try
        {
            if (!LayoutValidator.IsValidName(name))
            {
                _errors[name ?? ""] = "invalid layout name";
                return;
            }
            var changed = false;
            lock (_gate)
            {
                var path = PathFor(name);
                if (!File.Exists(path))
                {
                    changed = _layouts.Remove(name);
                    _lastText.Remove(name);
                    _errors.TryRemove(name, out _);
                }
                else
                {
                    var text = ReadWithRetry(path);
                    if (_lastText.TryGetValue(name, out var last) && last == text) return;
                    try
                    {
                        var layout = GlanceJson.Deserialize<Layout>(text)
                                     ?? throw new JsonException("file is empty");
                        if (!string.Equals(layout.Name, name, StringComparison.OrdinalIgnoreCase))
                            throw new JsonException($"layout name '{layout.Name}' does not match file name");
                        var errors = LayoutValidator.Validate(layout);
                        if (errors.Count > 0) throw new JsonException(string.Join("; ", errors));
                        _layouts[name] = layout;
                        _lastText[name] = text;
                        _errors.TryRemove(name, out _);
                        changed = true;
                    }
                    catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
                    {
                        _errors[name] = ex.Message;   // keep last good layout
                        _lastText[name] = text;       // do not re-parse identical bad content
                    }
                }
            }
            if (changed) RaiseChanged(name);
        }
        catch (Exception ex)
        {
            try { _errors[name ?? ""] = ex.Message; } catch { }
        }
    }

    /// <summary>Watches layouts/*.json for edits made outside the app (debounced by 250 ms).</summary>
    public void StartWatching()
    {
        lock (_gate)
        {
            if (_watcher != null || _disposed) return;
            var w = new FileSystemWatcher(_paths.Layouts, "*.json");
            w.Created += (_, e) => Schedule(e.Name);
            w.Changed += (_, e) => Schedule(e.Name);
            w.Deleted += (_, e) => Schedule(e.Name);
            w.Renamed += (_, e) => { Schedule(e.OldName); Schedule(e.Name); };
            w.Error += (_, _) => { };
            w.EnableRaisingEvents = true;
            _watcher = w;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _watcher?.Dispose();
            _watcher = null;
            foreach (var t in _pending.Values) t.Dispose();
            _pending.Clear();
        }
    }

    private void Schedule(string? fileName)
    {
        try
        {
            if (fileName is null || !fileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return;
            var name = Path.GetFileNameWithoutExtension(fileName);
            lock (_gate)
            {
                if (_disposed) return;
                if (_pending.TryGetValue(name, out var t)) t.Change(Debounce, Timeout.InfiniteTimeSpan);
                else _pending[name] = _time.CreateTimer(_ => Fire(name), null, Debounce, Timeout.InfiniteTimeSpan);
            }
        }
        catch { /* never throw on the watcher thread */ }
    }

    private void Fire(string name)
    {
        try
        {
            lock (_gate)
            {
                if (_disposed) return;
                if (_pending.Remove(name, out var t)) t.Dispose();
            }
            Reload(name);
        }
        catch { /* never throw on a timer thread */ }
    }

    private static string ReadWithRetry(string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { return File.ReadAllText(path); }
            catch (IOException) when (attempt < 3) { Thread.Sleep(50); }
        }
    }

    private void RaiseChanged(string name)
    {
        try { Changed?.Invoke(name); } catch { /* subscriber faults must not break the store */ }
    }

    private string PathFor(string name) => Path.Combine(_paths.Layouts, name + ".json");
}
