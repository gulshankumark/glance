using SkiaSharp;

namespace Glance.Core.Rendering;

public record EncodedJpeg(byte[] Data, int Quality);

public static class JpegEncoder
{
    public const int StartQuality = 90, MinQuality = 40, QualityStep = 10;

    /// <summary>Largest JPEG a device frame may be.</summary>
    public const int MaxBytes = 450_000;

    /// <summary>
    /// Encodes at quality 90, stepping down by 10 (floor 40) until the size is within <paramref name="maxBytes"/>.
    /// <paramref name="codec"/> defaults to Skia.
    /// </summary>
    public static EncodedJpeg Encode(SKBitmap bmp, int maxBytes = MaxBytes, IJpegCodec? codec = null)
    {
        using var pixels = bmp.PeekPixels();
        return Encode(pixels, maxBytes, codec);
    }

    /// <inheritdoc cref="Encode(SKBitmap, int, IJpegCodec)"/>
    /// <remarks>Encodes the pixels in place (no intermediate image copy); alpha is ignored, chroma is 4:2:0.</remarks>
    public static EncodedJpeg Encode(SKPixmap pixels, int maxBytes = MaxBytes, IJpegCodec? codec = null)
    {
        // codecs other than Skia take BGRA8888 only
        if (codec is null || pixels.ColorType != SKColorType.Bgra8888) codec = SkiaJpegCodec.Instance;
        for (var q = StartQuality; ; q -= QualityStep)
        {
            var data = codec.Encode(pixels, q);
            if (data.Length <= maxBytes || q <= MinQuality) return new EncodedJpeg(data, q);
        }
    }
}
