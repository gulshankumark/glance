using Glance.Core.Layouts;
using SkiaSharp;

namespace Glance.Core.Rendering.Items;

public static class ImageItemRenderer
{
    /// <summary>Draws the asset scaled to the item box; a missing asset is skipped.</summary>
    public static void Draw(SKCanvas canvas, ImageItem item, RenderContext ctx)
    {
        using var bmp = ctx.Assets.GetImage(item.AssetId);
        if (bmp is null) return;
        using var image = SKImage.FromBitmap(bmp);
        canvas.DrawImage(image, Box(item, bmp), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
    }

    /// <summary>The item box; the asset is only read when the item leaves its width or height to the asset (0 when missing).</summary>
    public static SKRect Measure(ImageItem item, RenderContext ctx)
    {
        if (item.Width > 0 && item.Height > 0) return Box(item, null);
        using var bmp = ctx.Assets.GetImage(item.AssetId);
        return Box(item, bmp);
    }

    /// <summary>Declared width and height, each falling back to the asset's own size when not positive.</summary>
    private static SKRect Box(ImageItem item, SKBitmap? bmp)
    {
        var w = item.Width > 0 ? item.Width : bmp?.Width ?? 0;
        var h = item.Height > 0 ? item.Height : bmp?.Height ?? 0;
        return new SKRect(item.X, item.Y, item.X + w, item.Y + h);
    }
}
