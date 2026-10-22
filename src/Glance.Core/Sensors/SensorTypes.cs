namespace Glance.Core.Sensors;

public enum SensorKind { Temperature, Load, Clock, Power, Fan, Data, SmallData, Voltage, Control, Throughput, Other }

/// <param name="HardwareKind">One of cpu|gpu|memory|storage|motherboard|other.</param>
public record SensorInfo(string Id, string Hardware, string HardwareKind, string Name, SensorKind Kind);

public record SensorDiagnostic(string Code, string Message);

public record SensorSnapshot(DateTimeOffset At, IReadOnlyDictionary<string, double?> Values);

public interface IRoleMap
{
    string? Match(string role, IReadOnlyList<SensorInfo> sensors);
}
