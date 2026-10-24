using System.Security.Cryptography;
using System.Text;
using Glance.Core.Devices;
using Glance.Core.Layouts;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Glance.Core.Sessions;

/// <summary>
/// Finds screens, connects them through the matching driver and runs one <see cref="DisplaySession"/> per connected
/// screen. Rescans (enumerator change notification, 5 s fallback, retry due, session end) are serialized; a screen
/// that cannot be opened is retried with back-off (1, 2, 4, 8, 16, then 30 s).
/// </summary>
public sealed class DeviceManager : BackgroundService
{
    private static readonly TimeSpan Fallback = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);

    private readonly IReadOnlyList<IDisplayDriver> _drivers;
    private readonly IUsbEnumerator _usb;
    private readonly IHidPortFactory _ports;
    private readonly Func<IDisplayConnection, DisplaySession> _sessionFactory;
    private readonly SettingsStore _settings;
    private readonly Func<string, bool> _layoutExists;
    private readonly TimeProvider _time;
    private readonly ILogger _log;

    private readonly SemaphoreSlim _rescanGate = new(1, 1);
    private readonly object _gate = new();
    private readonly Dictionary<string, Entry> _entries = new();   // by HID path; guarded by _gate
    private readonly CancellationTokenSource _stop = new();
    private ITimer? _fallbackTimer, _retryTimer;
    private int _pending;
    private long _floorTicks;   // after a failed rescan no retry is scheduled sooner than this (UTC ticks)
    private static readonly TimeSpan HealthyAfter = TimeSpan.FromSeconds(30);

    public DeviceManager(IEnumerable<IDisplayDriver> drivers, IUsbEnumerator usb, IHidPortFactory ports,
        Func<IDisplayConnection, DisplaySession> sessionFactory, SettingsStore settings, Func<string, bool> layoutExists, TimeProvider time, ILogger log)
    {
        _drivers = drivers.ToList();
        _usb = usb;
        _ports = ports;
        _sessionFactory = sessionFactory;
        _settings = settings;
        _layoutExists = layoutExists;
        _time = time;
        _log = log;
    }

    public event Action? DevicesChanged;

    /// <summary>Rescans (and session-end handlers) started but not finished; lets tests wait for the manager to be quiet.</summary>
    internal int PendingWork => Volatile.Read(ref _pending);

    public IReadOnlyList<DeviceState> Devices
    {
        get
        {
            lock (_gate)
                return _entries.OrderBy(e => e.Key, StringComparer.Ordinal).Select(e => e.Value.State).ToList();
        }
    }

    public DisplaySession? Session(string key)
    {
        lock (_gate)
            return _entries.Values.FirstOrDefault(e => e.Session is not null && e.State.Key == key)?.Session;
    }

    /// <summary>Enumerates devices now: removes vanished ones, connects new ones and those whose retry is due.</summary>
    public async Task RescanAsync()
    {
        var ct = _stop.Token;
        try { await _rescanGate.WaitAsync(ct); }
        catch (OperationCanceledException) { return; }
        try
        {
            Dictionary<string, UsbDeviceInfo> present;
            try { present = _usb.Enumerate().GroupBy(d => d.Path).ToDictionary(g => g.Key, g => g.First()); }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Could not enumerate USB devices");
                RescanFailed();
                return;
            }

            List<(string Path, Entry Entry)> vanished;
            lock (_gate) vanished = _entries.Where(e => !present.ContainsKey(e.Key)).Select(e => (e.Key, e.Value)).ToList();
            foreach (var (path, entry) in vanished)
            {
                await EndAsync(entry);
                lock (_gate) _entries.Remove(path);
                Changed();
            }

            foreach (var dev in present.Values)
            {
                if (ct.IsCancellationRequested) return;
                Entry? existing;
                lock (_gate) _entries.TryGetValue(dev.Path, out existing);
                if (existing is { State.Status: DeviceStatus.Connected or DeviceStatus.Unsupported }) continue;
                if (existing?.State.NextRetry is { } due && _time.GetUtcNow() < due) continue;
                try
                {
                    var driver = _drivers.FirstOrDefault(d => d.Matches(dev));
                    if (driver is null) continue;
                    await ConnectAsync(dev, driver, existing?.Attempt ?? 0, ct);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.LogWarning(ex, "Could not handle device {Path}", dev.Path);
                    RescanFailed();
                }
            }
        }
        finally
        {
            _rescanGate.Release();
            ScheduleRetry();
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var reg = stoppingToken.Register(_stop.Cancel);
        if (_usb is INotifyDeviceChanges notify) notify.Changed += OnUsbChanged;
        _retryTimer = _time.CreateTimer(_ => TriggerRescan(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _fallbackTimer = _time.CreateTimer(_ => TriggerRescan(), null, Fallback, Fallback);
        try
        {
            try { await RescanAsync(); }
            catch (Exception ex) when (ex is not OperationCanceledException) { _log.LogError(ex, "Initial device scan failed"); }
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (_usb is INotifyDeviceChanges n) n.Changed -= OnUsbChanged;
            _fallbackTimer.Dispose();
            _retryTimer.Dispose();
            await _rescanGate.WaitAsync();   // a running rescan sees the cancelled token and ends
            try
            {
                List<Entry> all;
                lock (_gate) all = _entries.Values.ToList();
                foreach (var e in all) await EndAsync(e);
                lock (_gate) _entries.Clear();
            }
            finally { _rescanGate.Release(); }
            Changed();
        }
    }

    private void OnUsbChanged() => TriggerRescan();

    /// <param name="counted">The caller already added one to the pending count; it is released when the rescan ends.</param>
    private void TriggerRescan(bool counted = false)
    {
        if (!counted) Interlocked.Increment(ref _pending);
        if (_stop.IsCancellationRequested) { Interlocked.Decrement(ref _pending); return; }
        _ = Task.Run(async () =>
        {
            try { await RescanAsync(); }
            catch (Exception ex) { _log.LogError(ex, "Device rescan failed"); }
            finally { Interlocked.Decrement(ref _pending); }
        });
    }

    private async Task ConnectAsync(UsbDeviceInfo dev, IDisplayDriver driver, int attempt, CancellationToken ct)
    {
        ConnectResult result;
        try
        {
            // Drivers do blocking HID I/O (up to ~2 s); keep it off the caller's thread.
            result = await Task.Run(() => driver.ConnectAsync(dev, _ports, ct), ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return; }
        catch (Exception ex) { result = new ConnectResult.Failed(ex.Message); }

        var name = dev.ProductName ?? driver.DisplayName;
        if (result is ConnectResult.Connected c)
        {
            if (ct.IsCancellationRequested) { await c.Connection.DisposeAsync(); return; }
            var failure = StartSession(dev, c.Connection, attempt);
            if (failure is null) return;
            await c.Connection.DisposeAsync();
            result = new ConnectResult.Failed(failure);
        }
        if (result is ConnectResult.Unsupported u)
            Put(dev.Path, new Entry(dev, new DeviceState($"{driver.Id}_{u.Serial}", driver.Id, name,
                DeviceStatus.Unsupported, 0, 0, u.ModelCode, u.Serial, $"Unsupported model {u.ModelCode}", null), 0));
        else
            Put(dev.Path, Retrying(dev, driver.Id, name, ((ConnectResult.Failed)result).Reason, attempt));
    }

    /// <summary>Creates the device's settings on first sight and starts its session; returns an error text on failure.</summary>
    private string? StartSession(UsbDeviceInfo dev, IDisplayConnection conn, int attempt)
    {
        var info = conn.Info;
        DisplaySession session;
        try
        {
            if (!_settings.Current.Devices.TryGetValue(info.Key, out var known) || !known.DefaultLayoutApplied)
                _settings.Update(s =>
                {
                    s.Devices.TryAdd(info.Key, new DeviceSettings { Rotation = 0, ActiveLayout = null });
                    var d = s.Devices[info.Key];
                    if (d.DefaultLayoutApplied) return;
                    if (d.ActiveLayout is null && _layoutExists("Blend")) d.ActiveLayout = "Blend";
                    d.DefaultLayoutApplied = true;
                });
            session = _sessionFactory(conn);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Could not start a session for {Key}", info.Key);
            return ex.Message;
        }

        var state = new DeviceState(info.Key, info.DriverId, info.Name, DeviceStatus.Connected, info.Width, info.Height,
            info.ModelCode, info.Serial, null, null);
        var cts = new CancellationTokenSource();
        var entry = new Entry(dev, state, attempt) { Conn = conn, Session = session, Cts = cts, StartedAt = _time.GetUtcNow() };
        entry.Run = Task.Run(() => session.RunAsync(cts.Token));
        Put(dev.Path, entry);
        _ = MonitorAsync(entry);
        return null;
    }

    /// <summary>Handles a session that ends by itself (send failure): release the screen and retry shortly.</summary>
    private async Task MonitorAsync(Entry entry)
    {
        Exception? error = null;
        try { await entry.Run!; }
        catch (Exception ex) { error = ex; }
        if (Interlocked.Exchange(ref entry.Ended, 1) != 0) return;   // a rescan or the host stop is tearing it down
        Interlocked.Increment(ref _pending);   // handed to the follow-up rescan below

        if (error is not null) _log.LogWarning(error, "Screen {Key} stopped: {Reason}", entry.State.Key, error.Message);
        try { await ReleaseAsync(entry); }
        finally { entry.Teardown.TrySetResult(); }

        var s = entry.State;
        // A session that ran long enough was healthy; a short one keeps escalating the back-off.
        var attempt = _time.GetUtcNow() - entry.StartedAt >= HealthyAfter ? 0 : entry.Attempt;
        var retry = Retrying(entry.Dev, s.DriverId, s.Name, error is null ? "session ended" : $"connection lost: {error.Message}", attempt);
        lock (_gate)
            if (_entries.TryGetValue(entry.Dev.Path, out var current) && ReferenceEquals(current, entry))
                _entries[entry.Dev.Path] = retry;
        Changed();
        ScheduleRetry();
        TriggerRescan(counted: true);
    }

    /// <summary>Stops a connected entry's session, waits for it to finish and releases the connection.</summary>
    private async Task EndAsync(Entry entry)
    {
        if (entry.Run is null) return;
        if (Interlocked.Exchange(ref entry.Ended, 1) != 0)
        {
            await entry.Teardown.Task;   // the monitor owns the teardown; wait until the connection is released
            return;
        }
        try
        {
            entry.Cts!.Cancel();
            try { await entry.Run; }
            catch (Exception ex) { _log.LogDebug(ex, "Session {Key} ended with an error while stopping", entry.State.Key); }
            await ReleaseAsync(entry);
        }
        finally { entry.Teardown.TrySetResult(); }
    }

    private async Task ReleaseAsync(Entry entry)
    {
        try { await entry.Conn!.DisposeAsync(); }
        catch (Exception ex) { _log.LogDebug(ex, "Disposing the connection of {Key} failed", entry.State.Key); }
        entry.Cts!.Dispose();
    }

    private Entry Retrying(UsbDeviceInfo dev, string driverId, string name, string reason, int attempt)
    {
        var delay = TimeSpan.FromSeconds(Math.Min(1 << Math.Min(attempt, 5), MaxBackoff.TotalSeconds));
        var key = $"{driverId}_{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(dev.Path)))[..8]}";
        return new Entry(dev, new DeviceState(key, driverId, name, DeviceStatus.Retrying, 0, 0, "", "",
            $"busy, retrying: {reason}", _time.GetUtcNow() + delay), attempt + 1);
    }

    /// <summary>A rescan hit an error: do not schedule retries sooner than the fallback interval.</summary>
    private void RescanFailed() =>
        Interlocked.Exchange(ref _floorTicks, (_time.GetUtcNow() + Fallback).UtcTicks);

    private void Put(string path, Entry entry)
    {
        lock (_gate) _entries[path] = entry;
        Changed();
    }

    private void Changed()
    {
        try { DevicesChanged?.Invoke(); }
        catch (Exception ex) { _log.LogError(ex, "DevicesChanged subscriber failed"); }
    }

    /// <summary>Arms the retry timer for the earliest pending retry (no timer while nothing is waiting).</summary>
    private void ScheduleRetry()
    {
        if (_retryTimer is null || _stop.IsCancellationRequested) return;
        DateTimeOffset? next;
        lock (_gate) next = _entries.Values.Select(e => e.State.NextRetry).Min();
        var wait = next is { } due ? due - _time.GetUtcNow() : Timeout.InfiniteTimeSpan;
        if (next is not null)
        {
            var floor = new DateTimeOffset(Interlocked.Read(ref _floorTicks), TimeSpan.Zero) - _time.GetUtcNow();
            if (wait < floor) wait = floor;
            if (wait < TimeSpan.Zero) wait = TimeSpan.Zero;
        }
        try { _retryTimer.Change(wait, Timeout.InfiniteTimeSpan); }
        catch (ObjectDisposedException) { }
    }

    private sealed class Entry(UsbDeviceInfo dev, DeviceState state, int attempt)
    {
        public UsbDeviceInfo Dev { get; } = dev;
        public DeviceState State { get; } = state;
        public int Attempt { get; } = attempt;   // failed connects so far; picks the next back-off
        public IDisplayConnection? Conn { get; init; }
        public DisplaySession? Session { get; init; }
        public CancellationTokenSource? Cts { get; init; }
        public Task? Run { get; set; }
        public DateTimeOffset StartedAt { get; init; }
        public TaskCompletionSource Teardown { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int Ended;
    }
}
