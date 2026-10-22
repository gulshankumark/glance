using Glance.Core.Sensors;

namespace Glance.Testing;

public sealed class FixedRoleMap(string role, string id) : IRoleMap
{
    public string? Match(string r, IReadOnlyList<SensorInfo> sensors) =>
        r == role && sensors.Any(s => s.Id == id) ? id : null;
}
