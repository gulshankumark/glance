namespace Glance.Core.Sensors;

public static class Roles
{
    public const string CpuTemperature = "cpu.temperature";
    public const string CpuLoad = "cpu.load";
    public const string CpuClock = "cpu.clock";
    public const string CpuClockMax = "cpu.clock.max";
    public const string CpuPowerPackage = "cpu.power.package";
    public const string CpuPowerCores = "cpu.power.cores";
    public const string CpuFan = "cpu.fan";
    public const string GpuTemperature = "gpu.temperature";
    public const string GpuLoad = "gpu.load";
    public const string GpuClock = "gpu.clock";
    public const string GpuMemoryClock = "gpu.memory.clock";
    public const string GpuMemoryLoad = "gpu.memory.load";
    public const string GpuFan = "gpu.fan";
    public const string GpuPower = "gpu.power";
    public const string MemoryLoad = "memory.load";
    public const string MemoryUsed = "memory.used";
    public const string MemoryAvailable = "memory.available";
    public const string GpuMemoryUsed = "gpu.memory.used";

    public static readonly IReadOnlyList<string> All = new[]
    {
        CpuTemperature, CpuLoad, CpuClock, CpuPowerPackage, CpuPowerCores, CpuFan,
        GpuTemperature, GpuLoad, GpuClock, GpuMemoryClock, GpuMemoryLoad, GpuFan, GpuPower,
        MemoryLoad, MemoryUsed, MemoryAvailable, GpuMemoryUsed, CpuClockMax,
    };
}
