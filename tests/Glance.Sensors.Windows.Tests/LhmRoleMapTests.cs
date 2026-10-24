using Glance.Sensors.Windows;

namespace Glance.Sensors.Windows.Tests;

public class LhmRoleMapTests
{
    static SensorInfo S(string id, string hw, string kind, string name, SensorKind k) => new(id, hw, kind, name, k);
    static readonly SensorInfo[] Pc =
    {
        S("c1","AMD Ryzen 9 9950X","cpu","CCD1 (Tdie)",SensorKind.Temperature),
        S("c2","AMD Ryzen 9 9950X","cpu","Core (Tctl/Tdie)",SensorKind.Temperature),
        S("c3","AMD Ryzen 9 9950X","cpu","CPU Total",SensorKind.Load),
        S("c4","AMD Ryzen 9 9950X","cpu","Cores (Average)",SensorKind.Clock),
        S("c5","AMD Ryzen 9 9950X","cpu","Package",SensorKind.Power),
        S("g1","Intel(R) Arc(TM) Pro B70 Graphics","gpu","GPU Core",SensorKind.Temperature),
        S("g2","Intel(R) Arc(TM) Pro B70 Graphics","gpu","GPU Core",SensorKind.Load),
        S("g3","Intel(R) Arc(TM) Pro B70 Graphics","gpu","GPU Core",SensorKind.Clock),
        S("g4","Intel(R) Arc(TM) Pro B70 Graphics","gpu","GPU Memory",SensorKind.Clock),
        S("g5","Intel(R) Arc(TM) Pro B70 Graphics","gpu","GPU Fan",SensorKind.Fan),
        S("g6","Intel(R) Arc(TM) Pro B70 Graphics","gpu","GPU Package",SensorKind.Power),
        S("g7","Intel(R) Arc(TM) Pro B70 Graphics","gpu","GPU Memory",SensorKind.Load),
        S("m0","Virtual Memory","memory","Memory",SensorKind.Load),
        S("m1","Total Memory","memory","Memory",SensorKind.Load),
        S("m2","Total Memory","memory","Memory Used",SensorKind.Data),
        S("f1","Gigabyte X870E","motherboard","Fan #1",SensorKind.Fan),
    };

    [Theory]
    [InlineData("cpu.temperature","c2")] [InlineData("cpu.load","c3")] [InlineData("cpu.clock","c4")]
    [InlineData("cpu.power.package","c5")] [InlineData("cpu.power.cores",null)] [InlineData("cpu.fan",null)]
    [InlineData("gpu.temperature","g1")] [InlineData("gpu.load","g2")] [InlineData("gpu.clock","g3")]
    [InlineData("gpu.memory.clock","g4")] [InlineData("gpu.fan","g5")] [InlineData("gpu.power","g6")]
    [InlineData("gpu.memory.load","g7")] [InlineData("memory.load","m1")] [InlineData("memory.used","m2")]
    [InlineData("bogus.role",null)]
    public void Maps_roles(string role, string? expected) => Assert.Equal(expected, new LhmRoleMap().Match(role, Pc));

    [Fact] public void Intel_cpu_temperature_falls_back_to_cpu_package()
    {
        var intel = new[] { S("i1","Intel Core i9","cpu","Core #1",SensorKind.Temperature),
                            S("i2","Intel Core i9","cpu","CPU Package",SensorKind.Temperature) };
        Assert.Equal("i2", new LhmRoleMap().Match("cpu.temperature", intel));
    }

    [Fact] public void Memory_available_maps_to_total_memory_data_sensor()
    {
        var sensors = new[] { S("v1","Virtual Memory","memory","Memory Available",SensorKind.Data),
                              S("t1","Total Memory","memory","Memory Available",SensorKind.Data) };
        Assert.Equal("t1", new LhmRoleMap().Match(Roles.MemoryAvailable, sensors));
    }

    [Fact] public void Gpu_memory_used_maps_to_gpu_small_data_sensor()
    {
        var sensors = new[] { S("g1","GPU","gpu","GPU Memory Used",SensorKind.Data) };
        Assert.Equal("g1", new LhmRoleMap().Match(Roles.GpuMemoryUsed, sensors));
    }
}
