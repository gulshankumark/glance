using Glance.Sensors.Windows;
using LibreHardwareMonitor.Hardware;

namespace Glance.Sensors.Windows.Tests;

public class LhmSensorSourceTests
{
    [Theory]
    [InlineData(HardwareType.Cpu, "cpu")]
    [InlineData(HardwareType.GpuIntel, "gpu")] [InlineData(HardwareType.GpuNvidia, "gpu")] [InlineData(HardwareType.GpuAmd, "gpu")]
    [InlineData(HardwareType.Memory, "memory")] [InlineData(HardwareType.Storage, "storage")]
    [InlineData(HardwareType.Motherboard, "motherboard")] [InlineData(HardwareType.SuperIO, "motherboard")]
    [InlineData(HardwareType.Network, "other")]
    public void Maps_hardware_kind(HardwareType t, string expected) =>
        Assert.Equal(expected, LhmSensorSource.MapHardwareKind(t));

    [Theory]
    [InlineData("cpu", PawnIoStatus.Running, 42.0, 42.0)]
    [InlineData("cpu", PawnIoStatus.Stopped, 0.0, null)]
    [InlineData("cpu", PawnIoStatus.NotInstalled, 0.0, null)]
    [InlineData("motherboard", PawnIoStatus.Stopped, 1200.0, null)]
    [InlineData("gpu", PawnIoStatus.NotInstalled, 55.0, 55.0)]
    [InlineData("memory", PawnIoStatus.Stopped, 30.0, 30.0)]
    public void Nulls_cpu_and_motherboard_unless_pawnio_running(string kind, PawnIoStatus st, double v, double? expected) =>
        Assert.Equal(expected, LhmSensorSource.ApplyPawnIoRule(kind, st, v));

    [Fact] public void Null_value_stays_null() =>
        Assert.Null(LhmSensorSource.ApplyPawnIoRule("gpu", PawnIoStatus.Running, null));

    [Theory]
    [InlineData(PawnIoStatus.Running, null)]
    [InlineData(PawnIoStatus.NotInstalled, "pawnio-not-installed")]
    [InlineData(PawnIoStatus.Stopped, "pawnio-stopped")]
    public void Diagnostics_for_status(PawnIoStatus st, string? code)
    {
        var d = LhmSensorSource.BuildDiagnostics(st);
        if (code is null) Assert.Empty(d);
        else Assert.Equal(code, Assert.Single(d).Code);
    }

    sealed class FakePawnIo(PawnIoStatus initial, bool startResult) : PawnIo
    {
        public PawnIoStatus Status = initial;
        public int Starts;
        public override PawnIoStatus GetStatus() => Status;
        public override bool TryStart() { Starts++; if (startResult) Status = PawnIoStatus.Running; return startResult; }
    }

    [Fact] public void Resolve_status_starts_stopped_service()
    {
        var p = new FakePawnIo(PawnIoStatus.Stopped, true);
        Assert.Equal(PawnIoStatus.Running, LhmSensorSource.ResolveStatus(p));
        Assert.Equal(1, p.Starts);
    }

    [Fact] public void Resolve_status_does_not_start_when_not_installed()
    {
        var p = new FakePawnIo(PawnIoStatus.NotInstalled, true);
        Assert.Equal(PawnIoStatus.NotInstalled, LhmSensorSource.ResolveStatus(p));
        Assert.Equal(0, p.Starts);
    }

    [Fact] public void Resolve_status_reports_stopped_when_start_fails()
    {
        var p = new FakePawnIo(PawnIoStatus.Stopped, false);
        Assert.Equal(PawnIoStatus.Stopped, LhmSensorSource.ResolveStatus(p));
    }
}
