using Glance.Core.Layouts;
using Glance.Core.Rendering;
using SkiaSharp;

namespace Glance.Testing;

public sealed class CountingRenderer : OverlayRenderer
{
    private int _count;

    public int RenderCount => Volatile.Read(ref _count);

    /// <summary>When set, every Render call throws it.</summary>
    public Exception? Throw { get; set; }

    public override SKBitmap Render(Layout layout, RenderContext ctx)
    {
        Interlocked.Increment(ref _count);
        if (Throw is { } ex) throw ex;
        return base.Render(layout, ctx);
    }
}
