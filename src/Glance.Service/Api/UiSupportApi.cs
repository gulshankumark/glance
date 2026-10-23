using Glance.Core.Layouts;
using Glance.Core.Media;
using Glance.Core.Rendering;
using Glance.Core.Sensors;
using SkiaSharp;

namespace Glance.Service.Api;

/// <summary>Lookups the web UI's layout editor needs: item bounds, font families and clock time zones.</summary>
public static class UiSupportApi
{
    public const string DefaultFont = "Inter";

    public sealed record Bounds(float X, float Y, float Width, float Height);

    public sealed record TimeZoneView(string Id, string Label);

    public static void MapUiSupportApi(this IEndpointRouteBuilder app)
    {
        // Measures the layout as sent, like /api/render draws it; writes nothing and sends nothing to a screen.
        app.MapPost("/api/render/bounds", async (HttpRequest request, SensorHub hub, SettingsStore settings, AssetStore assets,
            OverlayRenderer renderer, FontProvider fonts, TimeProvider time, UiInterest ui, CancellationToken ct) =>
        {
            ui.Touch();
            var (layout, invalid) = await LayoutsApi.ReadValidLayoutAsync(request, ct);
            if (layout is null) return invalid!;
            var measured = renderer.MeasureItems(layout, new RenderContext(hub, settings.Current, time.GetUtcNow(), assets, fonts));
            return Results.Ok(measured.ToDictionary(kv => kv.Key, kv => ToBounds(kv.Value)));
        });

        app.MapGet("/api/fonts", () => new { Default = DefaultFont, Families = FontFamilies() });

        app.MapGet("/api/timezones", TimeZones);
    }

    private static Bounds ToBounds(SKRect r) => new(r.Left, r.Top, r.Width, r.Height);

    /// <summary>The bundled default first, then the installed families sorted case-insensitively without duplicates.</summary>
    private static IReadOnlyList<string> FontFamilies() =>
        SKFontManager.Default.FontFamilies
            .Where(f => !string.IsNullOrWhiteSpace(f) && !f.Equals(DefaultFont, StringComparison.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .Prepend(DefaultFont)
            .ToList();

    /// <summary>"local" first, then each system zone once under its IANA id, sorted by base UTC offset then label.</summary>
    private static IReadOnlyList<TimeZoneView> TimeZones() =>
        TimeZoneInfo.GetSystemTimeZones()
            .Select(z => (Zone: z, Iana: z.HasIanaId ? z.Id : TimeZoneInfo.TryConvertWindowsIdToIanaId(z.Id, out var iana) ? iana : null))
            .Where(x => x.Iana is not null)
            .OrderBy(x => x.Zone.BaseUtcOffset).ThenBy(x => x.Zone.DisplayName, StringComparer.OrdinalIgnoreCase)
            .DistinctBy(x => x.Iana)
            .Select(x => new TimeZoneView(x.Iana!, x.Zone.DisplayName))
            .Prepend(new TimeZoneView("local", "Local time"))
            .ToList();
}
