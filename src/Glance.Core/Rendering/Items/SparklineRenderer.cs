using Glance.Core.Layouts;
using SkiaSharp;

namespace Glance.Core.Rendering.Items;

public static class SparklineRenderer
{
    public static void Draw(SKCanvas canvas, SparklineItem s, RenderContext ctx) =>
        HistoryGraphRenderer.DrawGraph(canvas, s, s.WindowSeconds, area: false, grid: false, ctx);
}
