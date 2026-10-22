using Glance.Core.Sensors;

namespace Glance.Service;

/// <summary>
/// While the web UI is in use the hub reads every sensor (current values only, no history). Interest is held by
/// open event streams (ref-counted) and by short leases that API requests take; it is dropped when none remain.
/// </summary>
public sealed class UiInterest(SensorHub hub, TimeProvider time)
{
    private const string Owner = "ui";

    private readonly object _gate = new();
    private int _streams;
    private DateTimeOffset _leaseUntil;
    private bool _active;
    private ITimer? _timer;

    /// <summary>How long a request keeps the interest alive; tests shorten it.</summary>
    public TimeSpan Lease { get; set; } = TimeSpan.FromSeconds(10);

    public int OpenStreams { get { lock (_gate) return _streams; } }
    public bool Active { get { lock (_gate) return _active; } }

    public IDisposable OpenStream()
    {
        lock (_gate)
        {
            _streams++;
            Activate();
        }
        return new Release(this);
    }

    public void Touch()
    {
        lock (_gate)
        {
            _leaseUntil = time.GetUtcNow() + Lease;
            Activate();
            _timer?.Dispose();
            _timer = time.CreateTimer(_ => Expire(), null, Lease, Timeout.InfiniteTimeSpan);
        }
    }

    private void Activate()   // caller holds _gate
    {
        if (_active) return;
        hub.SetInterest(Owner, hub.Sensors.Select(s => s.Id).Distinct().ToDictionary(id => id, _ => 0));
        _active = true;
    }

    private void Expire()
    {
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
            var remaining = _leaseUntil - time.GetUtcNow();
            if (remaining > TimeSpan.Zero)   // extended meanwhile, or the timer fired a little early
            {
                _timer = time.CreateTimer(_ => Expire(), null, remaining, Timeout.InfiniteTimeSpan);
                return;
            }
            Deactivate();
        }
    }

    private void Deactivate()   // caller holds _gate
    {
        if (!_active || _streams > 0 || time.GetUtcNow() < _leaseUntil) return;
        hub.ClearInterest(Owner);
        _active = false;
    }

    private sealed class Release(UiInterest owner) : IDisposable
    {
        private int _done;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _done, 1) != 0) return;
            lock (owner._gate)
            {
                owner._streams--;
                owner.Deactivate();
            }
        }
    }
}
