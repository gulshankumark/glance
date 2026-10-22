namespace Glance.Core.Sensors;

public interface ISensorSource : IDisposable
{
    IReadOnlyList<SensorInfo> Discover();
    IReadOnlyDictionary<string, double?> Read(IReadOnlySet<string> ids);
    IReadOnlyList<SensorDiagnostic> Diagnostics { get; }
}
