using Glance.Core.Rendering;
using SkiaSharp;

namespace Glance.Core.Media;

public enum MediaKind { Image, Gif, Zt }

/// <summary>A validated upload ready to store: <see cref="Png"/> for a still image, otherwise <see cref="Frames"/> and <see cref="DelaysMs"/>.</summary>
public sealed record ImportedMedia(MediaKind Kind, int Width, int Height, byte[]? Png,
    IReadOnlyList<byte[]> Frames, IReadOnlyList<int> DelaysMs);

/// <summary>
/// Validates and converts uploads. Order of checks, so a hostile file is rejected before any large allocation:
/// size, magic bytes, codec header dimensions, frame count, and only then pixel decoding.
/// </summary>
public static class MediaImporter
{
    public const int MaxBytes = 64 * 1024 * 1024;
    public const int MaxDimension = 8192;
    /// <summary>
    /// Stills are stored at most this many pixels on their long side (aspect kept); larger uploads are downscaled at
    /// import. 4096 is the maximum layout size, so nothing drawn is lost, and a stored still decodes to at most 64 MB.
    /// </summary>
    public const int MaxStoredImageDimension = 4096;
    public const int MaxFrames = 10_000;
    /// <summary>Sum over all frames of width x height; checked from headers before any pixel is decoded.</summary>
    public const long MaxTotalPixels = 1_000_000_000;
    /// <summary>Total size of the stored JPEG frames of one animation; enforced while frames are produced.</summary>
    public const long MaxTotalEncodedBytes = 256L * 1024 * 1024;
    private const int JpegMaxBytes = 450_000;
    private const int GifDefaultDelayMs = 100;   // GIF convention: a 0 or negative delay is shown as 100 ms (.zt uses 40 ms)

    public static ImportedMedia Import(byte[] data, MediaKind kind, int ztAuthoredRotation = 0,
        long maxTotalPixels = MaxTotalPixels, long maxTotalEncodedBytes = MaxTotalEncodedBytes)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (data.Length == 0) throw new InvalidMediaException("The file is empty.");
        if (data.Length > MaxBytes) throw new InvalidMediaException($"The file is larger than {MaxBytes / (1024 * 1024)} MB.");
        if (ztAuthoredRotation is not (0 or 90 or 180 or 270))
            throw new InvalidMediaException("Rotation must be 0, 90, 180 or 270.");
        return kind switch
        {
            MediaKind.Image => ImportImage(data),
            MediaKind.Gif => ImportGif(data, maxTotalPixels, maxTotalEncodedBytes),
            MediaKind.Zt => ImportZt(data, ztAuthoredRotation, maxTotalPixels, maxTotalEncodedBytes),
            _ => throw new InvalidMediaException("Unknown media kind."),
        };
    }

    private static ImportedMedia ImportImage(byte[] data)
    {
        if (!(StartsWith(data, 0x89, 0x50, 0x4E, 0x47) || StartsWith(data, 0xFF, 0xD8)))
            throw new InvalidMediaException("Not a PNG or JPEG image.");
        using var codec = CreateCodec(data);
        CheckDimensions(codec.Info.Width, codec.Info.Height);
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        using var decoded = new SKBitmap(info);
        if (codec.GetPixels(info, decoded.GetPixels(), new SKCodecOptions(0)) != SKCodecResult.Success)
            throw new InvalidMediaException("The image is corrupt or truncated.");
        using var downscaled = Downscale(decoded, MaxStoredImageDimension);
        var bmp = downscaled ?? decoded;
        using var image = SKImage.FromBitmap(bmp);
        using var png = image.Encode(SKEncodedImageFormat.Png, 100);
        return new ImportedMedia(MediaKind.Image, bmp.Width, bmp.Height, png.ToArray(), Array.Empty<byte[]>(), Array.Empty<int>());
    }

    /// <summary>
    /// A copy of <paramref name="bmp"/> scaled so its long side is at most <paramref name="maxLongSide"/> (aspect kept,
    /// mipmapped so large reductions do not alias), or null when it already fits.
    /// </summary>
    public static SKBitmap? Downscale(SKBitmap bmp, int maxLongSide)
    {
        var longSide = Math.Max(bmp.Width, bmp.Height);
        if (longSide <= maxLongSide) return null;
        var scale = (double)maxLongSide / longSide;
        var w = Math.Clamp((int)Math.Round(bmp.Width * scale), 1, maxLongSide);
        var h = Math.Clamp((int)Math.Round(bmp.Height * scale), 1, maxLongSide);
        return bmp.Resize(new SKImageInfo(w, h, bmp.ColorType, bmp.AlphaType), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
               ?? throw new InvalidMediaException("The image could not be resized.");
    }

    private static ImportedMedia ImportGif(byte[] data, long maxTotalPixels, long maxTotalEncodedBytes)
    {
        if (!StartsWith(data, 0x47, 0x49, 0x46, 0x38)) throw new InvalidMediaException("Not a GIF image.");   // "GIF8"
        using var codec = CreateCodec(data);
        var info = codec.Info;
        CheckDimensions(info.Width, info.Height);
        var count = codec.FrameCount;
        if (count < 1 || count > MaxFrames) throw new InvalidMediaException($"A GIF must have between 1 and {MaxFrames} frames.");
        if ((long)count * info.Width * info.Height > maxTotalPixels)
            throw new InvalidMediaException("The GIF is too large: its frames add up to more pixels than allowed.");
        var frameInfo = codec.FrameInfo;
        if (frameInfo.Length != count) throw new InvalidMediaException("The GIF frame table is inconsistent.");

        // A frame may be drawn on top of an earlier, already composited frame (RequiredFrame, which already accounts
        // for the disposal method). Keep only the composited bitmaps that a later frame still needs.
        var lastUse = new int[count];
        for (var i = 0; i < count; i++)
        {
            var req = frameInfo[i].RequiredFrame;
            if (req >= i) throw new InvalidMediaException("The GIF frame table is inconsistent.");
            if (req >= 0) lastUse[req] = Math.Max(lastUse[req], i);
        }

        long encodedTotal = 0;
        var kept = new Dictionary<int, SKBitmap>();
        var frames = new byte[count][];
        var delays = new int[count];
        var imageInfo = new SKImageInfo(info.Width, info.Height, SKColorType.Bgra8888, SKAlphaType.Premul);
        try
        {
            for (var i = 0; i < count; i++)
            {
                var req = frameInfo[i].RequiredFrame;
                var bmp = req >= 0 ? kept[req].Copy() : new SKBitmap(imageInfo);
                if (req < 0) bmp.Erase(SKColors.Transparent);
                var disposeBmp = true;
                try
                {
                    var result = codec.GetPixels(imageInfo, bmp.GetPixels(), bmp.RowBytes, new SKCodecOptions(i, req));
                    if (result != SKCodecResult.Success) throw new InvalidMediaException("The GIF could not be decoded.");
                    frames[i] = JpegEncoder.Encode(bmp, JpegMaxBytes).Data;
                    encodedTotal += frames[i].Length;
                    if (encodedTotal > maxTotalEncodedBytes) throw new InvalidMediaException("The animation is too large after conversion.");
                    var d = frameInfo[i].Duration;
                    delays[i] = d <= 0 ? GifDefaultDelayMs : d;
                    if (lastUse[i] > i) { kept[i] = bmp; disposeBmp = false; }
                }
                finally { if (disposeBmp) bmp.Dispose(); }

                foreach (var key in kept.Keys.Where(k => lastUse[k] <= i).ToList())
                {
                    kept[key].Dispose();
                    kept.Remove(key);
                }
            }
        }
        finally { foreach (var b in kept.Values) b.Dispose(); }
        return new ImportedMedia(MediaKind.Gif, info.Width, info.Height, null, frames, delays);
    }

    private static ImportedMedia ImportZt(byte[] data, int rotation, long maxTotalPixels, long maxTotalEncodedBytes)
    {
        var (delays, rawFrames) = ZtReader.Read(data);
        var frames = new byte[rawFrames.Count][];
        int width = 0, height = 0;

        // Pass 1: headers only. Checks magic, per-frame and aggregate dimensions before any pixel is decoded.
        long totalPixels = 0;
        for (var i = 0; i < rawFrames.Count; i++)
        {
            var raw = rawFrames[i];
            if (!StartsWith(raw, 0xFF, 0xD8)) throw new InvalidMediaException($"Frame {i} of the .zt file is not a JPEG.");
            using var codec = CreateCodec(raw);
            CheckDimensions(codec.Info.Width, codec.Info.Height);
            var (w, h) = rotation is 90 or 270 ? (codec.Info.Height, codec.Info.Width) : (codec.Info.Width, codec.Info.Height);
            if (i == 0) (width, height) = (w, h);
            else if ((w, h) != (width, height)) throw new InvalidMediaException("The .zt frames have different sizes.");
            totalPixels += (long)w * h;
            if (totalPixels > maxTotalPixels)
                throw new InvalidMediaException("The animation is too large: its frames add up to more pixels than allowed.");
        }

        // Pass 2: convert, enforcing the encoded-size budget as frames are produced.
        long encodedTotal = 0;
        for (var i = 0; i < rawFrames.Count; i++)
        {
            if (rotation == 0) frames[i] = rawFrames[i];
            else
            {
                using var device = SKBitmap.Decode(rawFrames[i]) ?? throw new InvalidMediaException($"Frame {i} of the .zt file could not be decoded.");
                using var layout = Orientation.FromDevice(device, rotation);
                frames[i] = JpegEncoder.Encode(layout, JpegMaxBytes).Data;
            }
            encodedTotal += frames[i].Length;
            if (encodedTotal > maxTotalEncodedBytes) throw new InvalidMediaException("The animation is too large after conversion.");
        }
        return new ImportedMedia(MediaKind.Zt, width, height, null, frames, delays);
    }

    private static SKCodec CreateCodec(byte[] data) =>
        SKCodec.Create(new MemoryStream(data, writable: false)) ?? throw new InvalidMediaException("The file is not a valid image.");

    private static void CheckDimensions(int w, int h)
    {
        if (w < 1 || h < 1 || w > MaxDimension || h > MaxDimension)
            throw new InvalidMediaException($"Image dimensions must be between 1 and {MaxDimension} pixels (got {w}x{h}).");
    }

    private static bool StartsWith(byte[] data, params byte[] magic) => data.AsSpan().StartsWith(magic);
}
