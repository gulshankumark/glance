using Glance.Core.Layouts;
using Glance.Core.Rendering.Items;
using SkiaSharp;

namespace Glance.Core.Rendering;

public static class FallbackScreen
{
    /// <summary>
    /// First-run text pointing at the web UI. HTTPS applies only with network access on (loopback is otherwise plain
    /// HTTP, see the Kestrel endpoints), so the scheme follows both settings.
    /// </summary>
    public static string RunningMessage(AppSettings s) =>
        $"Glance is running — open {(s.NetworkAccess && s.Https ? "https" : "http")}://localhost:{s.Port}";

    /// <summary>Black screen with the message centred on one line, shrunk to fit the width.</summary>
    public static SKBitmap Render(int w, int h, string message, FontProvider fonts)
    {
        var bmp = new SKBitmap(w, h);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(SKColors.Black);
        var typeface = fonts.Get(new FontSpec());
        var size = Math.Min(h / 8f, 48f);
        using (var font = new SKFont(typeface, size))
        {
            var width = font.MeasureText(message);
            if (width > w * 0.9f) size = size * w * 0.9f / width;
        }
        TextItems.DrawText(canvas, fonts, message, w / 2f, h / 2f - size / 2, new FontSpec(Size: size), "#FFFFFF", TextAlign.Center);
        return bmp;
    }
}
