using Glance.Core.Layouts;
using Glance.Core.Sensors;
using SkiaSharp;

namespace Glance.Core.Rendering.Items;

public static class HistoryGraphRenderer
{
    private const float StrokeWidth = 2;
    private const byte AreaAlpha = 0x40;
    private const int GridLines = 4;

    public static void Draw(SKCanvas canvas, HistoryGraphItem g, RenderContext ctx) =>
        DrawGraph(canvas, g, g.WindowSeconds, g.Area, g.Grid, ctx);

    /// <summary>Shared by history graph and sparkline: oldest sample at the left edge, newest at the right; null/NaN/Infinity samples are gaps.</summary>
    internal static void DrawGraph(SKCanvas canvas, GaugeItem g, int windowSeconds, bool area, bool grid, RenderContext ctx)
    {
        var st = GaugeState.Resolve(g, ctx);
        var id = ctx.Sensors.Resolve(SensorRef.Parse(g.Sensor));
        var history = id is null ? Array.Empty<double?>() : ctx.Sensors.History(id, windowSeconds);
        var rect = GaugeMath.Box(g);

        canvas.Save();
        canvas.ClipRect(rect);
        if (grid)
        {
            using var gridPaint = new SKPaint { Color = st.Track, StrokeWidth = 1, Style = SKPaintStyle.Stroke };
            for (int i = 1; i <= GridLines; i++)
            {
                var y = MathF.Floor(rect.Top + rect.Height * i / (GridLines + 1)) + 0.5f;
                canvas.DrawLine(rect.Left, y, rect.Right, y, gridPaint);
            }
        }

        using var linePaint = new SKPaint
        {
            IsAntialias = true, Color = st.Color, StrokeWidth = StrokeWidth,
            Style = SKPaintStyle.Stroke, StrokeJoin = SKStrokeJoin.Round, StrokeCap = SKStrokeCap.Round,
        };
        using var fillPaint = new SKPaint { IsAntialias = true, Color = st.Color.WithAlpha(AreaAlpha), Style = SKPaintStyle.Fill };

        var n = history.Count;
        var step = n > 1 ? (rect.Width - 1) / (n - 1) : 0;
        var run = new List<SKPoint>();
        for (int i = 0; i <= n; i++)
        {
            if (i < n && history[i] is { } v && double.IsFinite(v))
            {
                var y = rect.Bottom - (float)GaugeMath.Fraction(v, st.Min, st.Max) * rect.Height;
                run.Add(new SKPoint(rect.Left + i * step, y));
                continue;
            }
            Flush(canvas, run, rect.Bottom, area, linePaint, fillPaint);
            run.Clear();
        }
        canvas.Restore();
    }

    private static void Flush(SKCanvas canvas, List<SKPoint> run, float bottom, bool area, SKPaint line, SKPaint fill)
    {
        if (run.Count == 0) return;
        if (run.Count == 1)
        {
            canvas.DrawPoint(run[0], line);
            return;
        }
        if (area)
        {
            using var poly = new SKPath();
            poly.MoveTo(run[0].X, bottom);
            foreach (var p in run) poly.LineTo(p);
            poly.LineTo(run[^1].X, bottom);
            poly.Close();
            canvas.DrawPath(poly, fill);
        }
        using var path = new SKPath();
        path.MoveTo(run[0]);
        for (int i = 1; i < run.Count; i++) path.LineTo(run[i]);
        canvas.DrawPath(path, line);
    }
}
