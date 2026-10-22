namespace Glance.Core.Devices;

public enum PixelFormat { Jpeg, Rgb565LE, Rgb565BE }

public record UsbDeviceInfo(int VendorId, int ProductId, string Path, int MaxInputReportLength, int MaxOutputReportLength, string? ProductName);

public record DisplayInfo(string DriverId, string Name, int Width, int Height, IReadOnlyList<PixelFormat> Formats, string Serial, string ModelCode)
{
    public string Key => $"{DriverId}_{Serial}";
}

public record EncodedFrame(PixelFormat Format, int Width, int Height, ReadOnlyMemory<byte> Data);

public abstract record ConnectResult
{
    private ConnectResult() { }

    public sealed record Connected(IDisplayConnection Connection) : ConnectResult;
    public sealed record Unsupported(string ModelCode, string Serial) : ConnectResult;
    public sealed record Failed(string Reason) : ConnectResult;
}
