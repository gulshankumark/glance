using System.Collections.Concurrent;
using System.Reflection;
using Glance.Core.Layouts;
using SkiaSharp;

namespace Glance.Core.Rendering;

public sealed class FontProvider
{
    private readonly ConcurrentDictionary<(string Family, int Weight), SKTypeface> _cache = new();
    private readonly Lazy<SKTypeface> _regular = new(() => LoadEmbedded("Inter-Regular.ttf"));
    private readonly Lazy<SKTypeface> _bold = new(() => LoadEmbedded("Inter-Bold.ttf"));

    /// <summary>Bundled Inter by default; the bundled JetBrains Mono by name, whatever is installed; a named system family when installed; Inter when it is not.</summary>
    public SKTypeface Get(FontSpec? spec)
    {
        var f = LayoutLimits.SafeFont(spec);
        return _cache.GetOrAdd((f.Family.ToLowerInvariant(), f.Weight), _ => Resolve(f));
    }

    private readonly Lazy<SKTypeface> _monoRegular = new(() => LoadEmbedded("JetBrainsMono-Regular.ttf"));
    private readonly Lazy<SKTypeface> _monoBold = new(() => LoadEmbedded("JetBrainsMono-Bold.ttf"));

    public const string MonoFamily = "JetBrains Mono";

    private SKTypeface Inter(int weight) => weight >= 600 ? _bold.Value : _regular.Value;

    private SKTypeface Resolve(FontSpec f)
    {
        if (string.IsNullOrWhiteSpace(f.Family) || f.Family.Equals("Inter", StringComparison.OrdinalIgnoreCase))
            return Inter(f.Weight);
        if (f.Family.Equals(MonoFamily, StringComparison.OrdinalIgnoreCase))
            return f.Weight >= 600 ? _monoBold.Value : _monoRegular.Value;
        var style = new SKFontStyle(f.Weight, (int)SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        return SKFontManager.Default.MatchFamily(f.Family, style) ?? Inter(f.Weight);
    }

    private static SKTypeface LoadEmbedded(string name)
    {
        using var s = typeof(FontProvider).Assembly.GetManifestResourceStream(name)
                      ?? throw new InvalidOperationException($"Embedded font {name} missing");
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return SKTypeface.FromData(SKData.CreateCopy(ms.ToArray()))
               ?? throw new InvalidOperationException($"Embedded font {name} is not loadable");
    }
}
