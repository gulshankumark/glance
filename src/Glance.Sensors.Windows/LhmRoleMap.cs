using Glance.Core.Sensors;

namespace Glance.Sensors.Windows;

/// <summary>Maps roles to LibreHardwareMonitor sensors by exact name; never falls back to "first of kind".</summary>
public sealed class LhmRoleMap : IRoleMap
{
    private sealed record Rule(string Role, string HardwareKind, SensorKind Kind, string[] Names, string? Hardware = null);

    private static readonly Rule[] Rules =
    {
        new(Roles.CpuTemperature, "cpu", SensorKind.Temperature, new[] { "Core (Tctl/Tdie)", "CPU Package" }),
        new(Roles.CpuLoad, "cpu", SensorKind.Load, new[] { "CPU Total" }),
        new(Roles.CpuClock, "cpu", SensorKind.Clock, new[] { "Cores (Average)" }),
        new(Roles.CpuPowerPackage, "cpu", SensorKind.Power, new[] { "Package" }),
        new(Roles.CpuPowerCores, "cpu", SensorKind.Power, new[] { "Cores" }),
        new(Roles.CpuFan, "motherboard", SensorKind.Fan, new[] { "CPU Fan" }),
        new(Roles.GpuTemperature, "gpu", SensorKind.Temperature, new[] { "GPU Core" }),
        new(Roles.GpuLoad, "gpu", SensorKind.Load, new[] { "GPU Core" }),
        new(Roles.GpuClock, "gpu", SensorKind.Clock, new[] { "GPU Core" }),
        new(Roles.GpuMemoryClock, "gpu", SensorKind.Clock, new[] { "GPU Memory" }),
        new(Roles.GpuMemoryLoad, "gpu", SensorKind.Load, new[] { "GPU Memory" }),
        new(Roles.GpuFan, "gpu", SensorKind.Fan, new[] { "GPU Fan" }),
        new(Roles.GpuPower, "gpu", SensorKind.Power, new[] { "GPU Package", "GPU Power" }),
        new(Roles.MemoryLoad, "memory", SensorKind.Load, new[] { "Memory" }, "Total Memory"),
        new(Roles.MemoryUsed, "memory", SensorKind.Data, new[] { "Memory Used" }, "Total Memory"),
    };

    public string? Match(string role, IReadOnlyList<SensorInfo> sensors)
    {
        var rule = Rules.FirstOrDefault(r => r.Role == role);
        if (rule is null) return null;
        var candidates = sensors.Where(s => s.HardwareKind == rule.HardwareKind && s.Kind == rule.Kind
            && (rule.Hardware is null || s.Hardware == rule.Hardware)).ToList();
        foreach (var name in rule.Names)
        {
            var hit = candidates.FirstOrDefault(s => s.Name == name);
            if (hit is not null) return hit.Id;
        }
        return null;
    }
}
