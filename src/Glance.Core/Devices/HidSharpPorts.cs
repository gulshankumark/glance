using HidSharp;

namespace Glance.Core.Devices;

public sealed class HidSharpPortFactory : IHidPortFactory
{
    public IHidPort Open(UsbDeviceInfo dev)
    {
        var device = DeviceList.Local.GetHidDevices()
            .FirstOrDefault(d => d.DevicePath == dev.Path)
            ?? throw new IOException($"HID device not found: {dev.Path}");
        return new Port(device.Open());
    }

    sealed class Port : IHidPort
    {
        readonly HidStream _stream;
        public Port(HidStream stream) => _stream = stream;

        public void Write(ReadOnlySpan<byte> report) => _stream.Write(report.ToArray());

        public int Read(Span<byte> buffer, TimeSpan timeout)
        {
            _stream.ReadTimeout = (int)timeout.TotalMilliseconds;
            var tmp = new byte[buffer.Length];
            try
            {
                int n = _stream.Read(tmp, 0, tmp.Length);
                tmp.AsSpan(0, n).CopyTo(buffer);
                return n;
            }
            catch (TimeoutException) { return 0; }
        }

        public void Dispose() => _stream.Dispose();
    }
}

public sealed class HidSharpUsbEnumerator : IUsbEnumerator, INotifyDeviceChanges
{
    public event Action? Changed;

    public HidSharpUsbEnumerator() => DeviceList.Local.Changed += (_, _) => Changed?.Invoke();

    public IReadOnlyList<UsbDeviceInfo> Enumerate() =>
        DeviceList.Local.GetHidDevices().Select(ToInfo).ToList();

    static UsbDeviceInfo ToInfo(HidDevice d)
    {
        string? name = null;
        try { name = d.GetProductName(); } catch { }
        int inLen = 0, outLen = 0;
        try { inLen = d.GetMaxInputReportLength(); outLen = d.GetMaxOutputReportLength(); } catch { }
        return new UsbDeviceInfo(d.VendorID, d.ProductID, d.DevicePath, inLen, outLen, name);
    }
}
