using Glance.Core.Devices;

namespace Glance.Drivers.Thermalright.Tests;

public class DriverTests
{
    static UsbDeviceInfo Dev(int outLen = 513) => new(0x0416, 0x5302, "hid#1", 37, outLen, "USBDISPLAY");

    [Fact] public void Matches_only_5302_with_513_byte_reports()
    {
        var d = new ThermalrightHid5302Driver();
        Assert.True(d.Matches(Dev()));
        Assert.False(d.Matches(Dev(65)));
        Assert.False(d.Matches(Dev() with { ProductId = 0x5303 }));
    }

    [Fact] public async Task Connect_sends_handshake_and_returns_1280x480_jpeg()
    {
        var port = new FakeHidPort(Convert.FromHexString(Hid5302ProtocolTests.RealReply));
        var r = await new ThermalrightHid5302Driver().ConnectAsync(Dev(), new FakeFactory(port), default);
        var c = Assert.IsType<ConnectResult.Connected>(r).Connection;
        Assert.Equal(Hid5302Protocol.ToReports(Hid5302Protocol.BuildHandshake())[0], port.Written[0]);
        Assert.Equal((1280, 480), (c.Info.Width, c.Info.Height));
        Assert.Equal(new[] { PixelFormat.Jpeg }, c.Info.Formats);
        Assert.Equal("80/01", c.Info.ModelCode);
        Assert.Equal("thermalright-hid-5302_4250323139343007011D334247022078", c.Info.Key);
    }

    [Fact] public async Task Unknown_model_is_unsupported_not_failed()
    {
        var reply = Convert.FromHexString(Hid5302ProtocolTests.RealReply); reply[6] = 0x81;
        var r = await new ThermalrightHid5302Driver().ConnectAsync(Dev(), new FakeFactory(new FakeHidPort(reply)), default);
        Assert.Equal("81/01", Assert.IsType<ConnectResult.Unsupported>(r).ModelCode);
    }

    [Fact] public async Task No_reply_is_failed()
    {
        var port = new FakeHidPort();
        var r = await new ThermalrightHid5302Driver().ConnectAsync(Dev(), new FakeFactory(port), default);
        Assert.IsType<ConnectResult.Failed>(r);
        Assert.True(port.Disposed);
    }

    [Fact] public async Task SendFrame_writes_header_plus_jpeg_reports()
    {
        var port = new FakeHidPort(Convert.FromHexString(Hid5302ProtocolTests.RealReply));
        var c = ((ConnectResult.Connected)await new ThermalrightHid5302Driver().ConnectAsync(Dev(), new FakeFactory(port), default)).Connection;
        port.Written.Clear();
        await c.SendFrameAsync(new EncodedFrame(PixelFormat.Jpeg, 1280, 480, new byte[] { 0xFF, 0xD8, 9 }), default);
        Assert.Equal(Hid5302Protocol.ToReports(Hid5302Protocol.BuildJpegFrame(new byte[] { 0xFF, 0xD8, 9 }, 1280, 480)), port.Written);
        await Assert.ThrowsAsync<ArgumentException>(() =>
            c.SendFrameAsync(new EncodedFrame(PixelFormat.Rgb565LE, 1280, 480, new byte[4]), default));
        await c.DisposeAsync();
        Assert.True(port.Disposed);
    }

    [Theory]
    [InlineData(1280, 481)]
    [InlineData(640, 480)]
    [InlineData(0, 0)]
    [InlineData(70000, 480)]
    public async Task SendFrame_rejects_wrong_dimensions(int w, int h)
    {
        var port = new FakeHidPort(Convert.FromHexString(Hid5302ProtocolTests.RealReply));
        var c = ((ConnectResult.Connected)await new ThermalrightHid5302Driver().ConnectAsync(Dev(), new FakeFactory(port), default)).Connection;
        port.Written.Clear();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            c.SendFrameAsync(new EncodedFrame(PixelFormat.Jpeg, w, h, new byte[] { 0xFF, 0xD8 }), default));
        Assert.Empty(port.Written);
    }

    [Fact] public async Task Missing_serial_falls_back_to_path_hash()
    {
        var reply = Convert.FromHexString(Hid5302ProtocolTests.RealReply); reply[17] = 0;
        var r = await new ThermalrightHid5302Driver().ConnectAsync(Dev(), new FakeFactory(new FakeHidPort(reply)), default);
        var c = Assert.IsType<ConnectResult.Connected>(r).Connection;
        // first 8 bytes of SHA-256("hid#1")
        Assert.Equal("thermalright-hid-5302_65F97B1A234D6B82", c.Info.Key);
    }
}
