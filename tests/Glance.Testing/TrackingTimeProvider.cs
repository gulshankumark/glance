using Microsoft.Extensions.Time.Testing;

namespace Glance.Testing;

/// <summary>
/// A <see cref="FakeTimeProvider"/> wrapper that counts armed timers, so a test can tell when every timed loop is
/// parked waiting for (fake) time again.
/// </summary>
public sealed class TrackingTimeProvider : TimeProvider
{
    private int _armed;

    public FakeTimeProvider Fake { get; } = new();

    /// <summary>Timers that are scheduled to fire (one-shot timers stop counting once they fire or are disposed).</summary>
    public int ArmedTimers => Volatile.Read(ref _armed);

    public override DateTimeOffset GetUtcNow() => Fake.GetUtcNow();
    public override long GetTimestamp() => Fake.GetTimestamp();
    public override long TimestampFrequency => Fake.TimestampFrequency;
    public override TimeZoneInfo LocalTimeZone => Fake.LocalTimeZone;

    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) =>
        new Tracked(this, callback, state, dueTime, period);

    private sealed class Tracked : ITimer
    {
        private readonly TrackingTimeProvider _owner;
        private readonly ITimer _inner;
        private readonly object _gate = new();
        private bool _armed, _periodic;

        public Tracked(TrackingTimeProvider owner, TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _owner = owner;
            Set(dueTime, period);
            _inner = owner.Fake.CreateTimer(s => { OnFire(); callback(s); }, state, dueTime, period);
        }

        public bool Change(TimeSpan dueTime, TimeSpan period)
        {
            Set(dueTime, period);
            return _inner.Change(dueTime, period);
        }

        public void Dispose()
        {
            Set(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _inner.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            Dispose();
            return ValueTask.CompletedTask;
        }

        private void OnFire()
        {
            lock (_gate) if (!_periodic) Arm(false);
        }

        private void Set(TimeSpan dueTime, TimeSpan period)
        {
            lock (_gate)
            {
                _periodic = period != Timeout.InfiniteTimeSpan && period != TimeSpan.Zero;
                Arm(dueTime != Timeout.InfiniteTimeSpan);
            }
        }

        private void Arm(bool on)   // caller holds _gate
        {
            if (on == _armed) return;
            _armed = on;
            Interlocked.Add(ref _owner._armed, on ? 1 : -1);
        }
    }
}
