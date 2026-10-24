using Glance.Core.Devices;
using Glance.Core.Layouts;
using Glance.Core.Rendering;
using Glance.Core.Sessions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Glance.Testing;

/// <summary>
/// A started <see cref="DeviceManager"/> over a <see cref="FakeEnumerator"/> and <see cref="FakeDriver"/> with fake
/// time. <see cref="Advance"/> moves time in small steps and waits for the manager and its sessions to settle.
/// </summary>
public sealed class ManagerHarness : IDisposable
{
    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(500);
    private readonly TempDir _dir = new();
    private readonly FakeSensorSource _source = new(("cpu/t", "cpu", Core.Sensors.SensorKind.Temperature));

    public ManagerHarness(Action<FakeEnumerator>? configureUsb = null)
    {
        configureUsb?.Invoke(Usb);
        var paths = new GlancePaths(_dir.Path);
        Layouts = new LayoutStore(paths, Time);
        Settings = new SettingsStore(paths);
        var hub = HubFactory.NewHub(_source, time: Time);
        var assets = new FakeAssetStore();
        var renderer = new CountingRenderer();
        var fonts = new FontProvider();
        Manager = new DeviceManager(new[] { Driver }, Usb, new NoPorts(),
            conn => new DisplaySession(conn, Layouts, Settings, hub, assets, renderer, fonts, Time,
                NullLogger<DisplaySession>.Instance),
            Settings, n => Layouts.Get(n) is not null, Time, NullLogger<DeviceManager>.Instance);
        Manager.StartAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    public FakeEnumerator Usb { get; } = new();
    public FakeDriver Driver { get; } = new();
    public TrackingTimeProvider Time { get; } = new();
    public LayoutStore Layouts { get; }
    public SettingsStore Settings { get; }
    public DeviceManager Manager { get; }
    public FakeConnection LastConnection => Driver.LastConnection ?? throw new InvalidOperationException("nothing connected yet");

    public static UsbDeviceInfo Dev(string path) => new(0x1234, 0x0001, path, 37, 513, "Fake");
    public static ConnectResult Connected(string serial) => new ConnectResult.Connected(new FakeConnection("fake", serial));

    public Task AdvanceTo(DateTimeOffset instant) => Advance(instant - Time.GetUtcNow());

    public async Task Advance(TimeSpan t)
    {
        await Settle();
        for (var left = t; left > TimeSpan.Zero; left -= Step)
        {
            Time.Fake.Advance(left < Step ? left : Step);
            await Settle();
        }
    }

    /// <summary>Waits until no rescan is pending and every live session is parked on its timer again.</summary>
    public async Task Settle()
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        for (var spins = 0; !IsSettled(); spins++)
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("device manager did not settle");
            if (spins < 50) await Task.Yield();
            else await Task.Delay(1);
        }
    }

    // The manager owns one periodic timer; each live session owns one wait timer when idle.
    private bool IsSettled() =>
        Manager.PendingWork == 0 &&
        Time.ArmedTimers >= 1 + Manager.Devices.Count(d => d.Status == DeviceStatus.Connected);

    public void Dispose()
    {
        Manager.StopAsync(CancellationToken.None).GetAwaiter().GetResult();
        Layouts.Dispose();
        _dir.Dispose();
    }

    private sealed class NoPorts : IHidPortFactory
    {
        public IHidPort Open(UsbDeviceInfo dev) => throw new NotSupportedException();
    }
}
