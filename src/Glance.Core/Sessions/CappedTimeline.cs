namespace Glance.Core.Sessions;

/// <summary>
/// An animation's looping timeline (each frame shown for its delay, at least the minimum) sampled at no more than a
/// given frame rate: which frame is current at a position, and when the next frame should go out.
/// </summary>
internal sealed class CappedTimeline
{
    private readonly long[] _starts;   // ticks from the start of a loop at which each frame begins
    private readonly long _period;     // ticks per loop
    private readonly long _interval;   // minimum ticks between frames sent

    private CappedTimeline(long[] starts, long period, long interval)
    {
        _starts = starts;
        _period = period;
        _interval = interval;
    }

    /// <summary>
    /// The timeline for <paramref name="maxFps"/>, or null when there is no cap or no frame is shown for less than
    /// 1/<paramref name="maxFps"/> s, so every frame can go out at its own timing.
    /// </summary>
    public static CappedTimeline? CreateIfLimiting(IReadOnlyList<int> delaysMs, int minDelayMs, int? maxFps)
    {
        if (maxFps is not ({ } fps and > 0)) return null;
        var interval = TimeSpan.TicksPerSecond / fps;
        var starts = new long[delaysMs.Count];
        long t = 0, shortest = long.MaxValue;
        for (var i = 0; i < starts.Length; i++)
        {
            starts[i] = t;
            var d = Math.Max(delaysMs[i], minDelayMs) * TimeSpan.TicksPerMillisecond;
            shortest = Math.Min(shortest, d);
            t += d;
        }
        return shortest >= interval ? null : new CappedTimeline(starts, t, interval);
    }

    /// <summary>The frame current at <paramref name="position"/> (time since the animation started).</summary>
    public int FrameAt(TimeSpan position)
    {
        var i = Array.BinarySearch(_starts, position.Ticks % _period);
        return i >= 0 ? i : ~i - 1;
    }

    /// <summary>
    /// When the frame after the one sent at <paramref name="sentAt"/> should go out: the next frame change of the
    /// timeline, but no sooner than the minimum interval after <paramref name="sentAt"/>.
    /// </summary>
    public TimeSpan NextEmission(TimeSpan sentAt)
    {
        var i = FrameAt(sentAt);
        var loopStart = sentAt.Ticks - sentAt.Ticks % _period;
        var change = loopStart + (i + 1 < _starts.Length ? _starts[i + 1] : _period);
        return TimeSpan.FromTicks(Math.Max(sentAt.Ticks + _interval, change));
    }
}
