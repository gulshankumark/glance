using Microsoft.Extensions.Logging;

namespace Glance.Core.Sensors;

public sealed class SensorHub
{
    public const int MaxHistorySeconds = 1800;

    private readonly ISensorSource _source;
    private readonly IRoleMap _roles;
    private readonly TimeProvider _time;
    private readonly ILogger<SensorHub> _log;
    private readonly Dictionary<string, SensorInfo> _byId;
    private readonly object _gate = new();

    private const string DefaultOwner = "default";

    private readonly Dictionary<string, Dictionary<string, int>> _owners = new();
    private IReadOnlyDictionary<string, int> _interest = new Dictionary<string, int>();
    private IReadOnlyDictionary<string, string> _pins = new Dictionary<string, string>();
    private readonly Dictionary<string, RingBuffer> _history = new();
    private volatile SensorSnapshot _current;

    public SensorHub(ISensorSource source, IRoleMap roles, TimeProvider time, ILogger<SensorHub> log)
    {
        _source = source;
        _roles = roles;
        _time = time;
        _log = log;
        Sensors = source.Discover();
        _byId = Sensors.GroupBy(s => s.Id).ToDictionary(g => g.Key, g => g.First());
        _current = new SensorSnapshot(time.GetUtcNow(), new Dictionary<string, double?>());
    }

    public IReadOnlyList<SensorInfo> Sensors { get; }
    public SensorSnapshot Current => _current;
    public event Action<SensorSnapshot>? Updated;

    public SensorInfo? Info(string id) => _byId.GetValueOrDefault(id);

    /// <summary>Replaces the interest of the "default" owner.</summary>
    public void SetInterest(IReadOnlyDictionary<string, int> historySecondsById) => SetInterest(DefaultOwner, historySecondsById);

    /// <summary>
    /// Replaces the sensors <paramref name="owner"/> needs (id to history seconds, 0 = current value only).
    /// The hub reads the union of every owner's ids and keeps the largest history any owner asked for.
    /// </summary>
    public void SetInterest(string owner, IReadOnlyDictionary<string, int> historySecondsById)
    {
        lock (_gate)
        {
            _owners[owner] = historySecondsById.Where(kv => _byId.ContainsKey(kv.Key))
                .ToDictionary(kv => kv.Key, kv => kv.Value);
            ApplyInterest();
        }
    }

    public void ClearInterest(string owner)
    {
        lock (_gate)
            if (_owners.Remove(owner)) ApplyInterest();
    }

    private void ApplyInterest()   // caller holds _gate
    {
        var merged = new Dictionary<string, int>();
        foreach (var interest in _owners.Values)
            foreach (var (id, seconds) in interest)
                merged[id] = merged.TryGetValue(id, out var s) ? Math.Max(s, seconds) : seconds;
        _interest = merged;
        foreach (var id in _history.Keys.Where(id => !_interest.TryGetValue(id, out var s) || s <= 0).ToList())
            _history.Remove(id);
        foreach (var (id, seconds) in _interest)
        {
            if (seconds <= 0) continue;
            var cap = Math.Min(seconds, MaxHistorySeconds);
            if (_history.TryGetValue(id, out var old) && old.Capacity == cap) continue;
            var fresh = new RingBuffer(cap);
            if (old is not null)
                foreach (var v in old.Last(Math.Min(old.Count, cap))) fresh.Add(v);
            _history[id] = fresh;
        }
    }

    public void Tick()
    {
        SensorSnapshot snapshot;
        lock (_gate)
        {
            var ids = _interest.Keys.ToHashSet();
            IReadOnlyDictionary<string, double?>? read = null;
            try { read = _source.Read(ids); }
            catch (Exception ex) { _log.LogWarning(ex, "Sensor read failed"); }

            var values = new Dictionary<string, double?>();
            foreach (var id in ids)
            {
                double? v = null;
                if (read is not null && read.TryGetValue(id, out var r) && r is { } x && double.IsFinite(x)) v = x;   // NaN/±Infinity show as "—"
                values[id] = v;
                if (_history.TryGetValue(id, out var buf)) buf.Add(v);
            }
            snapshot = new SensorSnapshot(_time.GetUtcNow(), values);
            _current = snapshot;
        }
        Updated?.Invoke(snapshot);
    }

    public async Task RunAsync(CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(1), _time);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                try { Tick(); }
                catch (Exception ex) { _log.LogError(ex, "Sensor tick failed"); }
            }
        }
        catch (OperationCanceledException) { }
    }

    /// <summary>Last <paramref name="seconds"/> samples (capped at 1800), oldest first, left-padded with null.</summary>
    public IReadOnlyList<double?> History(string id, int seconds)
    {
        var n = Math.Clamp(seconds, 0, MaxHistorySeconds);
        lock (_gate)
            return _history.TryGetValue(id, out var buf) ? buf.Last(n) : new double?[n];
    }

    public void SetPins(IReadOnlyDictionary<string, string> rolePins)
    {
        lock (_gate) _pins = rolePins;
    }

    public string? Resolve(SensorRef r)
    {
        if (!r.IsRole) return _byId.ContainsKey(r.Value) ? r.Value : null;
        string? pin;
        lock (_gate) pin = _pins.GetValueOrDefault(r.Value);
        if (pin is not null) return _byId.ContainsKey(pin) ? pin : null;
        return _roles.Match(r.Value, Sensors);
    }
}
