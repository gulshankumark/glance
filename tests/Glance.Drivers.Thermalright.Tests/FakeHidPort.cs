using Glance.Core.Devices;

namespace Glance.Drivers.Thermalright.Tests;

public sealed class FakeHidPort : IHidPort
{
    readonly Queue<byte[]> _replies;
    public List<byte[]> Written { get; } = new();
    public bool Disposed { get; private set; }

    public FakeHidPort(params byte[][] replies) => _replies = new Queue<byte[]>(replies);

    public void Write(ReadOnlySpan<byte> report) => Written.Add(report.ToArray());

    public int Read(Span<byte> buffer, TimeSpan timeout)
    {
        if (!_replies.TryDequeue(out var r)) return 0;
        r.AsSpan(0, Math.Min(r.Length, buffer.Length)).CopyTo(buffer);
        return Math.Min(r.Length, buffer.Length);
    }

    public void Dispose() => Disposed = true;
}

public sealed class FakeFactory : IHidPortFactory
{
    readonly IHidPort _port;
    public FakeFactory(IHidPort port) => _port = port;
    public IHidPort Open(UsbDeviceInfo dev) => _port;
}
