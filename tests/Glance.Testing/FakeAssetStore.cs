using Glance.Core.Media;
using SkiaSharp;

namespace Glance.Testing;

/// <summary>In-memory assets: images are stored encoded and decoded per call, so callers own what they get.</summary>
public sealed class FakeAssetStore : IAssetStore
{
    private readonly Dictionary<string, byte[]> _images = new();
    private readonly Dictionary<string, Animation> _animations = new();

    public void SetImage(string id, byte[] encoded) { lock (_images) _images[id] = encoded; }
    public void SetAnimation(string id, Animation a) { lock (_animations) _animations[id] = a; }

    /// <summary>Number of <see cref="GetImage"/> calls so far.</summary>
    public int ImageRequests { get { lock (_images) return _imageRequests; } }
    private int _imageRequests;

    public SKBitmap? GetImage(string assetId)
    {
        lock (_images)
        {
            _imageRequests++;
            return _images.TryGetValue(assetId, out var data) ? SKBitmap.Decode(data) : null;
        }
    }

    public Animation? GetAnimation(string assetId)
    {
        lock (_animations) return _animations.GetValueOrDefault(assetId);
    }
}
