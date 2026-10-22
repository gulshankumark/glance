using Glance.Sensors.Windows;
using LibreHardwareMonitor.Hardware;

namespace Glance.Sensors.Windows.Tests;

public class LhmSensorSourceReadTests
{
    sealed class FakeSensor : ISensor
    {
        public FakeSensor(IHardware hw, string id, string name, SensorType type, float? value)
        { Hardware = hw; Identifier = new Identifier(id.Trim((char)47).Split((char)47)); Name = name; SensorType = type; Value = value; }
        public IHardware Hardware { get; }
        public Identifier Identifier { get; }
        public string Name { get; set; }
        public SensorType SensorType { get; }
        public float? Value { get; }
        public int Index => 0;
        public bool IsDefaultHidden => false;
        public IReadOnlyList<IParameter> Parameters => throw new NotSupportedException();
        public float? Min => null;
        public float? Max => null;
        public IControl? Control => null;
        public IEnumerable<SensorValue> Values => Array.Empty<SensorValue>();
        public TimeSpan ValuesTimeWindow { get; set; }
        public void ResetMin() { }
        public void ResetMax() { }
        public void ClearValues() { }
        public void Accept(IVisitor visitor) { }
        public void Traverse(IVisitor visitor) { }
    }

    sealed class FakeHardware : IHardware
    {
        public FakeHardware(string id, string name, HardwareType type) { Identifier = new Identifier(id.Trim((char)47).Split((char)47)); Name = name; HardwareType = type; }
        public int Updates;
        public bool Throws;
        public List<FakeSensor> Own = new();
        public List<FakeHardware> Subs = new();
        public Identifier Identifier { get; }
        public string Name { get; set; }
        public HardwareType HardwareType { get; }
        public IHardware? Parent => null;
        public ISensor[] Sensors => Own.ToArray();
        public IHardware[] SubHardware => Subs.ToArray();
        public string GetReport() => "";
        public IDictionary<string, string> Properties { get; } = new Dictionary<string, string>();
        public void Update() { Updates++; if (Throws) throw new InvalidOperationException("flaky"); }
        public void Accept(IVisitor visitor) { }
        public void Traverse(IVisitor visitor) { }
        public event SensorEventHandler? SensorAdded { add { } remove { } }
        public event SensorEventHandler? SensorRemoved { add { } remove { } }
        public FakeSensor Add(string id, string name, SensorType t, float? v) { var s = new FakeSensor(this, id, name, t, v); Own.Add(s); return s; }
    }

    static LhmSensorSource Make(PawnIoStatus st, params FakeHardware[] roots) =>
        new(st, () => (roots, () => { }));

    [Fact] public void Read_updates_only_owning_hardware_including_sub_hardware()
    {
        var mb = new FakeHardware("/mb", "Board", HardwareType.Motherboard);
        var sio = new FakeHardware("/mb/sio", "SIO", HardwareType.SuperIO);
        sio.Add("/mb/sio/fan/0", "Fan #1", SensorType.Fan, 900);
        mb.Subs.Add(sio);
        var gpu = new FakeHardware("/gpu", "GPU", HardwareType.GpuNvidia);
        gpu.Add("/gpu/t", "GPU Core", SensorType.Temperature, 50);
        using var src = Make(PawnIoStatus.Running, mb, gpu);
        src.Discover();
        mb.Updates = sio.Updates = gpu.Updates = 0;

        var r = src.Read(new HashSet<string> { "/mb/sio/fan/0" });

        Assert.Equal(900, r["/mb/sio/fan/0"]);
        Assert.Equal(1, sio.Updates);
        Assert.Equal(0, gpu.Updates);
        Assert.Equal(0, mb.Updates);
    }

    [Fact] public void Unknown_ids_are_returned_as_null()
    {
        var gpu = new FakeHardware("/gpu", "GPU", HardwareType.GpuNvidia);
        gpu.Add("/gpu/t", "GPU Core", SensorType.Temperature, 50);
        using var src = Make(PawnIoStatus.Running, gpu);
        var r = src.Read(new HashSet<string> { "/gpu/t", "/nope" });
        Assert.Equal(50, r["/gpu/t"]);
        Assert.True(r.ContainsKey("/nope"));
        Assert.Null(r["/nope"]);
    }

    [Fact] public void Failing_hardware_does_not_escape_or_blank_others()
    {
        var bad = new FakeHardware("/bad", "Bad", HardwareType.GpuAmd) { Throws = true };
        bad.Add("/bad/t", "GPU Core", SensorType.Temperature, 1);
        var good = new FakeHardware("/good", "Good", HardwareType.GpuNvidia);
        good.Add("/good/t", "GPU Core", SensorType.Temperature, 60);
        using var src = Make(PawnIoStatus.Running, bad, good);

        var found = src.Discover();
        var r = src.Read(new HashSet<string> { "/bad/t", "/good/t" });

        Assert.Equal(2, found.Count);
        Assert.Null(r["/bad/t"]);
        Assert.Equal(60, r["/good/t"]);
    }

    [Fact] public void Open_failure_gives_empty_sensors_and_diagnostic()
    {
        using var src = new LhmSensorSource(PawnIoStatus.Running, () => throw new InvalidOperationException("boom"));
        Assert.Empty(src.Discover());
        Assert.Empty(src.Read(new HashSet<string>()));
        Assert.Contains(src.Diagnostics, d => d.Code == "sensors-unavailable");
    }

    [Fact] public void Cpu_values_are_null_when_pawnio_not_running()
    {
        var cpu = new FakeHardware("/cpu", "CPU", HardwareType.Cpu);
        cpu.Add("/cpu/t", "Core (Tctl/Tdie)", SensorType.Temperature, 0);
        using var src = Make(PawnIoStatus.Stopped, cpu);
        Assert.Null(src.Read(new HashSet<string> { "/cpu/t" })["/cpu/t"]);
    }

    [Fact] public void Dispose_closes_computer()
    {
        var closed = 0;
        var src = new LhmSensorSource(PawnIoStatus.Running, () => (Array.Empty<IHardware>(), () => closed++));
        src.Dispose();
        Assert.Equal(1, closed);
    }
}
