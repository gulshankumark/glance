using Glance.Core.Devices;

namespace Glance.Testing;

public sealed class FakeEnumerator : IUsbEnumerator, INotifyDeviceChanges
{
    private readonly object _gate = new();
    private readonly List<UsbDeviceInfo> _devices = new();

    public event Action? Changed;

    public Exception? ThrowOnEnumerate { get; set; }
    public int EnumerateCalls => Volatile.Read(ref _calls);
    private int _calls;

    public void Add(UsbDeviceInfo dev) { lock (_gate) _devices.Add(dev); }
    public void Clear() { lock (_gate) _devices.Clear(); }

    /// <summary>Raises <see cref="Changed"/> like an OS hot-plug notification would.</summary>
    public void Notify() => Changed?.Invoke();

    public IReadOnlyList<UsbDeviceInfo> Enumerate()
    {
        Interlocked.Increment(ref _calls);
        if (ThrowOnEnumerate is { } ex) throw ex;
        lock (_gate) return _devices.ToList();
    }
}
