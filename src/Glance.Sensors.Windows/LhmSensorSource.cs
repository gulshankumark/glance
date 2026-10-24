using Glance.Core.Sensors;
using LibreHardwareMonitor.Hardware;

namespace Glance.Sensors.Windows;

public sealed class LhmSensorSource : ISensorSource
{
    private sealed record Entry(ISensor Sensor, string HardwareKind);

    private readonly Action _close;
    private readonly IReadOnlyList<IHardware> _roots;
    private readonly PawnIoStatus _pawnIoStatus;
    private readonly Dictionary<string, Entry> _entries = new();
    private readonly List<SensorDiagnostic> _diagnostics;
    private readonly object _gate = new();
    private IReadOnlyList<SensorInfo>? _discovered;

    public LhmSensorSource(PawnIo pawnIo) : this(ResolveStatus(pawnIo), OpenComputer)
    {
    }

    /// <summary>Test seam: <paramref name="open"/> returns the root hardware and a close action.</summary>
    internal LhmSensorSource(PawnIoStatus status, Func<(IReadOnlyList<IHardware> Roots, Action Close)> open)
    {
        _pawnIoStatus = status;
        _diagnostics = new List<SensorDiagnostic>(BuildDiagnostics(status));
        try
        {
            (_roots, _close) = open();
        }
        catch (Exception ex)
        {
            _roots = Array.Empty<IHardware>();
            _close = () => { };
            _diagnostics.Add(new SensorDiagnostic("sensors-unavailable", "Hardware sensors could not be opened: " + ex.Message));
        }
    }

    private static (IReadOnlyList<IHardware>, Action) OpenComputer()
    {
        var computer = new Computer
        {
            IsCpuEnabled = true,
            IsGpuEnabled = true,
            IsMemoryEnabled = true,
            IsMotherboardEnabled = true,
            IsStorageEnabled = true,
        };
        try
        {
            computer.Open();
        }
        catch
        {
            try { computer.Close(); } catch { }
            throw;
        }
        return (computer.Hardware.ToList(), computer.Close);
    }

    public IReadOnlyList<SensorDiagnostic> Diagnostics => _diagnostics;

    public IReadOnlyList<SensorInfo> Discover()
    {
        lock (_gate)
        {
            if (_discovered is not null) return _discovered;
            var list = new List<SensorInfo>();
            foreach (var hw in _roots) Walk(hw, list);
            return _discovered = list;
        }
    }

    private void Walk(IHardware hw, List<SensorInfo> list)
    {
        TryUpdate(hw);
        var kind = MapHardwareKind(hw.HardwareType);
        foreach (var s in hw.Sensors)
        {
            var id = s.Identifier.ToString();
            if (!_entries.TryAdd(id, new Entry(s, kind))) continue;
            list.Add(new SensorInfo(id, hw.Name, kind, s.Name, MapSensorKind(s.SensorType)));
        }
        foreach (var sub in hw.SubHardware) Walk(sub, list);
    }

    private static bool TryUpdate(IHardware hw)
    {
        try { hw.Update(); return true; }
        catch { return false; }
    }

    public IReadOnlyDictionary<string, double?> Read(IReadOnlySet<string> ids)
    {
        lock (_gate)
        {
            Discover();
            var updated = new Dictionary<IHardware, bool>();
            var result = new Dictionary<string, double?>();
            foreach (var id in ids)
            {
                result[id] = null;
                if (!_entries.TryGetValue(id, out var e)) continue;
                var hw = e.Sensor.Hardware;
                if (!updated.TryGetValue(hw, out var ok)) updated[hw] = ok = TryUpdate(hw);
                if (!ok) continue;
                try { result[id] = ApplyPawnIoRule(e.HardwareKind, _pawnIoStatus, ToReportedUnit(e.Sensor)); }
                catch { }
            }
            return result;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            try { _close(); } catch { }
        }
    }

    internal static PawnIoStatus ResolveStatus(PawnIo pawnIo)
    {
        var status = pawnIo.GetStatus();
        if (status == PawnIoStatus.Stopped && pawnIo.TryStart()) status = pawnIo.GetStatus();
        return status;
    }

    /// <summary>LHM SmallData sensors are MB; they are reported as Data in GB (the single conversion point).</summary>
    private static float? ToReportedUnit(ISensor s) => s.SensorType == SensorType.SmallData ? s.Value / 1024f : s.Value;

    internal static string MapHardwareKind(HardwareType t) => t switch
    {
        HardwareType.Cpu => "cpu",
        HardwareType.GpuIntel or HardwareType.GpuNvidia or HardwareType.GpuAmd => "gpu",
        HardwareType.Memory => "memory",
        HardwareType.Storage => "storage",
        HardwareType.Motherboard or HardwareType.SuperIO => "motherboard",
        _ => "other",
    };

    internal static SensorKind MapSensorKind(SensorType t) => t switch
    {
        SensorType.Temperature => SensorKind.Temperature,
        SensorType.Load => SensorKind.Load,
        SensorType.Clock => SensorKind.Clock,
        SensorType.Power => SensorKind.Power,
        SensorType.Fan => SensorKind.Fan,
        SensorType.Data => SensorKind.Data,
        SensorType.SmallData => SensorKind.Data,
        SensorType.Voltage => SensorKind.Voltage,
        SensorType.Control => SensorKind.Control,
        SensorType.Throughput => SensorKind.Throughput,
        _ => SensorKind.Other,
    };

    /// <summary>Without the PawnIO driver LHM reports 0 for CPU and motherboard sensors; surface that as "no value".</summary>
    internal static double? ApplyPawnIoRule(string hardwareKind, PawnIoStatus status, double? value) =>
        status != PawnIoStatus.Running && hardwareKind is "cpu" or "motherboard" ? null : value;

    internal static IReadOnlyList<SensorDiagnostic> BuildDiagnostics(PawnIoStatus status) => status switch
    {
        PawnIoStatus.NotInstalled => new[] { new SensorDiagnostic("pawnio-not-installed", "CPU and motherboard sensors need the PawnIO driver (not installed).") },
        PawnIoStatus.Stopped => new[] { new SensorDiagnostic("pawnio-stopped", "CPU and motherboard sensors need the PawnIO driver (installed but not running).") },
        _ => Array.Empty<SensorDiagnostic>(),
    };
}
