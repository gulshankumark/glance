using Glance.Core.Media;
using SkiaSharp;
using static Glance.Testing.TestMedia;

namespace Glance.Core.Tests.Media;

public class ZtReaderTests
{
    [Fact] public void Reads_zt_delays_and_frames()
    {
        var j = TinyJpeg(SKColors.Red);
        var (d, fr) = ZtReader.Read(Zt((41, j), (82, j)));
        Assert.Equal(new[] { 41, 41 }, d);
        Assert.Equal(j, fr[1]);
    }

    [Fact] public void Non_positive_delay_becomes_40()
    {
        var j = TinyJpeg(SKColors.Red);
        var (d, _) = ZtReader.Read(Zt((0, j), (50, j), (50, j)));
        Assert.Equal(new[] { 40, 50, 40 }, d);
    }

    [Theory]
    [InlineData("DD")]                                  // wrong magic
    [InlineData("")]                                    // empty
    [InlineData("DC")]                                  // no count
    [InlineData("DC 00000000")]                         // zero frames
    [InlineData("DC FFFFFF7F")]                         // n = int.MaxValue, no data
    [InlineData("DC FFFFFFFF")]                         // negative n
    [InlineData("DC 01000000 29000000 FFFFFFFF")]        // negative frame length
    [InlineData("DC 01000000 29000000 10000000 FFD8")]   // truncated frame
    [InlineData("DC 01000000 29000000 FFFFFF7F")]        // huge frame length
    public void Rejects_bad_zt(string hex) =>
        Assert.Throws<InvalidMediaException>(() => ZtReader.Read(Convert.FromHexString(hex.Replace(" ", ""))));

    [Fact] public void Rejects_more_frames_than_limit_even_with_a_table_that_fits()
    {
        var data = new byte[1 + 4 + 4 * (MediaImporter.MaxFrames + 1)];
        data[0] = 0xDC;
        BitConverter.GetBytes(MediaImporter.MaxFrames + 1).CopyTo(data, 1);
        Assert.Throws<InvalidMediaException>(() => ZtReader.Read(data));
    }
}
