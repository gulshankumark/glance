using System.Text.Json;
using System.Text.RegularExpressions;
using Glance.Core.Layouts;
using Glance.Core.Rendering;
using SkiaSharp;

namespace Glance.Core.Media;

public record AssetInfo(string Id, string Kind, int Width, int Height, int FrameCount);

/// <summary>
/// Disk-backed media store: <c>assets/{guid-n}/meta.json</c> plus <c>image.png</c> or <c>frames/0000.jpg...</c>, and
/// <c>thumb.jpg</c> (at most <see cref="ThumbnailMaxWidth"/> wide; made at import, or on first request for older assets).
/// Ids are always generated here (never client supplied) and every id is validated before it touches a path.
/// Safe for concurrent readers while imports and deletes run: an import is built in a temporary directory and
/// moved into place only when complete, and readers treat anything missing or unreadable as "no such asset".
/// </summary>
public sealed partial class AssetStore : IAssetStore
{
    private const string TempPrefix = "tmp-";
    // JPEG bytes kept in memory across animations; at least one maximal importable animation always fits.
    private const long AnimationCacheBudgetBytes = MediaImporter.MaxTotalEncodedBytes;

    private sealed record Meta(string Kind, int Width, int Height, int FrameCount, int[]? DelaysMs);

    private readonly string _root;
    private readonly long _imageBudget;
    private readonly object _imageLock = new();
    private readonly Dictionary<string, SKBitmap> _images = new();
    private readonly List<string> _imageOrder = new();       // least recently used first
    private long _imageBytes;
    private readonly object _animationLock = new();
    private readonly Dictionary<string, (Animation Animation, long Bytes)> _animations = new();
    private readonly List<string> _animationOrder = new();   // least recently used first
    private long _animationBytes;

    public const int ThumbnailMaxWidth = 320;
    private const string ThumbName = "thumb.jpg";

    /// <summary>
    /// Decoded stills kept in memory, least recently used evicted first: 64 MB holds one still of the largest stored
    /// size (4096 x 4096 BGRA, see <see cref="MediaImporter.MaxStoredImageDimension"/>) or several screen-size ones.
    /// A still over the budget (only possible for assets imported before the import downscale) is never cached.
    /// </summary>
    public const long DefaultImageCacheBudgetBytes = 64L * 1024 * 1024;

    /// <summary>Bytes of decoded stills now cached.</summary>
    public long CachedImageBytes { get { lock (_imageLock) return _imageBytes; } }

    public AssetStore(GlancePaths paths, long imageCacheBudgetBytes = DefaultImageCacheBudgetBytes)
    {
        _imageBudget = imageCacheBudgetBytes;
        _root = paths.Assets;
        Directory.CreateDirectory(_root);
        foreach (var leftover in Directory.EnumerateDirectories(_root, TempPrefix + "*"))   // interrupted imports/deletes
            TryDeleteDirectory(leftover);
    }

    public AssetInfo Import(byte[] data, MediaKind kind, int ztAuthoredRotation = 0)
    {
        var media = MediaImporter.Import(data, kind, ztAuthoredRotation);
        var id = Guid.NewGuid().ToString("N");
        var temp = Path.Combine(_root, TempPrefix + id);
        try
        {
            Directory.CreateDirectory(temp);
            var isImage = media.Png is not null;
            if (isImage) File.WriteAllBytes(Path.Combine(temp, "image.png"), media.Png!);
            else
            {
                var frames = Directory.CreateDirectory(Path.Combine(temp, "frames")).FullName;
                for (var i = 0; i < media.Frames.Count; i++) File.WriteAllBytes(Path.Combine(frames, FrameName(i)), media.Frames[i]);
            }
            if (MakeThumbnail(isImage ? media.Png! : media.Frames[0]) is { } thumb) File.WriteAllBytes(Path.Combine(temp, ThumbName), thumb);
            var meta = new Meta(media.Kind.ToString().ToLowerInvariant(), media.Width, media.Height,
                isImage ? 1 : media.Frames.Count, isImage ? null : media.DelaysMs.ToArray());
            File.WriteAllText(Path.Combine(temp, "meta.json"), JsonSerializer.Serialize(meta, GlanceJson.Options));
            Directory.Move(temp, Path.Combine(_root, id));
            return ToInfo(id, meta);
        }
        catch
        {
            TryDeleteDirectory(temp);
            throw;
        }
    }

    public IReadOnlyList<AssetInfo> List()
    {
        var result = new List<AssetInfo>();
        foreach (var dir in Directory.EnumerateDirectories(_root))
        {
            var id = Path.GetFileName(dir);
            if (!IsValidId(id)) continue;
            var meta = ReadMeta(id);
            if (meta is not null) result.Add(ToInfo(id, meta));
        }
        return result;
    }

    public bool Delete(string id)
    {
        if (!IsValidId(id)) return false;
        var dir = Path.Combine(_root, id);
        if (!Directory.Exists(dir)) return false;
        lock (_imageLock) RemoveImage(id);
        lock (_animationLock) RemoveAnimation(id);

        // Rename first (retrying while a reader has a file open) so the asset disappears atomically even if a reader still holds a file open.
        var doomed = Path.Combine(_root, TempPrefix + "del-" + Guid.NewGuid().ToString("N"));
        for (var attempt = 0; ; attempt++)
        {
            try { Directory.Move(dir, doomed); break; }
            catch (DirectoryNotFoundException) { return false; }
            catch (IOException) when (attempt < 20) { Thread.Sleep(25); }   // a concurrent reader briefly holds a file open
        }
        TryDeleteDirectory(doomed);
        return true;
    }

    /// <summary>A new bitmap on every call; the caller owns and disposes it.</summary>
    public SKBitmap? GetImage(string assetId)
    {
        if (!IsValidId(assetId)) return null;
        lock (_imageLock)
            if (_images.TryGetValue(assetId, out var cached)) { TouchImage(assetId); return cached.Copy(); }

        var bmp = DecodeImage(assetId);
        if (bmp is null) return null;
        lock (_imageLock)
        {
            if (_images.TryGetValue(assetId, out var raced))
            {
                bmp.Dispose();
                TouchImage(assetId);
                return raced.Copy();
            }
            long bytes = bmp.ByteCount;
            if (bytes > _imageBudget) return bmp;
            while (_imageBytes + bytes > _imageBudget && _imageOrder.Count > 0) RemoveImage(_imageOrder[0]);
            _images[assetId] = bmp;
            _imageOrder.Add(assetId);
            _imageBytes += bytes;
            return bmp.Copy();
        }
    }

    /// <summary>
    /// The stored JPEG thumbnail (at most <see cref="ThumbnailMaxWidth"/> wide), or null when the asset is missing.
    /// Assets imported before thumbnails existed get one made from the still or the first frame only, stored for next
    /// time; nothing is added to the image or animation caches.
    /// </summary>
    public byte[]? GetThumbnail(string assetId)
    {
        if (!IsValidId(assetId)) return null;
        var dir = Path.Combine(_root, assetId);
        try
        {
            var path = Path.Combine(dir, ThumbName);
            if (File.Exists(path)) return File.ReadAllBytes(path);
            var source = Path.Combine(dir, "image.png");
            if (!File.Exists(source)) source = Path.Combine(dir, "frames", FrameName(0));
            if (!File.Exists(source)) return null;
            var thumb = MakeThumbnail(File.ReadAllBytes(source));
            if (thumb is null) return null;
            try { AtomicFile.Write(path, thumb); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }   // e.g. deleted meanwhile: serve it anyway
            return thumb;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    private SKBitmap? DecodeImage(string id)
    {
        try
        {
            var path = Path.Combine(_root, id, "image.png");
            return File.Exists(path) ? SKBitmap.Decode(File.ReadAllBytes(path)) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    /// <summary>JPEG of the encoded image scaled to at most <see cref="ThumbnailMaxWidth"/> wide, or null if it does not decode.</summary>
    private static byte[]? MakeThumbnail(byte[] encoded)
    {
        using var bmp = SKBitmap.Decode(encoded);
        if (bmp is null) return null;
        if (bmp.Width <= ThumbnailMaxWidth) return JpegEncoder.Encode(bmp).Data;
        var h = Math.Max(1, (int)Math.Round(bmp.Height * (double)ThumbnailMaxWidth / bmp.Width));
        using var small = bmp.Resize(new SKImageInfo(ThumbnailMaxWidth, h), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
        return small is null ? null : JpegEncoder.Encode(small).Data;
    }

    private void RemoveImage(string id)   // caller holds _imageLock
    {
        if (!_images.Remove(id, out var bmp)) return;
        _imageBytes -= bmp.ByteCount;
        _imageOrder.Remove(id);
        bmp.Dispose();   // safe: copies are only made under the lock
    }

    private void TouchImage(string id)   // caller holds _imageLock
    {
        _imageOrder.Remove(id);
        _imageOrder.Add(id);
    }

    /// <summary>Animations are immutable, so the cached instance is shared between display sessions.</summary>
    public Animation? GetAnimation(string assetId)
    {
        if (!IsValidId(assetId)) return null;
        lock (_animationLock)
            if (_animations.TryGetValue(assetId, out var hit)) { Touch(assetId); return hit.Animation; }

        var animation = LoadAnimation(assetId);
        if (animation is null) return null;
        var bytes = animation.JpegFrames.Sum(f => (long)f.Length);
        lock (_animationLock)
        {
            if (_animations.TryGetValue(assetId, out var raced)) { Touch(assetId); return raced.Animation; }
            if (bytes <= AnimationCacheBudgetBytes)
            {
                while (_animationBytes + bytes > AnimationCacheBudgetBytes && _animationOrder.Count > 0)
                    RemoveAnimation(_animationOrder[0]);
                _animations[assetId] = (animation, bytes);
                _animationOrder.Add(assetId);
                _animationBytes += bytes;
            }
        }
        return animation;
    }

    private Animation? LoadAnimation(string id)
    {
        var meta = ReadMeta(id);
        if (meta?.DelaysMs is null || meta.DelaysMs.Length != meta.FrameCount || meta.FrameCount < 1) return null;
        try
        {
            var frames = new byte[meta.FrameCount][];
            for (var i = 0; i < frames.Length; i++)
                frames[i] = File.ReadAllBytes(Path.Combine(_root, id, "frames", FrameName(i)));
            return new Animation(meta.Width, meta.Height, frames, meta.DelaysMs);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return null; }
    }

    private void RemoveAnimation(string id)   // caller holds _animationLock
    {
        if (!_animations.Remove(id, out var entry)) return;
        _animationBytes -= entry.Bytes;
        _animationOrder.Remove(id);
    }

    private void Touch(string id)   // caller holds _animationLock
    {
        _animationOrder.Remove(id);
        _animationOrder.Add(id);
    }

    private Meta? ReadMeta(string id)
    {
        try
        {
            return JsonSerializer.Deserialize<Meta>(File.ReadAllText(Path.Combine(_root, id, "meta.json")), GlanceJson.Options);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    private static AssetInfo ToInfo(string id, Meta m) => new(id, m.Kind, m.Width, m.Height, m.FrameCount);

    private static string FrameName(int i) => $"{i:D4}.jpg";

    private static bool IsValidId(string? id) => id is not null && IdPattern().IsMatch(id);

    [GeneratedRegex(@"^[0-9a-f]{32}\z")]
    private static partial Regex IdPattern();

    private static void TryDeleteDirectory(string path)
    {
        try { Directory.Delete(path, recursive: true); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
