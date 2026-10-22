using Glance.Core.Devices;

namespace Glance.Testing;

/// <summary>Records every frame sent; set <see cref="ThrowOnSend"/> to simulate an unplugged device.</summary>
public sealed class FakeConnection(string driverId = "fake", string serial = "SER1", int width = 1280, int height = 480,
    PixelFormat[]? formats = null) : IDisplayConnection
{
    private readonly List<EncodedFrame> _frames = new();

    public DisplayInfo Info { get; } = new(driverId, "Fake screen", width, height, formats ?? new[] { PixelFormat.Jpeg }, serial, "FAKE");

    /// <summary>Snapshot of the frames sent so far (data copied at send time).</summary>
    public IReadOnlyList<EncodedFrame> Frames { get { lock (_frames) return _frames.ToList(); } }

    public Exception? ThrowOnSend { get; set; }

    /// <summary>Awaited before each frame is recorded, e.g. to make sends take (fake) time.</summary>
    public Func<CancellationToken, Task>? BeforeSend { get; set; }

    /// <summary>Awaited at the start of DisposeAsync, e.g. to hold a teardown open.</summary>
    public Func<Task>? OnDispose { get; set; }

    public bool Disposed { get; private set; }

    public async Task SendFrameAsync(EncodedFrame frame, CancellationToken ct)
    {
        if (ThrowOnSend is { } ex) throw ex;
        if (BeforeSend is { } before) await before(ct);
        lock (_frames) _frames.Add(frame with { Data = frame.Data.ToArray() });
    }

    public async ValueTask DisposeAsync()
    {
        if (OnDispose is { } hook) await hook();
        Disposed = true;
    }
}
