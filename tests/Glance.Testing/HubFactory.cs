using Glance.Core.Sensors;
using Microsoft.Extensions.Logging.Abstractions;

namespace Glance.Testing;

public static class HubFactory
{
    public static SensorHub NewHub(ISensorSource src, IRoleMap? roleMap = null, TimeProvider? time = null) =>
        new(src, roleMap ?? new FixedRoleMap("", ""), time ?? TimeProvider.System, NullLogger<SensorHub>.Instance);
}
