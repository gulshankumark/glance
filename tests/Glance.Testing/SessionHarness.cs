using Glance.Core.Layouts;
using Glance.Core.Media;
using Glance.Core.Rendering;
using Glance.Core.Sensors;
using Glance.Core.Sessions;
using Microsoft.Extensions.Logging.Abstractions;

namespace Glance.Testing;

/// <summary>
/// One <see cref="DisplaySession"/> on a <see cref="FakeConnection"/> with fake time, a fake sensor "cpu/t" and a
/// temporary data directory. <paramref name="layoutJson"/> (layout named "L") is made the device's active layout;
/// <paramref name="animation"/> is registered as asset "anim". The hub is ticked by <see cref="Advance"/>, once per
/// fake second, just before each whole second.
/// </summary>
public sealed class SessionHarness : IDisposable
{
    private static readonly TimeSpan Step = TimeSpan.FromMilliseconds(1);
    private readonly TempDir _dir = new();
    private readonly FakeSensorSource _source = new(("cpu/t", "cpu", SensorKind.Temperature));
    private readonly CancellationTokenSource _cts = new();
    private Task? _run;
    private long _elapsedMs;

    public SessionHarness(string? layoutJson, Animation? animation = null, int rotation = 0, FakeConnection? conn = null,
        IJpegCodec? codec = null)
    {
        Conn = conn ?? new FakeConnection();
        Animation = animation!;
        if (animation is not null) Assets.SetAnimation("anim", animation);
        var paths = new GlancePaths(_dir.Path);
        Layouts = new LayoutStore(paths, Time);
        Settings = new SettingsStore(paths);
        if (layoutJson is not null) Layouts.Save(GlanceJson.Deserialize<Layout>(layoutJson)!);
        Settings.Update(s => s.Devices[Conn.Info.Key] = new DeviceSettings
        {
            ActiveLayout = layoutJson is null ? null : "L",
            Rotation = rotation,
        });
        Hub = HubFactory.NewHub(_source, time: Time);
        Session = new DisplaySession(Conn, Layouts, Settings, Hub, Assets, Renderer, new FontProvider(), Time,
            NullLogger<DisplaySession>.Instance, codec);
    }

    public TrackingTimeProvider Time { get; } = new();
    public FakeConnection Conn { get; }
    public FakeAssetStore Assets { get; } = new();
    public CountingRenderer Renderer { get; } = new();
    public LayoutStore Layouts { get; }
    public SettingsStore Settings { get; }
    public SensorHub Hub { get; }
    public DisplaySession Session { get; }
    /// <summary>The animation passed to the constructor (null when none was).</summary>
    public Animation Animation { get; }
    public FakeSensorSource Source => _source;
    public Dictionary<string, double?> Values => _source.Values;
    public int RenderCount => Renderer.RenderCount;

    /// <summary>Takes a first sensor reading (as the service hub would have) and starts the session.</summary>
    public Task Start()
    {
        Hub.SetInterest("harness", _source.Discover().ToDictionary(s => s.Id, _ => 0));
        Hub.Tick();
        _run = Task.Run(() => Session.RunAsync(_cts.Token));
        return _run;
    }

    /// <summary>Advances fake time 1 ms at a time, letting the session finish its work at each step.</summary>
    public async Task Advance(TimeSpan t)
    {
        await WaitIdle();
        for (var end = _elapsedMs + (long)t.TotalMilliseconds; _elapsedMs < end;)
        {
            if ((_elapsedMs + 1) % 1000 == 0) Hub.Tick();
            Time.Fake.Advance(Step);
            _elapsedMs++;
            await WaitIdle();
        }
    }

    public async Task Stop(Task run)
    {
        _cts.Cancel();
        await run;
    }

    public void Dispose()
    {
        _cts.Cancel();
        Layouts.Dispose();
        _dir.Dispose();
    }

    /// <summary>The session is idle when its single wait timer is armed (or it has ended).</summary>
    private async Task WaitIdle()
    {
        var deadline = DateTime.UtcNow.AddSeconds(10);
        for (var spins = 0; !(_run is null || _run.IsCompleted || Time.ArmedTimers >= 1); spins++)
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException("session did not go idle");
            if (spins < 50) await Task.Yield();
            else await Task.Delay(1);
        }
    }
}
