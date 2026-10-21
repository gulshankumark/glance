# Glance Core Service Implementation Plan (Plan 1 of 3)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A headless Glance service that drives the Thermalright 0416:5302 screen, reads sensors, renders layouts (text, clocks, images, all six gauge/graph types, still and animated backgrounds), and exposes a secured JSON/SSE API.

**Architecture:** One ASP.NET Core process (Windows service). Pure-logic libraries (`Glance.Core`, `Glance.Drivers.Thermalright`) behind small interfaces so everything except the HID/LHM adapters is unit-tested with fakes. Per-screen `DisplaySession` drives frames with `TimeProvider`; `SensorHub` polls once per second.

**Tech Stack:** .NET 8, ASP.NET Core minimal APIs, HidSharp 2.1.0, SkiaSharp 3.x, LibreHardwareMonitorLib 0.9.6, System.ServiceProcess.ServiceController, Serilog (File sink), xUnit, Microsoft.Extensions.TimeProvider.Testing, Microsoft.AspNetCore.Mvc.Testing.

**Spec:** `docs/superpowers/specs/2026-10-21-glance-design.md`

**Plan series:** Plan 1 (this) = core service + API. Plan 2 = web UI. Plan 3 = curated themes, installer, release docs. Linux is v1.x, after Plan 3.

## Global Constraints

- **Commit or push only when the owner asks** (spec §3). Tasks end at "tests pass"; there are no commit steps.
- **Clean repository:** nothing from Thermalright software or its decompilation — no code, text, assets, file/line references. Never read or copy from private research material or the vendor's installed software. Protocol facts come only from spec §5.2.
- .NET 8 (`net8.0`; Windows-only projects `net8.0-windows`). Nullable enabled, warnings as errors.
- Dependencies GPL-3.0-compatible only. No HWiNFO, no WinRing0.
- `nuget.config` at repo root lists only `https://api.nuget.org/v3/index.json` (`<clear/>` first) — the owner's machine has a broken private feed.
- Default port **8735**; bind loopback only unless network access is enabled (spec §9.1).
- Data dir: `%ProgramData%\Glance\` (override with env `GLANCE_DATA` for dev/tests): `settings.json`, `layouts/`, `assets/`, `logs/`, `kestrel.json`, `https.pfx`.
- Missing sensor value renders as `—` (U+2014). Items are never hidden or swapped (spec §6.1).
- JPEG: start quality 90, step down by 10 (floor 40) while size > 450 000 bytes (spec §5.2).
- History: 1 Hz, max 1800 samples per sensor, memory only (spec §7.1).
- Idle CPU (whole process, UI closed): ≤2% of one core still layout, ≤5% animated 24 fps with overlay (spec §2).
- Default font: Inter (OFL), bundled.

## Review Focus

1. **A web page in the user's browser (or DNS rebinding) calling the no-login loopback API** → requests whose `Host` is not `localhost`/`127.0.0.1`/`[::1]`/configured LAN name, or mutating requests with a foreign `Origin`, are rejected (403). Test in Task 14.
2. **Another program (TRCC) holds the screen / handshake times out** → device shown as "busy — retrying", retries back off 1→2→4…→30 s, no CPU spin. Test in Task 11.
3. **Unplug/replug while streaming** → session ends without crashing the service, device reconnects on replug. Test in Task 11.
4. **Garbage or hostile uploads** (exe renamed `.gif`, 60000×60000 GIF header, truncated `.zt`, `.zt` with n = int.MaxValue) → rejected with a clear error, no large allocation. Tests in Tasks 9 and 13.
5. **Hand-edited, half-written or path-traversing layout/settings files and names** → last good version kept, error reported, service keeps running; names like `../x` rejected. Tests in Tasks 5 and 13.

---

## File Structure

```
glance/
  Glance.sln, Directory.Build.props, nuget.config, LICENSE, THIRD-PARTY-NOTICES.md
  docs/protocols/thermalright-hid-5302.md
  docs/testing/hardware-smoke.md
  src/Glance.Core/
    Devices/      DeviceTypes.cs (UsbDeviceInfo, DisplayInfo, EncodedFrame, PixelFormat, ConnectResult),
                  IDisplayDriver.cs, IHidPort.cs, HidSharpPorts.cs
    Sensors/      SensorTypes.cs, ISensorSource.cs, SensorHub.cs, RingBuffer.cs, SensorRef.cs, Roles.cs
    Layouts/      LayoutModel.cs, GlanceJson.cs, LayoutValidator.cs, LayoutStore.cs, AppSettings.cs, SettingsStore.cs, GlancePaths.cs
    Rendering/    ValueFormatter.cs, ClockFormatter.cs, Orientation.cs, JpegEncoder.cs, FontProvider.cs,
                  RenderContext.cs, OverlayRenderer.cs, GaugeMath.cs, Items/*.cs (one file per item type), FallbackScreen.cs
    Media/        AssetStore.cs, Animation.cs, ZtReader.cs, MediaImporter.cs, InvalidMediaException.cs
    Sessions/     DisplaySession.cs, OverlayKey.cs, DeviceManager.cs, DeviceState.cs
    Fonts/        Inter-Regular.ttf, Inter-Bold.ttf (embedded resources)
  src/Glance.Drivers.Thermalright/   Hid5302Protocol.cs, ThermalrightHid5302Driver.cs
  src/Glance.Sensors.Windows/        LhmSensorSource.cs, LhmRoleMap.cs, PawnIo.cs
  src/Glance.Service/                Program.cs, Api/*.cs, Security/*.cs, wwwroot/index.html
  tests/Glance.Core.Tests/           (+ Golden/*.png, Fakes/*.cs)
  tests/Glance.Drivers.Thermalright.Tests/
  tests/Glance.Sensors.Windows.Tests/
  tests/Glance.Service.Tests/
```

---

### Task 1: Solution skeleton + Thermalright HID 0416:5302 protocol bytes

**Files:**
- Create: `Glance.sln`, `Directory.Build.props`, `nuget.config`, `LICENSE`, `THIRD-PARTY-NOTICES.md`, `docs/protocols/thermalright-hid-5302.md`
- Create: `src/Glance.Core/Glance.Core.csproj`, `src/Glance.Core/Devices/DeviceTypes.cs`, `src/Glance.Core/Devices/IDisplayDriver.cs`, `src/Glance.Core/Devices/IHidPort.cs`
- Create: `src/Glance.Drivers.Thermalright/Hid5302Protocol.cs`
- Test: `tests/Glance.Drivers.Thermalright.Tests/Hid5302ProtocolTests.cs`

**Interfaces:**
- Produces (Glance.Core.Devices):
  - `enum PixelFormat { Jpeg, Rgb565LE, Rgb565BE }`
  - `record UsbDeviceInfo(int VendorId, int ProductId, string Path, int MaxInputReportLength, int MaxOutputReportLength, string? ProductName)`
  - `record DisplayInfo(string DriverId, string Name, int Width, int Height, IReadOnlyList<PixelFormat> Formats, string Serial, string ModelCode)` with `string Key => $"{DriverId}_{Serial}"`
  - `record EncodedFrame(PixelFormat Format, int Width, int Height, ReadOnlyMemory<byte> Data)`
  - `abstract record ConnectResult` with nested `Connected(IDisplayConnection Connection)`, `Unsupported(string ModelCode, string Serial)`, `Failed(string Reason)`
  - `interface IDisplayConnection : IAsyncDisposable { DisplayInfo Info { get; } Task SendFrameAsync(EncodedFrame frame, CancellationToken ct); }`
  - `interface IDisplayDriver { string Id { get; } string DisplayName { get; } bool Matches(UsbDeviceInfo dev); Task<ConnectResult> ConnectAsync(UsbDeviceInfo dev, IHidPortFactory ports, CancellationToken ct); }`
  - `interface IHidPort : IDisposable { void Write(ReadOnlySpan<byte> report); int Read(Span<byte> buffer, TimeSpan timeout); }` — `Read` returns 0 on timeout
  - `interface IHidPortFactory { IHidPort Open(UsbDeviceInfo dev); }`
  - `interface IUsbEnumerator { IReadOnlyList<UsbDeviceInfo> Enumerate(); }`
- Produces (Glance.Drivers.Thermalright): `static class Hid5302Protocol` with `const int ChunkSize = 512`, `byte[] BuildHandshake()`, `HandshakeReply? ParseHandshakeReply(ReadOnlySpan<byte> report)`, `(int Width, int Height)? ResolvePanel(byte model, byte subModel)`, `byte[] BuildJpegFrame(ReadOnlySpan<byte> jpeg, int width, int height)`, `IReadOnlyList<byte[]> ToReports(ReadOnlySpan<byte> payload)`; `record HandshakeReply(byte SubModel, byte Model, string? Serial)`.

- [ ] **Step 1: Create skeleton.** `dotnet new sln -n Glance`; class libraries `Glance.Core`, `Glance.Drivers.Thermalright` (refs Core); xUnit project `Glance.Drivers.Thermalright.Tests`. `Directory.Build.props`: `TargetFramework net8.0`, `Nullable enable`, `TreatWarningsAsErrors true`, `ImplicitUsings enable`. `nuget.config` per Global Constraints. `LICENSE` = GPL-3.0 text from `https://www.gnu.org/licenses/gpl-3.0.txt`. `THIRD-PARTY-NOTICES.md` lists each NuGet dependency added in this plan with license (append as tasks add them).

- [ ] **Step 2: Write `docs/protocols/thermalright-hid-5302.md`** in our own words from spec §5.2 (device identity, report sizes, handshake/reply bytes, model table, frame header table, chunking, keep-alive). No references to vendor software.

- [ ] **Step 3: Write the failing tests**

```csharp
public class Hid5302ProtocolTests
{
    const string RealReply = "00DADBDCDD018000000000000001000000100000004250323139343007011D334247022078";

    [Fact] public void Handshake_bytes() =>
        Assert.Equal(Convert.FromHexString("DADBDCDD" + "0000000000000000" + "01" + "00000000000000"),
                     Hid5302Protocol.BuildHandshake());

    [Fact] public void Parses_real_reply()
    {
        var r = Hid5302Protocol.ParseHandshakeReply(Convert.FromHexString(RealReply))!;
        Assert.Equal(0x01, r.SubModel);
        Assert.Equal(0x80, r.Model);
        Assert.Equal("4250323139343007011D334247022078", r.Serial);
    }

    [Fact] public void Rejects_wrong_magic_or_flag()
    {
        var bad = Convert.FromHexString(RealReply); bad[2] = 0x00;
        Assert.Null(Hid5302Protocol.ParseHandshakeReply(bad));
        var noFlag = Convert.FromHexString(RealReply); noFlag[13] = 0x00;
        Assert.Null(Hid5302Protocol.ParseHandshakeReply(noFlag));
        Assert.Null(Hid5302Protocol.ParseHandshakeReply(new byte[5]));
    }

    [Fact] public void Serial_absent_when_byte17_not_0x10()
    {
        var b = Convert.FromHexString(RealReply); b[17] = 0;
        Assert.Null(Hid5302Protocol.ParseHandshakeReply(b)!.Serial);
    }

    [Theory]
    [InlineData(0x80, 0x01, true)] [InlineData(0x80, 0x02, true)]
    [InlineData(0x80, 0x03, false)] [InlineData(0x81, 0x01, false)]
    public void Panel_resolution(byte model, byte sub, bool known)
    {
        var p = Hid5302Protocol.ResolvePanel(model, sub);
        if (known) Assert.Equal((1280, 480), p); else Assert.Null(p);
    }

    [Fact] public void Jpeg_frame_header() =>
        Assert.Equal(Convert.FromHexString("DADBDCDD02000000" + "0005E001" + "02000000" + "03000000" + "010203"),
                     Hid5302Protocol.BuildJpegFrame(new byte[] { 1, 2, 3 }, 1280, 480));

    [Fact] public void Chunks_into_513_byte_reports_zero_padded()
    {
        var payload = Enumerable.Range(0, 1030).Select(i => (byte)(i % 251 + 1)).ToArray();
        var reports = Hid5302Protocol.ToReports(payload);
        Assert.Equal(3, reports.Count);
        Assert.All(reports, r => { Assert.Equal(513, r.Length); Assert.Equal(0, r[0]); });
        Assert.Equal(payload[512..1024], reports[1][1..513]);
        Assert.Equal(payload[1024..], reports[2][1..7]);
        Assert.All(reports[2][7..], b => Assert.Equal(0, b));
    }
}
```

- [ ] **Step 4: Run** `dotnet test tests/Glance.Drivers.Thermalright.Tests` — Expected: build FAIL (`Hid5302Protocol` missing).
- [ ] **Step 5: Implement** the Core device types and `Hid5302Protocol` exactly per Interfaces and spec §5.2 (reply: bytes 1–4 magic, byte 13 == 1, byte 5 sub, byte 6 model, serial = hex of bytes 21–36 when byte 17 == 0x10 and length ≥ 37).
- [ ] **Step 6: Run** `dotnet test` — Expected: PASS (8 tests).

---

### Task 2: Thermalright driver + HidSharp adapters

**Files:**
- Create: `src/Glance.Drivers.Thermalright/ThermalrightHid5302Driver.cs`, `src/Glance.Core/Devices/HidSharpPorts.cs`
- Test: `tests/Glance.Drivers.Thermalright.Tests/DriverTests.cs`, `tests/Glance.Drivers.Thermalright.Tests/FakeHidPort.cs`

**Interfaces:**
- Consumes: Task 1 types.
- Produces: `class ThermalrightHid5302Driver : IDisplayDriver` (`Id = "thermalright-hid-5302"`, `DisplayName = "Thermalright USB LCD (HID)"`); `class HidSharpPortFactory : IHidPortFactory`; `class HidSharpUsbEnumerator : IUsbEnumerator` (also exposes `event Action? Changed` wired to `DeviceList.Local.Changed`).

- [ ] **Step 1: Write the failing tests** (`FakeHidPort` records written reports, returns queued replies, returns 0 when queue empty)

```csharp
static UsbDeviceInfo Dev(int outLen = 513) => new(0x0416, 0x5302, "hid#1", 37, outLen, "USBDISPLAY");

[Fact] public void Matches_only_5302_with_513_byte_reports()
{
    var d = new ThermalrightHid5302Driver();
    Assert.True(d.Matches(Dev()));
    Assert.False(d.Matches(Dev(65)));
    Assert.False(d.Matches(Dev() with { ProductId = 0x5303 }));
}

[Fact] public async Task Connect_sends_handshake_and_returns_1280x480_jpeg()
{
    var port = new FakeHidPort(Convert.FromHexString(Hid5302ProtocolTests.RealReply));
    var r = await new ThermalrightHid5302Driver().ConnectAsync(Dev(), new FakeFactory(port), default);
    var c = Assert.IsType<ConnectResult.Connected>(r).Connection;
    Assert.Equal(Hid5302Protocol.ToReports(Hid5302Protocol.BuildHandshake())[0], port.Written[0]);
    Assert.Equal((1280, 480), (c.Info.Width, c.Info.Height));
    Assert.Equal(new[] { PixelFormat.Jpeg }, c.Info.Formats);
    Assert.Equal("80/01", c.Info.ModelCode);
    Assert.Equal("thermalright-hid-5302_4250323139343007011D334247022078", c.Info.Key);
}

[Fact] public async Task Unknown_model_is_unsupported_not_failed()
{
    var reply = Convert.FromHexString(Hid5302ProtocolTests.RealReply); reply[6] = 0x81;
    var r = await new ThermalrightHid5302Driver().ConnectAsync(Dev(), new FakeFactory(new FakeHidPort(reply)), default);
    Assert.Equal("81/01", Assert.IsType<ConnectResult.Unsupported>(r).ModelCode);
}

[Fact] public async Task No_reply_is_failed()
{
    var r = await new ThermalrightHid5302Driver().ConnectAsync(Dev(), new FakeFactory(new FakeHidPort()), default);
    Assert.IsType<ConnectResult.Failed>(r);
}

[Fact] public async Task SendFrame_writes_header_plus_jpeg_reports()
{
    var port = new FakeHidPort(Convert.FromHexString(Hid5302ProtocolTests.RealReply));
    var c = ((ConnectResult.Connected)await new ThermalrightHid5302Driver().ConnectAsync(Dev(), new FakeFactory(port), default)).Connection;
    port.Written.Clear();
    await c.SendFrameAsync(new EncodedFrame(PixelFormat.Jpeg, 1280, 480, new byte[] { 0xFF, 0xD8, 9 }), default);
    Assert.Equal(Hid5302Protocol.ToReports(Hid5302Protocol.BuildJpegFrame(new byte[] { 0xFF, 0xD8, 9 }, 1280, 480)), port.Written);
    await Assert.ThrowsAsync<ArgumentException>(() =>
        c.SendFrameAsync(new EncodedFrame(PixelFormat.Rgb565LE, 1280, 480, new byte[4]), default));
}
```

- [ ] **Step 2: Run** `dotnet test tests/Glance.Drivers.Thermalright.Tests` — Expected: FAIL (driver missing).
- [ ] **Step 3: Implement** the driver: handshake read timeout 2 s; serial missing → use the device path hash as serial (hex of first 8 bytes of SHA-256 of `Path`). Connection disposes the port. `HidSharpPortFactory` opens `HidDevice` by path via `DeviceList.Local.GetHidDevices()`; `Read` sets `ReadTimeout` and maps `TimeoutException` → 0. `HidSharpUsbEnumerator` maps `HidDevice` → `UsbDeviceInfo` (`GetMaxInputReportLength()`, `GetMaxOutputReportLength()`, `GetProductName()` in try/catch).
- [ ] **Step 4: Run** tests — Expected: PASS.

---

### Task 3: SensorHub, history and role resolution

**Files:**
- Create: `src/Glance.Core/Sensors/SensorTypes.cs`, `ISensorSource.cs`, `SensorRef.cs`, `Roles.cs`, `RingBuffer.cs`, `SensorHub.cs`
- Test: `tests/Glance.Core.Tests/Sensors/SensorHubTests.cs`, `tests/Glance.Core.Tests/Fakes/FakeSensorSource.cs`

**Interfaces:**
- Produces:
  - `enum SensorKind { Temperature, Load, Clock, Power, Fan, Data, SmallData, Voltage, Control, Throughput, Other }`
  - `record SensorInfo(string Id, string Hardware, string HardwareKind, string Name, SensorKind Kind)` — `HardwareKind` ∈ `cpu|gpu|memory|storage|motherboard|other`
  - `record SensorDiagnostic(string Code, string Message)`
  - `interface ISensorSource : IDisposable { IReadOnlyList<SensorInfo> Discover(); IReadOnlyDictionary<string, double?> Read(IReadOnlySet<string> ids); IReadOnlyList<SensorDiagnostic> Diagnostics { get; } }`
  - `interface IRoleMap { string? Match(string role, IReadOnlyList<SensorInfo> sensors); }`
  - `static class Roles` — constants and `All`: `cpu.temperature, cpu.load, cpu.clock, cpu.power.package, cpu.power.cores, cpu.fan, gpu.temperature, gpu.load, gpu.clock, gpu.memory.clock, gpu.memory.load, gpu.fan, gpu.power, memory.load, memory.used`
  - `readonly record struct SensorRef(bool IsRole, string Value)` with `static SensorRef Parse(string s)` — `"role:cpu.load"` → role, anything else → id; `ToString()` round-trips
  - `record SensorSnapshot(DateTimeOffset At, IReadOnlyDictionary<string, double?> Values)`
  - `class SensorHub(ISensorSource source, IRoleMap roles, TimeProvider time, ILogger<SensorHub> log)`: `IReadOnlyList<SensorInfo> Sensors`, `SensorSnapshot Current`, `event Action<SensorSnapshot>? Updated`, `void Tick()`, `Task RunAsync(CancellationToken ct)` (1 s `PeriodicTimer` with `time`), `void SetInterest(IReadOnlyDictionary<string, int> historySecondsById)`, `IReadOnlyList<double?> History(string id, int seconds)`, `string? Resolve(SensorRef r)`, `void SetPins(IReadOnlyDictionary<string, string> rolePins)`, `SensorInfo? Info(string id)`

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Tick_reads_only_interest_ids_and_publishes()
{
    var src = new FakeSensorSource(("cpu/t", "cpu", SensorKind.Temperature), ("gpu/t", "gpu", SensorKind.Temperature));
    src.Values["cpu/t"] = 51.6; src.Values["gpu/t"] = 53;
    var hub = NewHub(src);
    hub.SetInterest(new Dictionary<string, int> { ["gpu/t"] = 0 });
    hub.Tick();
    Assert.Equal(new[] { "gpu/t" }, src.LastRequested);
    Assert.Equal(53, hub.Current.Values["gpu/t"]);
}

[Fact] public void Source_exception_makes_values_null_and_hub_survives()
{
    var src = new FakeSensorSource(("cpu/t", "cpu", SensorKind.Temperature)) { ThrowOnRead = true };
    var hub = NewHub(src); hub.SetInterest(new Dictionary<string, int> { ["cpu/t"] = 0 });
    hub.Tick();
    Assert.Null(hub.Current.Values["cpu/t"]);
}

[Fact] public void History_oldest_first_padded_with_null_and_capped_at_1800()
{
    var src = new FakeSensorSource(("x", "cpu", SensorKind.Load));
    var hub = NewHub(src); hub.SetInterest(new Dictionary<string, int> { ["x"] = 5000 });
    for (int i = 1; i <= 3; i++) { src.Values["x"] = i; hub.Tick(); }
    Assert.Equal(new double?[] { null, null, 1, 2, 3 }, hub.History("x", 5));
    for (int i = 0; i < 2000; i++) hub.Tick();
    Assert.Equal(1800, hub.History("x", 5000).Count);
}

[Fact] public void Pin_beats_role_map_and_missing_pin_resolves_to_null()
{
    var src = new FakeSensorSource(("fan1", "motherboard", SensorKind.Fan), ("fan2", "motherboard", SensorKind.Fan));
    var hub = NewHub(src, new FixedRoleMap("cpu.fan", "fan1"));
    Assert.Equal("fan1", hub.Resolve(SensorRef.Parse("role:cpu.fan")));
    hub.SetPins(new Dictionary<string, string> { ["cpu.fan"] = "fan2" });
    Assert.Equal("fan2", hub.Resolve(SensorRef.Parse("role:cpu.fan")));
    hub.SetPins(new Dictionary<string, string> { ["cpu.fan"] = "gone" });
    Assert.Null(hub.Resolve(SensorRef.Parse("role:cpu.fan")));
    Assert.Equal("fan2", hub.Resolve(SensorRef.Parse("fan2")));
}
```

- [ ] **Step 2: Run** `dotnet test tests/Glance.Core.Tests` — Expected: FAIL.
- [ ] **Step 3: Implement.** History buffers only for interest ids with seconds > 0, capacity `min(max seconds requested, 1800)`; interest ids not in `Sensors` are ignored. `RunAsync` catches and logs per-tick exceptions.
- [ ] **Step 4: Run** tests — Expected: PASS.

---

### Task 4: Windows sensors via LibreHardwareMonitor + PawnIO

**Files:**
- Create: `src/Glance.Sensors.Windows/Glance.Sensors.Windows.csproj` (`net8.0-windows`; LibreHardwareMonitorLib 0.9.6, System.ServiceProcess.ServiceController), `LhmRoleMap.cs`, `LhmSensorSource.cs`, `PawnIo.cs`
- Test: `tests/Glance.Sensors.Windows.Tests/LhmRoleMapTests.cs`, `LhmSensorSourceHardwareTests.cs`

**Interfaces:**
- Consumes: Task 3 `ISensorSource`, `IRoleMap`, `SensorInfo`, `Roles`.
- Produces: `class LhmRoleMap : IRoleMap`; `class LhmSensorSource(PawnIo pawnIo) : ISensorSource`; `enum PawnIoStatus { NotInstalled, Stopped, Running }`; `class PawnIo { virtual PawnIoStatus GetStatus(); virtual bool TryStart(); }` (service name `PawnIO`).

- [ ] **Step 1: Write the failing role-map tests** (sensor names are those LHM 0.9.6 reported on the owner's Ryzen 9950X + Arc Pro B70)

```csharp
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
public void Maps_roles(string role, string? expected) => Assert.Equal(expected, new LhmRoleMap().Match(role, Pc));

[Fact] public void Intel_cpu_temperature_falls_back_to_cpu_package()
{
    var intel = new[] { S("i1","Intel Core i9","cpu","Core #1",SensorKind.Temperature),
                        S("i2","Intel Core i9","cpu","CPU Package",SensorKind.Temperature) };
    Assert.Equal("i2", new LhmRoleMap().Match("cpu.temperature", intel));
}
```

- [ ] **Step 2: Run** `dotnet test tests/Glance.Sensors.Windows.Tests` — Expected: FAIL.
- [ ] **Step 3: Implement `LhmRoleMap`** as an ordered rule table `(role, hardwareKind, SensorKind, preferred names in order)`; first sensor matching a preferred name wins, else `null` (never "first of kind" — avoids wrong sensors). `cpu.temperature`: `Core (Tctl/Tdie)`, `CPU Package`; `gpu.power`: `GPU Package`, `GPU Power`; `memory.*`: hardware `Total Memory` only; `cpu.fan` and `cpu.power.cores` (`Cores` name, Intel) as per table.
- [ ] **Step 4: Implement `LhmSensorSource`**: ctor calls `pawnIo.TryStart()` when status is `Stopped`; opens `Computer` with CPU, GPU, memory, motherboard, storage enabled. `Id = sensor.Identifier.ToString()`; `HardwareKind` from `HardwareType` (`Cpu`→cpu, `GpuIntel|GpuNvidia|GpuAmd`→gpu, `Memory`→memory, `Storage`→storage, `Motherboard|SuperIO`→motherboard). `Read(ids)` updates only the hardware (incl. sub-hardware) owning requested ids. When PawnIO is not `Running`, values from cpu/motherboard hardware are returned as `null` (LHM reports 0 there). `Diagnostics`: `pawnio-not-installed` / `pawnio-stopped` with messages "CPU and motherboard sensors need the PawnIO driver (not installed)." / "(installed but not running)."
- [ ] **Step 5: Write hardware test** `[Fact(Skip = "hardware")]`-style via `[Trait("Category","Hardware")]` that discovers sensors and asserts at least one `gpu` temperature with a value. Run `dotnet test --filter Category!=Hardware` — Expected: PASS. Run elevated once with `--filter Category=Hardware` on the owner's PC — Expected: PASS.

---

### Task 5: Layout model, JSON, validation, LayoutStore, settings

**Files:**
- Create: `src/Glance.Core/Layouts/LayoutModel.cs`, `GlanceJson.cs`, `LayoutValidator.cs`, `LayoutStore.cs`, `AppSettings.cs`, `SettingsStore.cs`, `GlancePaths.cs`
- Test: `tests/Glance.Core.Tests/Layouts/LayoutJsonTests.cs`, `LayoutStoreTests.cs`, `SettingsStoreTests.cs`

**Interfaces:**
- Produces (all `System.Text.Json` with `GlanceJson.Options`: camelCase, enums as camelCase strings, polymorphism via `"type"`):
  - `class Layout { string Name; int Width; int Height; Background Background; List<LayoutItem> Items; }`
  - `Background`: `ColorBackground(string Color)` `"color"`, `ImageBackground(string AssetId)` `"image"`, `AnimationBackground(string AssetId)` `"animation"` (GIF and .zt both import to animation assets)
  - `abstract class LayoutItem { string Id; float X; float Y; }` — (X,Y) is the item's top-left; text items use `Align` relative to X
  - `record FontSpec(string Family = "Inter", float Size = 32, int Weight = 400)`; `enum TextAlign { Left, Center, Right }`
  - Text-like: `TextItem` `"text"` (`Text, Font, Color, Align`), `SensorItem` `"sensor"` (`Sensor` string, `Label?`, `ShowUnit = true`, `Decimals = 0`, `Font, Color, Align`), `ClockItem` `"clock"` (`Format?`, `TimeZone = "local"`, `Label?`, `Font, Color, Align`), `ImageItem` `"image"` (`AssetId, Width, Height`)
  - `record Threshold(double From, string Color)`
  - `abstract class GaugeItem : LayoutItem { string Sensor; float Width; float Height; double? Min; double? Max; string Color = "#4AAFFF"; string TrackColor = "#33FFFFFF"; List<Threshold> Thresholds; bool ShowValue; string? Label; FontSpec Font; }`
  - `BarItem` `"bar"` (`Vertical`, `Rounded = true`), `SegmentedBarItem` `"segmentedBar"` (`Segments = 10`, `Gap = 4`, `Vertical`), `ArcGaugeItem` `"arcGauge"` (`StartAngle = 135`, `SweepAngle = 270`, `Thickness = 16`), `DialItem` `"dial"` (`StartAngle = 135`, `SweepAngle = 270`, `Ticks = 10`, `NeedleColor = "#FF4444"`), `HistoryGraphItem` `"historyGraph"` (`WindowSeconds = 60`, `Area = true`, `Grid = true`), `SparklineItem` `"sparkline"` (`WindowSeconds = 60`)
  - `static class LayoutValidator { IReadOnlyList<string> Validate(Layout l); bool IsValidName(string name); }` — name regex `^[A-Za-z0-9 _-]{1,64}$`
  - `class LayoutStore(GlancePaths paths, TimeProvider time)`: `IReadOnlyList<string> Names`, `Layout? Get(string name)`, `void Save(Layout l)`, `bool Delete(string name)`, `IReadOnlyDictionary<string,string> Errors`, `event Action<string>? Changed`, `void StartWatching()`
  - `class AppSettings { int Port = 8735; bool NetworkAccess; string? PasswordHash; bool Https; string? HttpsPfxPassword; TemperatureUnit Unit = Celsius; bool Clock24h = true; Dictionary<string, DeviceSettings> Devices; Dictionary<string,string> Aliases; Dictionary<string,string> RolePins; }`, `class DeviceSettings { string? ActiveLayout; int Rotation; }`, `enum TemperatureUnit { Celsius, Fahrenheit }`
  - `class SettingsStore(GlancePaths paths)`: `AppSettings Current`, `void Update(Action<AppSettings> change)` (atomic write temp+replace), `event Action? Changed`
  - `class GlancePaths(string root)`: `Root, Settings, Layouts, Assets, Logs, KestrelConfig, HttpsPfx`; `static GlancePaths Default()` uses `GLANCE_DATA` env or `%ProgramData%\Glance`; creates dirs.

- [ ] **Step 1: Write the failing tests**

```csharp
const string Sample = """
{ "name":"Gaming","width":1280,"height":480,
  "background":{"type":"color","color":"#101820"},
  "items":[
    {"type":"sensor","id":"a","x":40,"y":40,"sensor":"role:cpu.temperature","label":"CPU","decimals":0},
    {"type":"clock","id":"b","x":900,"y":40,"timeZone":"Asia/Tokyo","label":"Tokyo"},
    {"type":"arcGauge","id":"c","x":40,"y":200,"sensor":"role:gpu.load","width":200,"height":200,
     "thresholds":[{"from":0,"color":"#00FF00"},{"from":80,"color":"#FF0000"}]},
    {"type":"historyGraph","id":"d","x":300,"y":200,"sensor":"role:gpu.temperature","width":400,"height":150,"windowSeconds":300}
  ]}
""";

[Fact] public void Roundtrips_polymorphic_items()
{
    var l = JsonSerializer.Deserialize<Layout>(Sample, GlanceJson.Options)!;
    Assert.IsType<ColorBackground>(l.Background);
    Assert.Collection(l.Items, i => Assert.IsType<SensorItem>(i), i => Assert.IsType<ClockItem>(i),
                               i => Assert.IsType<ArcGaugeItem>(i), i => Assert.IsType<HistoryGraphItem>(i));
    var again = JsonSerializer.Deserialize<Layout>(JsonSerializer.Serialize(l, GlanceJson.Options), GlanceJson.Options)!;
    Assert.Equal(300, ((HistoryGraphItem)again.Items[3]).WindowSeconds);
}

[Theory]
[InlineData("""{"type":"bar","id":"x","x":0,"y":0,"sensor":"s","width":10,"height":10,"min":5,"max":5}""", "min must be less than max")]
[InlineData("""{"type":"historyGraph","id":"x","x":0,"y":0,"sensor":"s","width":10,"height":10,"windowSeconds":4000}""", "windowSeconds must be 1..1800")]
[InlineData("""{"type":"sensor","id":"x","x":0,"y":0,"sensor":"s","decimals":7}""", "decimals must be 0..3")]
[InlineData("""{"type":"text","id":"x","x":0,"y":0,"text":"hi","color":"red"}""", "invalid color 'red'")]
public void Validator_reports(string itemJson, string error)
{
    var l = JsonSerializer.Deserialize<Layout>($$"""{"name":"L","width":1280,"height":480,"background":{"type":"color","color":"#000000"},"items":[{{itemJson}}]}""", GlanceJson.Options)!;
    Assert.Contains(LayoutValidator.Validate(l), e => e.Contains(error));
}

[Theory] [InlineData("../evil")] [InlineData("a/b")] [InlineData("")] [InlineData("con.json")]
public void Rejects_bad_names(string n) => Assert.False(LayoutValidator.IsValidName(n));

[Fact] public void Store_keeps_last_good_layout_when_file_is_corrupted()
{
    using var dir = new TempDir(); var store = new LayoutStore(new GlancePaths(dir.Path), TimeProvider.System);
    var l = JsonSerializer.Deserialize<Layout>(Sample, GlanceJson.Options)!;
    store.Save(l);
    File.WriteAllText(Path.Combine(dir.Path, "layouts", "Gaming.json"), "{ \"name\": ");   // half-written
    store.Reload("Gaming");
    Assert.Equal(4, store.Get("Gaming")!.Items.Count);
    Assert.True(store.Errors.ContainsKey("Gaming"));
}

[Fact] public void Settings_defaults_and_atomic_update()
{
    using var dir = new TempDir(); var s = new SettingsStore(new GlancePaths(dir.Path));
    Assert.Equal(8735, s.Current.Port);
    s.Update(x => x.Devices["k"] = new DeviceSettings { Rotation = 180 });
    Assert.Equal(180, new SettingsStore(new GlancePaths(dir.Path)).Current.Devices["k"].Rotation);
}
```

- [ ] **Step 2: Run** — Expected: FAIL.
- [ ] **Step 3: Implement.** `LayoutStore` file = `layouts/{Name}.json`; `Reload(name)` (public, used by watcher) parses + validates; on failure keeps the previous in-memory layout and records `Errors[name]`. Watcher: `FileSystemWatcher` on `layouts/*.json`, debounce 250 ms via `time`, retry read 3× on `IOException`. Colors: `#RRGGBB` or `#AARRGGBB`. Validation also: width/height 1..4096, rotation values in settings ∈ {0,90,180,270}, thresholds sorted ascending by `From`, segments 2..50.
- [ ] **Step 4: Run** — Expected: PASS.

---

### Task 6: Rendering foundation — formatting, clocks, fonts, text/sensor/clock/image items, orientation, JPEG

**Files:**
- Create: `src/Glance.Core/Rendering/ValueFormatter.cs`, `ClockFormatter.cs`, `FontProvider.cs`, `RenderContext.cs`, `OverlayRenderer.cs`, `Orientation.cs`, `JpegEncoder.cs`, `FallbackScreen.cs`, `Items/TextItems.cs`, `Items/ImageItemRenderer.cs`; `src/Glance.Core/Fonts/Inter-Regular.ttf`, `Inter-Bold.ttf` (download Inter 4.x release from `https://github.com/rsms/inter/releases`, OFL; add to THIRD-PARTY-NOTICES)
- Test: `tests/Glance.Core.Tests/Rendering/FormatTests.cs`, `OrientationTests.cs`, `JpegEncoderTests.cs`, `OverlayGoldenTests.cs`, `tests/Glance.Core.Tests/GoldenAssert.cs`

**Interfaces:**
- Consumes: Tasks 3, 5; `IAssetStore` (defined here as interface, implemented in Task 9): `interface IAssetStore { SKBitmap? GetImage(string assetId); Animation? GetAnimation(string assetId); }`
- Produces:
  - `static class ValueFormatter { string Format(double? value, SensorKind? kind, int decimals, bool showUnit, TemperatureUnit unit); string Unit(SensorKind kind, TemperatureUnit unit); }` — units: Temperature `°C`/`°F`, Load `%`, Clock `MHz`, Power `W`, Fan `RPM`, Data `GB`, SmallData `MB`, Voltage `V`, Throughput `MB/s`, others none; format `"{number} {unit}"`; null → `"—"`
  - `static class ClockFormatter { string Format(DateTimeOffset now, string timeZone, string? format, bool clock24h); }` — default format `HH:mm` / `h:mm tt`; unknown zone → local
  - `record RenderContext(SensorHub Sensors, AppSettings Settings, DateTimeOffset Now, IAssetStore Assets, FontProvider Fonts)`
  - `class FontProvider { SKTypeface Get(FontSpec f); }` — bundled Inter by default, system family via `SKFontManager.Default.MatchFamily`, fallback Inter
  - `class OverlayRenderer { SKBitmap Render(Layout layout, RenderContext ctx); IEnumerable<string> ReferencedSensorIds(Layout l, SensorHub hub); IReadOnlyDictionary<string,int> HistoryNeeds(Layout l, SensorHub hub); }` — transparent bitmap `layout.Width × layout.Height`; dispatches per item type; an item that throws is skipped and logged
  - `static class Orientation { (int W, int H) Canvas(int nativeW, int nativeH, int rotation); SKBitmap ToDevice(SKBitmap canvas, int rotation); SKBitmap FromDevice(SKBitmap device, int rotation); SKBitmap FitToCanvas(SKBitmap layoutImage, int canvasW, int canvasH); }` — rotation = clockwise degrees applied to the canvas to produce the device image
  - `record EncodedJpeg(byte[] Data, int Quality)`; `static class JpegEncoder { EncodedJpeg Encode(SKBitmap bmp, int maxBytes = 450_000); }`
  - `static class FallbackScreen { SKBitmap Render(int w, int h, string message, FontProvider fonts); }` — used when no layout is active: "Glance is running — open http://localhost:8735"

- [ ] **Step 1: Write the failing tests**

```csharp
[Theory]
[InlineData(51.62, SensorKind.Temperature, 0, TemperatureUnit.Celsius, "52 °C")]
[InlineData(51.62, SensorKind.Temperature, 0, TemperatureUnit.Fahrenheit, "125 °F")]
[InlineData(62.4, SensorKind.Load, 0, TemperatureUnit.Celsius, "62 %")]
[InlineData(3836.0, SensorKind.Clock, 0, TemperatureUnit.Celsius, "3836 MHz")]
[InlineData(37.21, SensorKind.Power, 1, TemperatureUnit.Celsius, "37.2 W")]
[InlineData(1148.0, SensorKind.Fan, 0, TemperatureUnit.Celsius, "1148 RPM")]
[InlineData(null, SensorKind.Fan, 0, TemperatureUnit.Celsius, "—")]
public void Formats(double? v, SensorKind k, int d, TemperatureUnit u, string expected) =>
    Assert.Equal(expected, ValueFormatter.Format(v, k, d, showUnit: true, u));

[Fact] public void Clock_formats_zone_and_12h()
{
    var now = new DateTimeOffset(2026, 10, 21, 14, 5, 9, TimeSpan.Zero);
    Assert.Equal("23:05", ClockFormatter.Format(now, "Asia/Tokyo", null, clock24h: true));
    Assert.Equal("11:05 PM", ClockFormatter.Format(now, "Asia/Tokyo", null, clock24h: false));
}

[Theory] [InlineData(0, 1280, 480)] [InlineData(90, 480, 1280)] [InlineData(180, 1280, 480)] [InlineData(270, 480, 1280)]
public void Canvas_size(int rot, int w, int h) => Assert.Equal((w, h), Orientation.Canvas(1280, 480, rot));

[Fact] public void Rotation_180_and_90_move_corner_pixel()
{
    using var c = Solid(1280, 480, SKColors.Black); c.SetPixel(0, 0, SKColors.Red);
    Assert.Equal(SKColors.Red, Orientation.ToDevice(c, 180).GetPixel(1279, 479));
    using var p = Solid(480, 1280, SKColors.Black); p.SetPixel(0, 0, SKColors.Red);
    var d = Orientation.ToDevice(p, 90);
    Assert.Equal((1280, 480), (d.Width, d.Height));
    Assert.Equal(SKColors.Red, d.GetPixel(1279, 0));
    Assert.Equal(SKColors.Red, Orientation.FromDevice(d, 90).GetPixel(0, 0));
}

[Fact] public void Fit_letterboxes_landscape_into_portrait()
{
    var f = Orientation.FitToCanvas(Solid(1280, 480, SKColors.White), 480, 1280);
    Assert.Equal(SKColors.Black, f.GetPixel(240, 10));
    Assert.Equal(SKColors.White, f.GetPixel(240, 640));
}

[Fact] public void Jpeg_steps_quality_down_under_limit()
{
    using var noise = Noise(1280, 480, seed: 1);
    var big = JpegEncoder.Encode(noise, maxBytes: int.MaxValue);
    Assert.Equal(90, big.Quality);
    var limit = big.Data.Length / 2;
    var capped = JpegEncoder.Encode(noise, maxBytes: limit);
    Assert.True(capped.Quality < 90);
    Assert.True(capped.Data.Length <= limit || capped.Quality == 40);
    Assert.Equal(new byte[] { 0xFF, 0xD8 }, capped.Data[..2]);
}

[Fact] public void Text_sensor_clock_golden_and_missing_value_dash()
{
    var bmp = Render("""[{"type":"sensor","id":"a","x":40,"y":40,"sensor":"cpu/t","label":"CPU","font":{"size":48}},
                         {"type":"sensor","id":"b","x":40,"y":140,"sensor":"role:cpu.fan","label":"FAN","font":{"size":48}},
                         {"type":"clock","id":"c","x":900,"y":40,"timeZone":"Asia/Tokyo","label":"Tokyo","font":{"size":48}},
                         {"type":"image","id":"d","x":10,"y":10,"assetId":"missing","width":10,"height":10}]""",
                     values: new() { ["cpu/t"] = 51.6 }, now: new DateTimeOffset(2026,10,21,14,5,9,TimeSpan.Zero));
    GoldenAssert.Match(bmp, "text-sensor-clock");   // shows "CPU 52 °C", "FAN —", "Tokyo 23:05"; missing image skipped
}
```

`GoldenAssert.Match(SKBitmap actual, string name, int channelTolerance = 8, double maxDiffFraction = 0.002)`: compares with `tests/Glance.Core.Tests/Golden/{name}.png`; when the file is missing or env `GLANCE_UPDATE_GOLDEN=1`, writes it and fails with "golden written — review the PNG and re-run". `Render(...)` is a test helper building a 1280×480 layout on a black color background with a fake hub.

- [ ] **Step 2: Run** — Expected: FAIL.
- [ ] **Step 3: Implement** per Interfaces. Sensor item text = `"{Label} {value}"` (label omitted → value only), alias from `Settings.Aliases` is **not** used on screen (labels are explicit). Clock item = `"{Label} {time}"`. Text drawn top-left at (X,Y) using font ascent so Y is the top of the glyph box.
- [ ] **Step 4: Run** `GLANCE_UPDATE_GOLDEN=1 dotnet test --filter Golden`, open `Golden/text-sensor-clock.png`, check it shows the three lines correctly, then run `dotnet test` — Expected: PASS.

---

### Task 7: Gauges — bar, segmented bar, arc gauge, dial, thresholds

**Files:**
- Create: `src/Glance.Core/Rendering/GaugeMath.cs`, `Items/BarRenderer.cs`, `Items/SegmentedBarRenderer.cs`, `Items/ArcGaugeRenderer.cs`, `Items/DialRenderer.cs`
- Test: `tests/Glance.Core.Tests/Rendering/GaugeTests.cs`

**Interfaces:**
- Consumes: Task 6 `OverlayRenderer` dispatch, `RenderContext`.
- Produces: `static class GaugeMath { (double Min, double Max) Range(GaugeItem g, SensorKind? kind, IReadOnlyList<double?> history, double? value); double Fraction(double? value, double min, double max); string ColorFor(double? value, IReadOnlyList<Threshold> t, string fallback); }` — auto range: Load and Temperature → 0..100; otherwise 0..(max of history ∪ value) × 1.1, minimum span 1; `Fraction` clamps to 0..1, null → 0. `ColorFor`: last threshold with `From <= value`, else fallback; null → fallback.

- [ ] **Step 1: Write the failing tests** (each renders one item on a transparent 400×400 overlay with sensor `s` = value; `C(x,y)` reads a pixel)

```csharp
const string BarJson = """{"type":"bar","id":"g","x":0,"y":0,"sensor":"s","width":200,"height":20,"min":0,"max":100,"rounded":false,
                           "color":"#00FF00","trackColor":"#333333","thresholds":[{"from":70,"color":"#FF0000"}]}""";

[Fact] public void Horizontal_bar_fills_fraction_with_threshold_colour()
{
    var b = Gauge(BarJson, value: 50);
    Assert.Equal(new SKColor(0, 255, 0), C(b, 90, 10));
    Assert.Equal(new SKColor(0x33, 0x33, 0x33), C(b, 150, 10));
    Assert.Equal(new SKColor(255, 0, 0), C(Gauge(BarJson, value: 75), 90, 10));
}

[Fact] public void Vertical_bar_fills_from_bottom()
{
    var b = Gauge("""{"type":"bar","id":"g","x":0,"y":0,"sensor":"s","width":20,"height":200,"vertical":true,"rounded":false,"min":0,"max":100,"color":"#00FF00","trackColor":"#333333"}""", value: 25);
    Assert.Equal(new SKColor(0, 255, 0), C(b, 10, 190));
    Assert.Equal(new SKColor(0x33, 0x33, 0x33), C(b, 10, 20));
}

[Fact] public void Segmented_bar_colours_each_block_by_its_own_value()
{
    var b = Gauge("""{"type":"segmentedBar","id":"g","x":0,"y":0,"sensor":"s","width":200,"height":20,"segments":10,"gap":0,"min":0,"max":100,
                      "trackColor":"#333333","thresholds":[{"from":0,"color":"#00FF00"},{"from":60,"color":"#FFFF00"},{"from":80,"color":"#FF0000"}]}""", value: 75);
    Assert.Equal(new SKColor(0, 255, 0), C(b, 10, 10));     // block 0 (value 5)
    Assert.Equal(new SKColor(255, 255, 0), C(b, 130, 10));  // block 6 (value 65)
    Assert.Equal(new SKColor(0x33, 0x33, 0x33), C(b, 170, 10)); // block 8 unlit (7 lit)
}

[Fact] public void Arc_gauge_sweeps_clockwise_from_start()
{
    var b = Gauge("""{"type":"arcGauge","id":"g","x":0,"y":0,"sensor":"s","width":200,"height":200,"min":0,"max":100,
                      "startAngle":135,"sweepAngle":270,"thickness":20,"color":"#00FF00","trackColor":"#333333"}""", value: 50);
    Assert.Equal(new SKColor(0, 255, 0), C(b, PointOnArc(100, 100, 90, 250)));   // before 270° (top)
    Assert.Equal(new SKColor(0x33, 0x33, 0x33), C(b, PointOnArc(100, 100, 90, 300)));
}

[Fact] public void Dial_needle_points_to_value()
{
    const string Dial = """{"type":"dial","id":"g","x":0,"y":0,"sensor":"s","width":200,"height":200,"min":0,"max":100,
                           "startAngle":135,"sweepAngle":270,"needleColor":"#FF0000"}""";
    Assert.Equal(new SKColor(255, 0, 0), C(Gauge(Dial, value: 0), PointOnArc(100, 100, 60, 135)));
    Assert.Equal(new SKColor(255, 0, 0), C(Gauge(Dial, value: 100), PointOnArc(100, 100, 60, 45)));
}

[Fact] public void Missing_value_draws_empty_gauge_and_dash()
{
    var b = Gauge("""{"type":"bar","id":"g","x":0,"y":0,"sensor":"s","width":200,"height":20,"min":0,"max":100,"rounded":false,"trackColor":"#333333","showValue":true}""", value: null);
    Assert.Equal(new SKColor(0x33, 0x33, 0x33), C(b, 5, 10));
}

[Theory] [InlineData(SensorKind.Load, 0, 100)] [InlineData(SensorKind.Temperature, 0, 100)]
public void Auto_range_fixed_kinds(SensorKind k, double min, double max) =>
    Assert.Equal((min, max), GaugeMath.Range(new BarItem(), k, Array.Empty<double?>(), 42));

[Fact] public void Auto_range_from_history() =>
    Assert.Equal((0, 2200), GaugeMath.Range(new BarItem(), SensorKind.Fan, new double?[] { 1000, 2000, null }, 1500));
```

Shared test helpers (`tests/Glance.Core.Tests/RenderHelpers.cs`, also used by Tasks 6 and 8): `Gauge(itemJson, double? value)` / `Graph(itemJson, IEnumerable<double?> history)` render a single-item 400×400 layout with transparent background through `OverlayRenderer` using a fake hub whose sensor `s` (kind Load) has that value/history; `C(bmp, x, y)` / `C(bmp, (x, y))` return the pixel; `Solid`, `Noise`, `TempDir` are trivial. `PointOnArc(cx, cy, r, degrees)` uses Skia's convention (0° = 3 o'clock, clockwise). Angles are degrees in the same convention.

- [ ] **Step 2: Run** — Expected: FAIL.
- [ ] **Step 3: Implement** renderers per tests; antialiasing on, but pixel probes are at shape interiors. `ShowValue` draws `ValueFormatter` text centred (arc/dial) or right of bar; `Label` drawn above (bar) or below value (arc/dial). Bar/segment fill colour uses `ColorFor`; segmented per-block colour uses the block's midpoint value.
- [ ] **Step 4: Run** — Expected: PASS.

---

### Task 8: Graphs — history graph and sparkline

**Files:**
- Create: `src/Glance.Core/Rendering/Items/HistoryGraphRenderer.cs`, `Items/SparklineRenderer.cs`
- Test: `tests/Glance.Core.Tests/Rendering/GraphTests.cs`

**Interfaces:**
- Consumes: `SensorHub.History(id, seconds)` (Task 3), `GaugeMath` (Task 7), `OverlayRenderer.HistoryNeeds` (Task 6) must return `WindowSeconds` for these items so the hub keeps history.

- [ ] **Step 1: Write the failing tests** (hub history pre-filled through the fake source by ticking)

```csharp
const string LineJson = """{"type":"historyGraph","id":"g","x":0,"y":0,"sensor":"s","width":300,"height":100,"min":0,"max":100,
                            "windowSeconds":60,"area":false,"grid":false,"color":"#00FF00"}""";
const string AreaJson = """{"type":"historyGraph","id":"g","x":0,"y":0,"sensor":"s","width":300,"height":100,"min":0,"max":100,
                            "windowSeconds":60,"area":true,"grid":false,"color":"#00FF00"}""";

[Fact] public void Flat_history_draws_line_at_matching_height()
{
    var b = Graph(LineJson, history: Enumerable.Repeat<double?>(50, 60));
    Assert.Equal(new SKColor(0, 255, 0), C(b, 150, 50));
    Assert.Equal(0, C(b, 150, 20).Alpha);
}

[Fact] public void Area_mode_fills_below_line()
{
    var b = Graph(AreaJson, history: Enumerable.Repeat<double?>(50, 60));
    Assert.True(C(b, 150, 80).Alpha > 0);
    Assert.Equal(0, C(b, 150, 20).Alpha);
}

[Fact] public void Null_samples_leave_gaps()
{
    var b = Graph(LineJson, history: Enumerable.Repeat<double?>(null, 30).Concat(Enumerable.Repeat<double?>(50, 30)));
    Assert.Equal(0, C(b, 60, 50).Alpha);
    Assert.Equal(new SKColor(0, 255, 0), C(b, 250, 50));
}

[Fact] public void Sparkline_spans_full_width()
{
    var b = Graph("""{"type":"sparkline","id":"g","x":0,"y":0,"sensor":"s","width":100,"height":20,"min":0,"max":100,"windowSeconds":10,"color":"#00FF00"}""",
                  history: Enumerable.Repeat<double?>(50, 10));
    Assert.Equal(new SKColor(0, 255, 0), C(b, 2, 10));
    Assert.Equal(new SKColor(0, 255, 0), C(b, 97, 10));
}
```

- [ ] **Step 2: Run** — Expected: FAIL.
- [ ] **Step 3: Implement.** Sample i of N maps to x = i × (width−1)/(N−1); stroke width 2; area fill = line colour at alpha 0x40; grid = 4 horizontal lines at `TrackColor`. Line colour uses `ColorFor` of the latest value.
- [ ] **Step 4: Run** — Expected: PASS.

---

### Task 9: Media import — images, GIF, .zt, asset store

**Files:**
- Create: `src/Glance.Core/Media/Animation.cs`, `ZtReader.cs`, `MediaImporter.cs`, `AssetStore.cs`, `InvalidMediaException.cs`
- Test: `tests/Glance.Core.Tests/Media/ZtReaderTests.cs`, `MediaImporterTests.cs`

**Interfaces:**
- Consumes: `IAssetStore` (Task 6), `Orientation.FromDevice` (Task 6), `JpegEncoder` (Task 6), `GlancePaths.Assets`.
- Produces:
  - `record Animation(int Width, int Height, IReadOnlyList<byte[]> JpegFrames, IReadOnlyList<int> DelaysMs)` — frames in layout orientation
  - `static class ZtReader { (IReadOnlyList<int> DelaysMs, IReadOnlyList<byte[]> Frames) Read(ReadOnlySpan<byte> data); }` — delays = differences of cumulative ms (first = first value); a delay ≤ 0 becomes 40
  - `enum MediaKind { Image, Gif, Zt }`; `record AssetInfo(string Id, string Kind, int Width, int Height, int FrameCount)`
  - `class AssetStore(GlancePaths paths) : IAssetStore` with `AssetInfo Import(byte[] data, MediaKind kind, int ztAuthoredRotation = 0)`, `IReadOnlyList<AssetInfo> List()`, `bool Delete(string id)`
  - Limits: `MediaImporter.MaxBytes = 64 * 1024 * 1024`, `MaxDimension = 8192`, `MaxFrames = 10_000`
  - Storage: `assets/{guid-n}/meta.json` + `image.png` or `frames/0000.jpg…`

- [ ] **Step 1: Write the failing tests**

```csharp
static byte[] Zt(params (int cumMs, byte[] jpg)[] f)
{
    using var ms = new MemoryStream(); using var w = new BinaryWriter(ms);
    w.Write((byte)0xDC); w.Write(f.Length);
    foreach (var x in f) w.Write(x.cumMs);
    foreach (var x in f) { w.Write(x.jpg.Length); w.Write(x.jpg); }
    return ms.ToArray();
}

[Fact] public void Reads_zt_delays_and_frames()
{
    var j = TinyJpeg(SKColors.Red);
    var (d, fr) = ZtReader.Read(Zt((41, j), (82, j)));
    Assert.Equal(new[] { 41, 41 }, d);
    Assert.Equal(j, fr[1]);
}

[Theory]
[InlineData("DD")]                                  // wrong magic
[InlineData("DC FFFFFF7F")]                         // n = int.MaxValue, no data
[InlineData("DC 01000000 29000000 FFFFFFFF")]        // negative frame length
[InlineData("DC 01000000 29000000 10000000 FFD8")]   // truncated frame
public void Rejects_bad_zt(string hex) =>
    Assert.Throws<InvalidMediaException>(() => ZtReader.Read(Convert.FromHexString(hex.Replace(" ", ""))));

[Fact] public void Zt_authored_at_180_is_normalised_to_layout_orientation()
{
    using var dir = new TempDir(); var store = new AssetStore(new GlancePaths(dir.Path));
    var info = store.Import(Zt((41, CornerJpeg(1280, 480, corner: (1279, 479)))), MediaKind.Zt, ztAuthoredRotation: 180);
    using var f0 = SKBitmap.Decode(store.GetAnimation(info.Id)!.JpegFrames[0]);
    Assert.True(IsRedish(f0.GetPixel(2, 2)));
}

// Two-frame 1×1 GIF: frame 0 black (palette 0) 100 ms, frame 1 white (palette 1) 200 ms.
public const string Gif2 =
    "474946383961" + "01000100" + "800000" + "000000FFFFFF" +          // header, 1×1, 2-colour palette
    "21F904000A000000" + "2C000000000100010000" + "0202440100" +       // GCE 10cs, image, LZW pixel 0
    "21F9040014000000" + "2C000000000100010000" + "02024C0100" +       // GCE 20cs, image, LZW pixel 1
    "3B";

[Fact] public void Imports_gif_as_animation()
{
    using var dir = new TempDir(); var store = new AssetStore(new GlancePaths(dir.Path));
    var info = store.Import(Convert.FromHexString(Gif2), MediaKind.Gif);
    var a = store.GetAnimation(info.Id)!;
    Assert.Equal(new[] { 100, 200 }, a.DelaysMs);
}

[Fact] public void Rejects_exe_renamed_and_giant_dimensions()
{
    using var dir = new TempDir(); var store = new AssetStore(new GlancePaths(dir.Path));
    Assert.Throws<InvalidMediaException>(() => store.Import("MZ\u0090\0garbage"u8.ToArray(), MediaKind.Gif));
    var giant = Convert.FromHexString(Gif2); giant[6] = 0x60; giant[7] = 0xEA; giant[8] = 0x60; giant[9] = 0xEA; // 60000×60000
    Assert.Throws<InvalidMediaException>(() => store.Import(giant, MediaKind.Gif));
}
```

`TinyJpeg(color)` and `CornerJpeg(w, h, corner)` are test helpers encoding a Skia bitmap (solid colour / black with a red 6×6 square at `corner`) to JPEG; `IsRedish` tolerates JPEG error (R > 180, G < 80, B < 80).

- [ ] **Step 2: Run** — Expected: FAIL.
- [ ] **Step 3: Implement.** Check magic first (PNG `89504E47`, JPEG `FFD8`, GIF `GIF8`, .zt `DC`), size ≤ `MaxBytes`, then `SKCodec` info dims ≤ `MaxDimension` **before** decoding; ZtReader validates `n ≤ MaxFrames` and `4*n` ≤ remaining before allocating, each length ≥ 0 and ≤ remaining. GIF frames decoded with `SKCodec.GetPixels(..., new SKCodecOptions(i))` honouring `FrameInfo[i].RequiredFrame`, re-encoded JPEG q90. `.zt` frames: if authored rotation ≠ 0, decode → `FromDevice` → re-encode; else stored as-is.
- [ ] **Step 4: Run** — Expected: PASS.

---

### Task 10: DisplaySession — frame pipeline, keep-alive, preview

**Files:**
- Create: `src/Glance.Core/Sessions/DisplaySession.cs`, `OverlayKey.cs`
- Test: `tests/Glance.Core.Tests/Sessions/DisplaySessionTests.cs`, `tests/Glance.Core.Tests/Fakes/FakeConnection.cs`, `tests/Glance.Core.Tests/Fakes/SessionHarness.cs`

`SessionHarness(string? layoutJson, Animation? animation = null, int rotation = 0)` builds a temp data dir, writes the layout (named "L", set active for the fake device key), registers `animation` under asset id `"anim"` (layouts reference it), uses `FakeTimeProvider`, a fake sensor source with mutable `Values`, a counting `OverlayRenderer` subclass (`RenderCount`), and a 1280×480 JPEG `FakeConnection` (`Frames`, `ThrowOnSend`). `Start()` returns the `RunAsync` task; `Advance(t)` advances fake time in 1 ms-aware steps and yields; `Stop(run)` cancels and awaits. Layout constants `StillWithSensor`, `AnimationOnly`, `AnimationWithSensor`, `RedTopLeftImage` live in the test class.

**Interfaces:**
- Consumes: `IDisplayConnection` (T1), `SensorHub` (T3), `LayoutStore`/`SettingsStore` (T5), `OverlayRenderer`, `Orientation`, `JpegEncoder`, `FallbackScreen`, `FontProvider` (T6), `IAssetStore`/`Animation` (T9).
- Produces: `class DisplaySession(IDisplayConnection conn, LayoutStore layouts, SettingsStore settings, SensorHub hub, IAssetStore assets, OverlayRenderer renderer, FontProvider fonts, TimeProvider time, ILogger log)`:
  - `Task RunAsync(CancellationToken ct)` — completes when `ct` is cancelled or the connection throws (exception rethrown to the caller)
  - `void Invalidate()` — layout/settings changed; next frame re-renders immediately
  - `byte[]? GetPreviewJpeg()` — latest composed frame in **layout orientation**, encoded lazily and cached per frame version
  - `double Fps` (frames sent per second over the last 5 s), `string? LayoutName`, `int FramesSent`
  - `static string OverlayKey.Compute(Layout l, RenderContext ctx, OverlayRenderer r)` — layout JSON hash + formatted values of referenced sensors + clock strings + history versions; equal keys ⇒ identical overlay

Behaviour (spec §7.3): no active layout → fallback screen once, re-sent every 1 s. Still background → every 1 s: if `OverlayKey` unchanged re-send previous `EncodedFrame` bytes, else render+compose+rotate+encode. Animation without items → send stored frames pre-rotated to device orientation once at session start (cached), honouring `DelaysMs`. Animation with items → per frame decode + draw overlay (overlay re-rendered at most once per second) + rotate + encode. Layout size ≠ canvas → `FitToCanvas`. On start the session calls `hub.SetInterest(renderer.HistoryNeeds(...))`.

- [ ] **Step 1: Write the failing tests** (`FakeTimeProvider`; `FakeConnection` records frames, can be told to throw)

```csharp
[Fact] public async Task Still_layout_sends_every_second_and_reuses_unchanged_frame()
{
    var h = new SessionHarness(layoutJson: StillWithSensor);       // background color + sensor "cpu/t"
    h.Values["cpu/t"] = 50;
    var run = h.Start();
    await h.Advance(TimeSpan.FromSeconds(3));
    Assert.True(h.Conn.Frames.Count >= 3);
    Assert.Equal(1, h.RenderCount);                                 // unchanged → re-sent bytes
    Assert.Equal(h.Conn.Frames[0].Data.ToArray(), h.Conn.Frames[^1].Data.ToArray());
    h.Values["cpu/t"] = 60;
    await h.Advance(TimeSpan.FromSeconds(1));
    Assert.Equal(2, h.RenderCount);
    await h.Stop(run);
}

[Fact] public async Task No_active_layout_sends_fallback_screen()
{
    var h = new SessionHarness(layoutJson: null);
    var run = h.Start(); await h.Advance(TimeSpan.FromSeconds(2));
    Assert.NotEmpty(h.Conn.Frames);
    await h.Stop(run);
}

[Fact] public async Task Animation_without_items_passes_frames_through_with_delays()
{
    var h = new SessionHarness(layoutJson: AnimationOnly, animation: TwoFrames(delays: new[] { 41, 41 }), rotation: 0);
    var run = h.Start();
    await h.Advance(TimeSpan.FromMilliseconds(41 * 4));
    Assert.Equal(h.Animation.JpegFrames[0], h.Conn.Frames[0].Data.ToArray());
    Assert.Equal(h.Animation.JpegFrames[1], h.Conn.Frames[1].Data.ToArray());
    Assert.Equal(0, h.RenderCount);
    await h.Stop(run);
}

[Fact] public async Task Animation_with_items_composites_every_frame()
{
    var h = new SessionHarness(layoutJson: AnimationWithSensor, animation: TwoFrames(new[] { 41, 41 }));
    var run = h.Start(); await h.Advance(TimeSpan.FromMilliseconds(41 * 4));
    Assert.NotEqual(h.Animation.JpegFrames[0], h.Conn.Frames[0].Data.ToArray());
    await h.Stop(run);
}

[Fact] public async Task Preview_is_layout_oriented_when_device_rotated_180()
{
    var h = new SessionHarness(layoutJson: RedTopLeftImage, rotation: 180);
    var run = h.Start(); await h.Advance(TimeSpan.FromSeconds(1));
    using var p = SKBitmap.Decode(h.Session.GetPreviewJpeg());
    Assert.True(IsRedish(p.GetPixel(2, 2)));
    using var dev = SKBitmap.Decode(h.Conn.Frames[^1].Data.ToArray());
    Assert.True(IsRedish(dev.GetPixel(1277, 477)));
    await h.Stop(run);
}

[Fact] public async Task Connection_failure_ends_run_with_exception()
{
    var h = new SessionHarness(layoutJson: StillWithSensor); h.Conn.ThrowOnSend = new IOException("unplugged");
    var run = h.Start(); await h.Advance(TimeSpan.FromSeconds(1));
    await Assert.ThrowsAsync<IOException>(() => run);
}
```

- [ ] **Step 2: Run** — Expected: FAIL.
- [ ] **Step 3: Implement** per Behaviour. Timing via `Task.Delay(delay, time, ct)`. `RenderCount` in tests is observed through an `OverlayRenderer` subclass or a counting decorator (make `Render` virtual).
- [ ] **Step 4: Run** — Expected: PASS.

---

### Task 11: DeviceManager — discovery, connect, retry/backoff, hot-plug

**Files:**
- Create: `src/Glance.Core/Sessions/DeviceManager.cs`, `DeviceState.cs`
- Test: `tests/Glance.Core.Tests/Sessions/DeviceManagerTests.cs`, `tests/Glance.Core.Tests/Fakes/FakeDriver.cs`, `FakeEnumerator.cs`, `ManagerHarness.cs`

`FakeDriver` (`Id = "fake"`) matches every device; `Next` is the result of the next `ConnectAsync`, `AlwaysFail` makes every connect return `Failed(AlwaysFail)`. `ManagerHarness` exposes `Usb` (`Add`, `Clear`), `Driver`, `Settings`, `Time` (`FakeTimeProvider`), `Manager` (started), `LastConnection` (the `FakeConnection` from Task 10), `Advance(t)` and `AdvanceTo(instant)`. `Dev(path)` = `UsbDeviceInfo(0x1234, 0x0001, path, 37, 513, "Fake")`; `Connected(serial)` = `ConnectResult.Connected` with a new `FakeConnection` for 1280×480.

**Interfaces:**
- Consumes: `IUsbEnumerator`, `IDisplayDriver`, `IHidPortFactory` (T1/T2), `DisplaySession` (T10), `SettingsStore` (T5).
- Produces:
  - `enum DeviceStatus { Connected, Unsupported, Retrying }`
  - `record DeviceState(string Key, string DriverId, string Name, DeviceStatus Status, int Width, int Height, string ModelCode, string Serial, string? Message, DateTimeOffset? NextRetry)`
  - `class DeviceManager(IEnumerable<IDisplayDriver> drivers, IUsbEnumerator usb, IHidPortFactory ports, Func<IDisplayConnection, DisplaySession> sessionFactory, SettingsStore settings, TimeProvider time, ILogger log) : BackgroundService` with `IReadOnlyList<DeviceState> Devices`, `DisplaySession? Session(string key)`, `event Action? DevicesChanged`, `Task RescanAsync()`
  - On first connect of a key, `settings.Devices[key]` is created (`Rotation = 0`, `ActiveLayout = null`).
  - Rescan triggers: `HidSharpUsbEnumerator.Changed` (when the enumerator exposes it), every 5 s fallback, and immediately after a session ends.
  - Unmatched path keys for Retrying/Unsupported devices = `"{driverId}_{path-hash}"` until a serial is known.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public async Task Connects_matching_device_and_creates_settings()
{
    var h = new ManagerHarness(); h.Usb.Add(Dev("p1")); h.Driver.Next = Connected("SER1");
    await h.Manager.RescanAsync();
    var d = Assert.Single(h.Manager.Devices);
    Assert.Equal(DeviceStatus.Connected, d.Status);
    Assert.True(h.Settings.Current.Devices.ContainsKey("fake_SER1"));
    Assert.NotNull(h.Manager.Session("fake_SER1"));
}

[Fact] public async Task Unsupported_model_is_listed()
{
    var h = new ManagerHarness(); h.Usb.Add(Dev("p1")); h.Driver.Next = new ConnectResult.Unsupported("81/01", "SER2");
    await h.Manager.RescanAsync();
    Assert.Equal(DeviceStatus.Unsupported, h.Manager.Devices.Single().Status);
}

[Fact] public async Task Busy_device_retries_with_exponential_backoff_capped_at_30s()
{
    var h = new ManagerHarness(); h.Usb.Add(Dev("p1")); h.Driver.AlwaysFail = "handshake timeout";
    await h.Manager.RescanAsync();
    var delays = new List<TimeSpan>();
    for (int i = 0; i < 7; i++) { var s = h.Manager.Devices.Single(); delays.Add(s.NextRetry!.Value - h.Time.GetUtcNow()); await h.AdvanceTo(s.NextRetry!.Value); }
    Assert.Equal(new[] { 1, 2, 4, 8, 16, 30, 30 }, delays.Select(d => (int)d.TotalSeconds));
    Assert.Equal(DeviceStatus.Retrying, h.Manager.Devices.Single().Status);
    Assert.Contains("handshake timeout", h.Manager.Devices.Single().Message);
}

[Fact] public async Task Unplug_during_streaming_then_replug_reconnects()
{
    var h = new ManagerHarness(); h.Usb.Add(Dev("p1")); h.Driver.Next = Connected("SER1");
    await h.Manager.RescanAsync();
    h.LastConnection.ThrowOnSend = new IOException("gone"); h.Usb.Clear();
    await h.Advance(TimeSpan.FromSeconds(2));
    Assert.Empty(h.Manager.Devices);
    h.Usb.Add(Dev("p1")); h.Driver.Next = Connected("SER1");
    await h.Advance(TimeSpan.FromSeconds(6));
    Assert.Equal(DeviceStatus.Connected, h.Manager.Devices.Single().Status);
}
```

- [ ] **Step 2: Run** — Expected: FAIL.
- [ ] **Step 3: Implement.** Backoff sequence 1,2,4,8,16,30,30… s, reset on success. Session `RunAsync` exceptions are caught, logged at Warning, device removed, rescan triggered. A device still enumerated but with an active session is not reconnected. `DevicesChanged` fires on every state change.
- [ ] **Step 4: Run** — Expected: PASS.

---

### Task 12: Service host, wiring, data dir, logging, hardware smoke

**Files:**
- Create: `src/Glance.Service/Glance.Service.csproj` (`net8.0-windows`, refs all src projects; Serilog.Extensions.Hosting, Serilog.Sinks.File, Microsoft.Extensions.Hosting.WindowsServices), `Program.cs`, `Api/HealthApi.cs`, `wwwroot/index.html`
- Create: `docs/testing/hardware-smoke.md`
- Test: `tests/Glance.Service.Tests/GlanceFactory.cs`, `HealthTests.cs`

**Interfaces:**
- Consumes: everything above.
- Produces: `public partial class Program` (for `WebApplicationFactory`); DI registrations: `GlancePaths`, `TimeProvider.System`, `SettingsStore`, `LayoutStore`, `AssetStore` (as `IAssetStore` too), `FontProvider`, `OverlayRenderer`, `ISensorSource` → `LhmSensorSource`, `IRoleMap` → `LhmRoleMap`, `SensorHub` (+ hosted runner), `IUsbEnumerator`/`IHidPortFactory` → HidSharp, `IDisplayDriver` → `ThermalrightHid5302Driver`, `DeviceManager` (hosted). Kestrel endpoints from `kestrel.json` in the data dir loaded with `reloadOnChange: true` (written on first start: `http://127.0.0.1:8735` and `http://[::1]:8735`). `GET /api/health` → `{"status":"ok","version":"<assembly informational version>"}`. `GlanceFactory(bool withDevice = false) : WebApplicationFactory<Program>` sets `GLANCE_DATA` to a temp dir (`DataDir`), swaps `ISensorSource`/`IRoleMap` for fakes (one sensor `cpu/t` = 50, role `cpu.temperature` → `cpu/t`), replaces drivers with `FakeDriver` + `FakeEnumerator` (one device when `withDevice`), exposes `Settings` (`SettingsStore`) and `Connection` (the `FakeConnection`), and `CreateClient(string? remoteIp = null)` which inserts a startup filter setting `HttpContext.Connection.RemoteIpAddress` (default `127.0.0.1`).
- Layout/settings changes call `DisplaySession.Invalidate()` for affected devices; settings `RolePins` are pushed to `SensorHub.SetPins`.

- [ ] **Step 1: Write the failing test**

```csharp
[Fact] public async Task Health_ok()
{
    await using var f = new GlanceFactory();
    var r = await f.CreateClient().GetFromJsonAsync<JsonElement>("/api/health");
    Assert.Equal("ok", r.GetProperty("status").GetString());
}
```

- [ ] **Step 2: Run** `dotnet test tests/Glance.Service.Tests` — Expected: FAIL.
- [ ] **Step 3: Implement** `Program.cs` with `builder.Host.UseWindowsService(o => o.ServiceName = "Glance")`, Serilog file sink `logs/glance-.log` (daily, 7 files), wiring above, static files from `wwwroot` (placeholder page "Glance is running. The web UI arrives in a later build.").
- [ ] **Step 4: Run** — Expected: PASS.
- [ ] **Step 5: Write `docs/testing/hardware-smoke.md`** with the manual checks: stop any other software driving the screen (the vendor app runs elevated, restarts its helper processes, and relaunches at logon via a scheduled task — exit it from its tray icon, or end `TRCC.exe`, `USBLCD.exe`, `USBLCDNEW.exe`, `HWINFO.exe` from an elevated prompt); run `dotnet run -c Release --project src/Glance.Service` from an elevated terminal; expect fallback screen within 3 s; drop a layout JSON (sample from Task 5) into `%ProgramData%\Glance\layouts`, set `devices.<key>.activeLayout` via settings file; expect it on screen within 2 s; set rotation 180 and confirm orientation; unplug/replug and confirm recovery within 6 s; CPU measurement: PowerShell sampling `(Get-Process Glance.Service).TotalProcessorTime` over 60 s → percent of one core.
- [ ] **Step 6: Run the hardware smoke on the owner's PC** — Expected: all checks pass; record CPU % for a still layout with live sensors (must be ≤ 2%).

---

### Task 13: JSON API — sensors, layouts, devices, settings, assets, preview, events

**Files:**
- Create: `src/Glance.Service/Api/SensorsApi.cs`, `LayoutsApi.cs`, `DevicesApi.cs`, `SettingsApi.cs`, `AssetsApi.cs`, `PreviewApi.cs`, `EventsApi.cs`
- Test: `tests/Glance.Service.Tests/ApiTests.cs`

**Interfaces (routes are this plan's decision; Plan 2's UI consumes exactly these):**
- `GET /api/sensors` → `[{id, hardware, hardwareKind, name, kind, unit, value, alias}]` + `roles: {role: sensorId|null}` + `diagnostics: [{code,message}]`
- `PUT /api/sensors/{id}/alias` `{alias: string|null}`; `PUT /api/roles/{role}` `{sensorId: string|null}`
- `GET /api/layouts` → `[{name, width, height, error?}]`; `GET|PUT|DELETE /api/layouts/{name}`; `POST /api/layouts/{name}/duplicate` `{newName}`. PUT body = Layout JSON; 400 with `{errors:[...]}` on validation failure or invalid name.
- `POST /api/render?device={key}` body = Layout JSON → `image/jpeg` (layout orientation, not sent to the device)
- `GET /api/devices` → `DeviceState[]` + per-device `{rotation, activeLayout, layoutMismatch: bool}`; `PUT /api/devices/{key}` `{rotation?, activeLayout?}` (rotation ∈ 0/90/180/270; layout must exist)
- `GET /api/preview/{key}.jpg` → latest preview, `Cache-Control: no-store`; 404 if unknown/not yet rendered
- `GET /api/settings` (never returns `passwordHash`/`httpsPfxPassword`; returns `hasPassword`); `PUT /api/settings` `{unit?, clock24h?}` (network/password/https in Task 14)
- `POST /api/assets` multipart `file`, `kind` (`image|gif|zt`), `ztRotation?` → `AssetInfo`; request size limit 64 MB; `GET /api/assets`; `DELETE /api/assets/{id}`; `GET /api/assets/{id}/thumbnail.jpg` (≤ 320 px wide)
- `GET /api/logs` → zip of `logs/*.log`
- `GET /api/events` (SSE): `event: sensors` every second `data: {"<id>": value|null, ...}` for all sensors; `event: devices` on `DevicesChanged` with the devices payload.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public async Task Layout_crud_and_validation()
{
    await using var f = new GlanceFactory(); var c = f.CreateClient();
    Assert.Equal(HttpStatusCode.OK, (await c.PutAsync("/api/layouts/Gaming", Json(SampleLayout("Gaming")))).StatusCode);
    Assert.Contains("Gaming", await c.GetStringAsync("/api/layouts"));
    var bad = await c.PutAsync("/api/layouts/Bad", Json(SampleLayout("Bad", windowSeconds: 4000)));
    Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsync("/api/layouts/..%2Fevil", Json(SampleLayout("x")))).StatusCode);
}

[Fact] public async Task Device_rotation_and_active_layout()
{
    await using var f = new GlanceFactory(withDevice: true); var c = f.CreateClient();
    await c.PutAsync("/api/layouts/Gaming", Json(SampleLayout("Gaming")));
    var key = (await c.GetFromJsonAsync<JsonElement>("/api/devices"))[0].GetProperty("key").GetString();
    Assert.Equal(HttpStatusCode.OK, (await c.PutAsync($"/api/devices/{key}", Json(new { rotation = 180, activeLayout = "Gaming" }))).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsync($"/api/devices/{key}", Json(new { rotation = 45 }))).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest, (await c.PutAsync($"/api/devices/{key}", Json(new { activeLayout = "Nope" }))).StatusCode);
}

[Fact] public async Task Render_endpoint_returns_jpeg_without_touching_device()
{
    await using var f = new GlanceFactory(withDevice: true); var c = f.CreateClient();
    var key = (await c.GetFromJsonAsync<JsonElement>("/api/devices"))[0].GetProperty("key").GetString();
    var sentBefore = f.Connection.Frames.Count;
    var r = await c.PostAsync($"/api/render?device={key}", Json(SampleLayout("Draft")));
    Assert.Equal("image/jpeg", r.Content.Headers.ContentType!.MediaType);
    Assert.Equal(0xFF, (await r.Content.ReadAsByteArrayAsync())[0]);
    Assert.False(File.Exists(Path.Combine(f.DataDir, "layouts", "Draft.json")));
}

[Fact] public async Task Upload_rejects_garbage_and_accepts_gif()
{
    await using var f = new GlanceFactory(); var c = f.CreateClient();
    Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync("/api/assets", Upload("evil.gif", "MZ\u0090\0"u8.ToArray(), "gif"))).StatusCode);
    var ok = await c.PostAsync("/api/assets", Upload("a.gif", Convert.FromHexString(MediaImporterTests.Gif2), "gif"));
    Assert.Equal(2, (await ok.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("frameCount").GetInt32());
}

[Fact] public async Task Settings_never_leak_password_hash()
{
    await using var f = new GlanceFactory(); f.Settings.Update(s => s.PasswordHash = "pbkdf2-sha256$1$AA==$AA==");
    var json = await f.CreateClient().GetStringAsync("/api/settings");
    Assert.DoesNotContain("pbkdf2", json);
    Assert.Contains("\"hasPassword\":true", json);
}

[Fact] public async Task Events_stream_sends_sensor_event()
{
    await using var f = new GlanceFactory(); var c = f.CreateClient();
    using var s = await c.GetStreamAsync("/api/events");
    var line = await new StreamReader(s).ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5));
    Assert.Equal("event: sensors", line);
}
```

- [ ] **Step 2: Run** — Expected: FAIL.
- [ ] **Step 3: Implement** endpoints per Interfaces as minimal APIs grouped under `/api`. `MediaKind` from the `kind` field only (never from the file name); `InvalidMediaException` → 400 `{error}`. Layout `PUT` path name must equal body `name` (400 otherwise).
- [ ] **Step 4: Run** — Expected: PASS.

---

### Task 14: Access control — loopback rules, network access, login, HTTPS

**Files:**
- Create: `src/Glance.Service/Security/AccessGate.cs`, `PasswordHasher.cs`, `AuthApi.cs`, `NetworkConfig.cs`, `SelfSignedCert.cs`
- Test: `tests/Glance.Service.Tests/SecurityTests.cs`, `PasswordHasherTests.cs`

**Interfaces:**
- Produces:
  - `static class PasswordHasher { string Hash(string password); bool Verify(string password, string stored); }` — format `pbkdf2-sha256$600000$<salt b64>$<hash b64>`, 16-byte salt, 32-byte hash, `CryptographicOperations.FixedTimeEquals`
  - Routes: `POST /api/auth/login` `{password}` → sets cookie `glance_auth` (HttpOnly, SameSite=Strict, Secure when HTTPS, 7-day sliding); `POST /api/auth/logout`; `GET /api/auth/state` → `{loopback, authenticated, networkAccess}`; `PUT /api/settings/password` `{current?, new}` (min length 8; `current` required when a password exists); `PUT /api/settings/network` `{enabled, https?, port?}` (enabling requires a password, else 400; port 1024..65535) → saves settings and rewrites `kestrel.json` (`http://0.0.0.0:{port}` + `http://[::]:{port}`, or HTTPS endpoints with `https.pfx`; loopback-only endpoints when disabled) for reload
  - `SelfSignedCert.EnsureCreated(GlancePaths p, AppSettings s)` — RSA 2048, SAN `localhost`, machine name, current LAN IPv4s, 5-year validity, random PFX password stored in `HttpsPfxPassword`
- Rules (`AccessGate` middleware, before all endpoints and static files):
  1. `Host` header must be `localhost`, `127.0.0.1`, `[::1]`, the machine name, or one of the machine's IPs (with any port) — else 403 (DNS rebinding).
  2. Mutating methods (POST/PUT/DELETE) with an `Origin` header whose host differs from the request `Host` → 403; mutating `/api` requests must have `Content-Type: application/json` or `multipart/form-data`.
  3. Remote IP loopback (`Connection.RemoteIpAddress`, forwarded headers never honoured) → allowed.
  4. Remote, `NetworkAccess == false` → 403.
  5. Remote, not authenticated → 401 for `/api/*` (except `/api/auth/login`, `/api/auth/state`, `/api/health`); static files allowed (login page lives in Plan 2's UI).
  6. Login rate limit: 5 attempts per minute per remote IP (ASP.NET `RateLimiter`, fixed window) → 429.

- [ ] **Step 1: Write the failing tests** (`GlanceFactory.CreateClient(remoteIp: "192.168.1.50")` sets `RemoteIpAddress` via a test middleware placed before `AccessGate`)

```csharp
[Fact] public void Hash_roundtrip() { var h = PasswordHasher.Hash("correct horse"); Assert.True(PasswordHasher.Verify("correct horse", h)); Assert.False(PasswordHasher.Verify("nope", h)); Assert.StartsWith("pbkdf2-sha256$600000$", h); }

[Fact] public async Task Loopback_needs_no_login_but_foreign_host_is_rejected()
{
    await using var f = new GlanceFactory(); var c = f.CreateClient();
    Assert.Equal(HttpStatusCode.OK, (await c.GetAsync("/api/sensors")).StatusCode);
    var req = new HttpRequestMessage(HttpMethod.Get, "/api/sensors"); req.Headers.Host = "evil.example";
    Assert.Equal(HttpStatusCode.Forbidden, (await c.SendAsync(req)).StatusCode);
}

[Fact] public async Task Cross_origin_post_from_browser_is_rejected()
{
    await using var f = new GlanceFactory(); var c = f.CreateClient();
    var req = new HttpRequestMessage(HttpMethod.Put, "/api/settings") { Content = Json(new { unit = "fahrenheit" }) };
    req.Headers.Add("Origin", "https://evil.example");
    Assert.Equal(HttpStatusCode.Forbidden, (await c.SendAsync(req)).StatusCode);
}

[Fact] public async Task Remote_denied_when_network_access_off_and_x_forwarded_for_ignored()
{
    await using var f = new GlanceFactory(); var c = f.CreateClient(remoteIp: "192.168.1.50");
    var req = new HttpRequestMessage(HttpMethod.Get, "/api/sensors"); req.Headers.Add("X-Forwarded-For", "127.0.0.1");
    Assert.Equal(HttpStatusCode.Forbidden, (await c.SendAsync(req)).StatusCode);
}

[Fact] public async Task Enabling_network_requires_password_then_remote_must_login()
{
    await using var f = new GlanceFactory(); var local = f.CreateClient();
    Assert.Equal(HttpStatusCode.BadRequest, (await local.PutAsync("/api/settings/network", Json(new { enabled = true }))).StatusCode);
    await local.PutAsync("/api/settings/password", Json(new { @new = "correct horse" }));
    Assert.Equal(HttpStatusCode.OK, (await local.PutAsync("/api/settings/network", Json(new { enabled = true }))).StatusCode);
    Assert.Contains("0.0.0.0:8735", File.ReadAllText(Path.Combine(f.DataDir, "kestrel.json")));

    var remote = f.CreateClient(remoteIp: "192.168.1.50");
    Assert.Equal(HttpStatusCode.Unauthorized, (await remote.GetAsync("/api/sensors")).StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await remote.PostAsync("/api/auth/login", Json(new { password = "correct horse" }))).StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await remote.GetAsync("/api/sensors")).StatusCode);
}

[Fact] public async Task Login_is_rate_limited()
{
    await using var f = new GlanceFactory(); f.Settings.Update(s => { s.PasswordHash = PasswordHasher.Hash("correct horse"); s.NetworkAccess = true; });
    var remote = f.CreateClient(remoteIp: "192.168.1.51");
    for (int i = 0; i < 5; i++) await remote.PostAsync("/api/auth/login", Json(new { password = "wrong" }));
    Assert.Equal(HttpStatusCode.TooManyRequests, (await remote.PostAsync("/api/auth/login", Json(new { password = "wrong" }))).StatusCode);
}
```

- [ ] **Step 2: Run** — Expected: FAIL.
- [ ] **Step 3: Implement** per Rules. Cookie auth via `AddAuthentication().AddCookie("glance")`; data-protection keys persisted to `{Root}/keys`. Disabling network access rewrites `kestrel.json` back to loopback.
- [ ] **Step 4: Run** `dotnet test` (whole solution) — Expected: PASS.

---

### Task 15: Performance acceptance on hardware

**Files:**
- Modify: `docs/testing/hardware-smoke.md` (add results table)
- Possibly modify: `src/Glance.Core/Sessions/DisplaySession.cs` (frame cache)

- [ ] **Step 1: Still layout** — activate the Task 5 sample layout (sensors + clock + arc gauge + history graph, colour background) on the owner's screen; measure Glance CPU over 60 s with the web UI closed. Expected: ≤ 2% of one core. If above: profile with `dotnet-counters`/`dotnet-trace`; first fixes to try are (a) `OverlayKey` hit rate (sensor values with 0 decimals change less often), (b) restricting `SensorHub` interest to referenced sensors only.
- [ ] **Step 2: Animated layout** — import the owner's own `C:\TRCCCAP\Data\USBLCD\Theme1280480\001b\Theme.zt` via `POST /api/assets` with `ztRotation=180` (spec §7.2), confirm it displays upright with device rotation 180, add sensor items, activate; measure 60 s. Expected: ≤ 5% of one core at the file's native ~24 fps. If above: cache decoded frames in memory when `frames × width × height × 4 ≤ 512 MB`, otherwise halve the frame rate; re-measure.
- [ ] **Step 3: Passthrough** — same animation with no items. Expected: ≈ 3% or less (spec §5.2: 2.8% measured for 24 fps passthrough).
- [ ] **Step 4: Record** the three numbers and the machine (Ryzen 9 9950X, Arc Pro B70) in the results table.
