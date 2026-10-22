using Glance.Core.Rendering;
using static Glance.Core.Tests.RenderHelpers;

namespace Glance.Core.Tests.Rendering;

public class JpegEncoderTests
{
    [Fact] public void Jpeg_steps_quality_down_under_limit()
    {
        using var noise = Noise(1280, 480, seed: 1);
        var big = JpegEncoder.Encode(noise, maxBytes: int.MaxValue);
        Assert.Equal(90, big.Quality);
        var limit = big.Data.Length / 2;
        var capped = JpegEncoder.Encode(noise, maxBytes: limit);
        Assert.True(capped.Quality < 90);
        Assert.True(capped.Data.Length <= limit || capped.Quality == 40);
        Assert.Equal(new byte[] { 0xFF, 0xD8 }, capped.Data[..2]);
    }
}
