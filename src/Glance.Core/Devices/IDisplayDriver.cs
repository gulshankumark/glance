namespace Glance.Core.Devices;

public interface IDisplayConnection : IAsyncDisposable
{
    DisplayInfo Info { get; }
    Task SendFrameAsync(EncodedFrame frame, CancellationToken ct);
}

public interface IDisplayDriver
{
    string Id { get; }
    string DisplayName { get; }
    bool Matches(UsbDeviceInfo dev);
    Task<ConnectResult> ConnectAsync(UsbDeviceInfo dev, IHidPortFactory ports, CancellationToken ct);
}

public interface IUsbEnumerator
{
    IReadOnlyList<UsbDeviceInfo> Enumerate();
}

/// <summary>An enumerator that raises <see cref="Changed"/> when devices are plugged or unplugged.</summary>
public interface INotifyDeviceChanges
{
    event Action? Changed;
}
