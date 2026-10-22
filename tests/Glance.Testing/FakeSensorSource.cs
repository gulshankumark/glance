using Glance.Core.Sensors;

namespace Glance.Testing;

public sealed class FakeSensorSource : ISensorSource
{
    private readonly List<SensorInfo> _sensors;

    public FakeSensorSource(params (string Id, string HardwareKind, SensorKind Kind)[] sensors)
    {
        _sensors = sensors.Select(s => new SensorInfo(s.Id, s.HardwareKind, s.HardwareKind, s.Id, s.Kind)).ToList();
    }

    public Dictionary<string, double?> Values { get; } = new();
    public bool ThrowOnRead { get; set; }
    /// <summary>Ids passed to the last Read, sorted ordinally.</summary>
    public string[] LastRequested { get; private set; } = Array.Empty<string>();
    public IReadOnlyList<SensorDiagnostic> Diagnostics { get; } = Array.Empty<SensorDiagnostic>();

    public IReadOnlyList<SensorInfo> Discover() => _sensors;

    public IReadOnlyDictionary<string, double?> Read(IReadOnlySet<string> ids)
    {
        LastRequested = ids.OrderBy(i => i, StringComparer.Ordinal).ToArray();
        if (ThrowOnRead) throw new InvalidOperationException("read failed");
        var result = new Dictionary<string, double?>();
        foreach (var id in ids) result[id] = Values.TryGetValue(id, out var v) ? v : null;
        return result;
    }

    public void Dispose() { }
}
