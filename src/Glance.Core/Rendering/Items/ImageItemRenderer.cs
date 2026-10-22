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
        var w = item.Width > 0 ? item.Width : bmp.Width;
        var h = item.Height > 0 ? item.Height : bmp.Height;
        using var image = SKImage.FromBitmap(bmp);
        canvas.DrawImage(image, new SKRect(item.X, item.Y, item.X + w, item.Y + h),
            new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear));
    }
}
