namespace Glance.Core.Devices;

public interface IHidPort : IDisposable
{
    void Write(ReadOnlySpan<byte> report);

    /// <summary>Reads one input report into <paramref name="buffer"/>; returns 0 on timeout.</summary>
    int Read(Span<byte> buffer, TimeSpan timeout);
}

public interface IHidPortFactory
{
    IHidPort Open(UsbDeviceInfo dev);
}
