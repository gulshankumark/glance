using System.Security.Cryptography;
using System.Text;
using Glance.Core.Devices;

namespace Glance.Drivers.Thermalright;

public sealed class ThermalrightHid5302Driver : IDisplayDriver
{
    const int VendorId = 0x0416, ProductId = 0x5302, OutputReportLength = 513;
    static readonly TimeSpan HandshakeTimeout = TimeSpan.FromSeconds(2);

    public string Id => "thermalright-hid-5302";
    public string DisplayName => "Thermalright USB LCD (HID)";

    public bool Matches(UsbDeviceInfo dev) =>
        dev.VendorId == VendorId && dev.ProductId == ProductId && dev.MaxOutputReportLength == OutputReportLength;

    public Task<ConnectResult> ConnectAsync(UsbDeviceInfo dev, IHidPortFactory ports, CancellationToken ct)
    {
        IHidPort? port = null;
        try
        {
            port = ports.Open(dev);
            foreach (var r in Hid5302Protocol.ToReports(Hid5302Protocol.BuildHandshake()))
                port.Write(r);

            var buf = new byte[Math.Max(dev.MaxInputReportLength, 64)];
            int n = port.Read(buf, HandshakeTimeout);
            if (n == 0) return Fail(port, "No handshake reply from device (is another program using it?)");

            var reply = Hid5302Protocol.ParseHandshakeReply(buf.AsSpan(0, n));
            if (reply is null) return Fail(port, "Unrecognised handshake reply");

            string serial = reply.Serial ?? PathHash(dev.Path);
            string modelCode = $"{reply.Model:X2}/{reply.SubModel:X2}";
            var panel = Hid5302Protocol.ResolvePanel(reply.Model, reply.SubModel);
            if (panel is null)
            {
                port.Dispose();
                return Task.FromResult<ConnectResult>(new ConnectResult.Unsupported(modelCode, serial));
            }

            var info = new DisplayInfo(Id, DisplayName, panel.Value.Width, panel.Value.Height,
                new[] { PixelFormat.Jpeg }, serial, modelCode);
            return Task.FromResult<ConnectResult>(new ConnectResult.Connected(new Connection(port, info)));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Fail(port, ex.Message);
        }
    }

    static Task<ConnectResult> Fail(IHidPort? port, string reason)
    {
        port?.Dispose();
        return Task.FromResult<ConnectResult>(new ConnectResult.Failed(reason));
    }

    static string PathHash(string path) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path)).AsSpan(0, 8));

    sealed class Connection : IDisplayConnection
    {
        readonly IHidPort _port;
        public DisplayInfo Info { get; }

        public Connection(IHidPort port, DisplayInfo info) { _port = port; Info = info; }

        public Task SendFrameAsync(EncodedFrame frame, CancellationToken ct)
        {
            if (frame.Format != PixelFormat.Jpeg)
                throw new ArgumentException("Only JPEG frames are supported.", nameof(frame));
            if (frame.Width != Info.Width || frame.Height != Info.Height)
                throw new ArgumentException($"Frame must be {Info.Width}x{Info.Height}.", nameof(frame));
            foreach (var report in Hid5302Protocol.ToReports(
                         Hid5302Protocol.BuildJpegFrame(frame.Data.Span, frame.Width, frame.Height)))
            {
                ct.ThrowIfCancellationRequested();
                _port.Write(report);
            }
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() { _port.Dispose(); return ValueTask.CompletedTask; }
    }
}
