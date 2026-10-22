using SkiaSharp;

namespace Glance.Core.Rendering;

/// <summary>
/// Baseline JPEG decode/encode. Implementations must be safe to call from several threads at once and must not
/// throw for bad input (decode reports false instead).
/// </summary>
public interface IJpegCodec
{
    /// <summary>
    /// Decodes <paramref name="jpeg"/> into <paramref name="target"/> (BGRA8888, exactly the image's size; opaque
    /// result). False when the data is not a decodable JPEG of that size.
    /// </summary>
    bool TryDecode(ReadOnlySpan<byte> jpeg, SKPixmap target);

    /// <summary>Encodes BGRA8888 pixels (alpha ignored) as a baseline 4:2:0 JPEG at <paramref name="quality"/> (1..100).</summary>
    byte[] Encode(SKPixmap source, int quality);
}

/// <summary>SkiaSharp's bundled libjpeg-turbo: cross-platform, the default.</summary>
public sealed class SkiaJpegCodec : IJpegCodec
{
    public static readonly SkiaJpegCodec Instance = new();

    public bool TryDecode(ReadOnlySpan<byte> jpeg, SKPixmap target)
    {
        try
        {
            using var data = SKData.CreateCopy(jpeg);
            using var codec = SKCodec.Create(data);
            if (target.ColorType != SKColorType.Bgra8888) return false;
            if (codec is null || codec.EncodedFormat != SKEncodedImageFormat.Jpeg
                || (codec.Info.Width, codec.Info.Height) != (target.Width, target.Height)) return false;
            var info = new SKImageInfo(target.Width, target.Height, SKColorType.Bgra8888, SKAlphaType.Opaque);
            return codec.GetPixels(info, target.GetPixels(), target.RowBytes, SKCodecOptions.Default) is SKCodecResult.Success or SKCodecResult.IncompleteInput;
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            return false;
        }
    }

    public byte[] Encode(SKPixmap source, int quality)
    {
        using var data = source.Encode(new SKJpegEncoderOptions(quality, SKJpegEncoderDownsample.Downsample420, SKJpegEncoderAlphaOption.Ignore))
            ?? throw new InvalidOperationException("JPEG encoding failed");
        return data.ToArray();
    }
}

/// <summary>Reads a JPEG's dimensions from its frame header without decoding it.</summary>
public static class JpegHeader
{
    public static bool TryReadSize(ReadOnlySpan<byte> jpeg, out int width, out int height)
    {
        width = height = 0;
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8) return false;
        var pos = 2;
        while (pos + 4 <= jpeg.Length)
        {
            if (jpeg[pos] != 0xFF) return false;
            var marker = jpeg[pos + 1];
            if (marker == 0xFF) { pos++; continue; }   // fill byte
            var length = (jpeg[pos + 2] << 8) | jpeg[pos + 3];
            if (length < 2) return false;
            var isSof = marker is >= 0xC0 and <= 0xCF && marker is not (0xC4 or 0xC8 or 0xCC);
            if (isSof)
            {
                if (pos + 9 > jpeg.Length) return false;
                height = (jpeg[pos + 5] << 8) | jpeg[pos + 6];
                width = (jpeg[pos + 7] << 8) | jpeg[pos + 8];
                return width > 0 && height > 0;
            }
            if (marker == 0xDA) return false;   // scan before any frame header
            pos += 2 + length;
        }
        return false;
    }
}
