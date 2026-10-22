using Glance.Drivers.Thermalright;

namespace Glance.Drivers.Thermalright.Tests;

public class Hid5302ProtocolTests
{
    public const string RealReply = "00DADBDCDD018000000000000001000000100000004250323139343007011D334247022078";

    [Fact] public void Handshake_bytes() =>
        Assert.Equal(Convert.FromHexString("DADBDCDD" + "0000000000000000" + "01" + "00000000000000"),
                     Hid5302Protocol.BuildHandshake());

    [Fact] public void Parses_real_reply()
    {
        var r = Hid5302Protocol.ParseHandshakeReply(Convert.FromHexString(RealReply))!;
        Assert.Equal(0x01, r.SubModel);
        Assert.Equal(0x80, r.Model);
        Assert.Equal("4250323139343007011D334247022078", r.Serial);
    }

    [Fact] public void Rejects_wrong_magic_or_flag()
    {
        var bad = Convert.FromHexString(RealReply); bad[2] = 0x00;
        Assert.Null(Hid5302Protocol.ParseHandshakeReply(bad));
        var noFlag = Convert.FromHexString(RealReply); noFlag[13] = 0x00;
        Assert.Null(Hid5302Protocol.ParseHandshakeReply(noFlag));
        Assert.Null(Hid5302Protocol.ParseHandshakeReply(new byte[5]));
    }

    [Fact] public void Serial_absent_when_byte17_not_0x10()
    {
        var b = Convert.FromHexString(RealReply); b[17] = 0;
        Assert.Null(Hid5302Protocol.ParseHandshakeReply(b)!.Serial);
    }

    [Theory]
    [InlineData(0x80, 0x01, true)] [InlineData(0x80, 0x02, true)]
    [InlineData(0x80, 0x03, false)] [InlineData(0x81, 0x01, false)]
    public void Panel_resolution(byte model, byte sub, bool known)
    {
        var p = Hid5302Protocol.ResolvePanel(model, sub);
        if (known) Assert.Equal((1280, 480), p); else Assert.Null(p);
    }

    [Fact] public void Jpeg_frame_header() =>
        Assert.Equal(Convert.FromHexString("DADBDCDD02000000" + "0005E001" + "02000000" + "03000000" + "010203"),
                     Hid5302Protocol.BuildJpegFrame(new byte[] { 1, 2, 3 }, 1280, 480));

    [Fact] public void Chunks_into_513_byte_reports_zero_padded()
    {
        var payload = Enumerable.Range(0, 1030).Select(i => (byte)(i % 251 + 1)).ToArray();
        var reports = Hid5302Protocol.ToReports(payload);
        Assert.Equal(3, reports.Count);
        Assert.All(reports, r => { Assert.Equal(513, r.Length); Assert.Equal(0, r[0]); });
        Assert.Equal(payload[512..1024], reports[1][1..513]);
        Assert.Equal(payload[1024..], reports[2][1..7]);
        Assert.All(reports[2][7..], b => Assert.Equal(0, b));
    }
}
