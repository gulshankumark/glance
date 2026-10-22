using SkiaSharp;

namespace Glance.Core.Media;

public interface IAssetStore
{
    /// <summary>The decoded still image for an asset id, or null when the asset is missing. The caller owns the returned bitmap and must dispose it.</summary>
    SKBitmap? GetImage(string assetId);

    Animation? GetAnimation(string assetId);
}
