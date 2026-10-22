using Glance.Core.Layouts;
using Glance.Core.Rendering.Items;
using Glance.Core.Sensors;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SkiaSharp;

namespace Glance.Core.Rendering;

public class OverlayRenderer
{
    private readonly ILogger _log;

    public OverlayRenderer(ILogger<OverlayRenderer>? log = null) => _log = log ?? NullLogger<OverlayRenderer>.Instance;

    /// <summary>Transparent bitmap of the layout size with every item drawn; an item that throws is logged and skipped.</summary>
    public virtual SKBitmap Render(Layout layout, RenderContext ctx)
    {
        var bmp = new SKBitmap(layout.Width, layout.Height);
        bmp.Erase(SKColors.Transparent);
        using var canvas = new SKCanvas(bmp);
        foreach (var item in layout.Items)
        {
            var saved = canvas.Save();
            try { DrawItem(canvas, item, ctx); }
            catch (Exception ex) { _log.LogWarning(ex, "Item '{Id}' ({Type}) failed to render and was skipped", item.Id, item.GetType().Name); }
            finally { canvas.RestoreToCount(saved); }
        }
        return bmp;
    }

    private static void DrawItem(SKCanvas canvas, LayoutItem item, RenderContext ctx)
    {
        switch (item)
        {
            case TextItem t: TextItems.Draw(canvas, t, ctx); break;
            case SensorItem s: TextItems.Draw(canvas, s, ctx); break;
            case ClockItem c: TextItems.Draw(canvas, c, ctx); break;
            case ImageItem i: ImageItemRenderer.Draw(canvas, i, ctx); break;
            case BarItem b: BarRenderer.Draw(canvas, b, ctx); break;
            case SegmentedBarItem sb: SegmentedBarRenderer.Draw(canvas, sb, ctx); break;
            case ArcGaugeItem a: ArcGaugeRenderer.Draw(canvas, a, ctx); break;
            case DialItem d: DialRenderer.Draw(canvas, d, ctx); break;
            case HistoryGraphItem h: HistoryGraphRenderer.Draw(canvas, h, ctx); break;
            case SparklineItem p: SparklineRenderer.Draw(canvas, p, ctx); break;
        }
    }

    /// <summary>Resolved ids of every sensor the layout reads (roles and pins applied; unresolved refs omitted).</summary>
    public IEnumerable<string> ReferencedSensorIds(Layout l, SensorHub hub) => HistoryNeeds(l, hub).Keys;

    /// <summary>Referenced sensor id to the history seconds its items need (0 = current value only).</summary>
    public IReadOnlyDictionary<string, int> HistoryNeeds(Layout l, SensorHub hub)
    {
        var needs = new Dictionary<string, int>();
        foreach (var item in l.Items)
        {
            var sensor = item switch { SensorItem s => s.Sensor, GaugeItem g => g.Sensor, _ => null };
            if (sensor is null) continue;
            var id = hub.Resolve(SensorRef.Parse(sensor));
            if (id is null) continue;
            var seconds = item switch { HistoryGraphItem h => h.WindowSeconds, SparklineItem p => p.WindowSeconds,
                // auto-ranged gauges look back over recent history for their scale
                GaugeItem g when g.Min is null || g.Max is null => 300, _ => 0 };
            needs[id] = Math.Max(needs.GetValueOrDefault(id), seconds);
        }
        return needs;
    }
}
