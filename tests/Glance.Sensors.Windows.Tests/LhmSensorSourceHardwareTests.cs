using Glance.Sensors.Windows;

namespace Glance.Sensors.Windows.Tests;

public class LhmSensorSourceHardwareTests
{
    [Fact, Trait("Category", "Hardware")]
    public void Discovers_gpu_temperature_with_a_value()
    {
        using var source = new LhmSensorSource(new PawnIo());
        var gpuTemps = source.Discover().Where(s => s.HardwareKind == "gpu" && s.Kind == SensorKind.Temperature).ToList();
        Assert.NotEmpty(gpuTemps);
        var values = source.Read(gpuTemps.Select(s => s.Id).ToHashSet());
        Assert.Contains(gpuTemps, s => values.TryGetValue(s.Id, out var v) && v is not null);
    }
}
