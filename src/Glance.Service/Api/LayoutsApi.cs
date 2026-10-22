using System.Text.Json;
using Glance.Core.Layouts;
using Glance.Core.Media;
using Glance.Core.Rendering;
using Glance.Core.Sensors;
using SkiaSharp;

namespace Glance.Service.Api;

public static class LayoutsApi
{
    private const int MaxBodyBytes = 2 * 1024 * 1024;

    public sealed record DuplicateBody(string? NewName);

    public static void MapLayoutsApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/layouts", (LayoutStore store) =>
        {
            var errors = store.Errors;
            var names = store.Names.Concat(errors.Keys).Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
            return names.Select(n =>
            {
                var l = store.Get(n);
                return new { Name = n, Width = l?.Width ?? 0, Height = l?.Height ?? 0, Error = errors.GetValueOrDefault(n) };
            });
        });

        app.MapGet("/api/layouts/{name}", (string name, LayoutStore store) =>
        {
            if (!LayoutValidator.IsValidName(name)) return InvalidName();
            return store.Get(name) is { } l
                ? Results.Content(JsonSerializer.Serialize(l, GlanceJson.Options), "application/json")
                : Results.NotFound();
        });

        app.MapPut("/api/layouts/{name}", async (string name, HttpRequest request, LayoutStore store, CancellationToken ct) =>
        {
            if (!LayoutValidator.IsValidName(name)) return InvalidName();
            var (layout, error) = await ReadLayoutAsync(request, ct);
            if (layout is null) return Results.BadRequest(new { errors = new[] { error } });
            if (!string.Equals(layout.Name, name, StringComparison.Ordinal))
                return Results.BadRequest(new { errors = new[] { $"layout name '{layout.Name}' does not match '{name}'" } });
            var errors = LayoutValidator.Validate(layout);
            if (errors.Count > 0) return Results.BadRequest(new { errors });
            try { store.Save(layout); }
            catch (ArgumentException ex) { return Results.BadRequest(new { errors = new[] { ex.Message } }); }
            return Results.Ok();
        });

        app.MapDelete("/api/layouts/{name}", (string name, LayoutStore store) =>
        {
            if (!LayoutValidator.IsValidName(name)) return InvalidName();
            return store.Delete(name) ? Results.NoContent() : Results.NotFound();
        });

        app.MapPost("/api/layouts/{name}/duplicate", (string name, DuplicateBody body, LayoutStore store) =>
        {
            if (!LayoutValidator.IsValidName(name)) return InvalidName();
            if (body.NewName is null || !LayoutValidator.IsValidName(body.NewName)) return InvalidName();
            if (store.Get(name) is not { } source) return Results.NotFound();
            if (store.Get(body.NewName) is not null) return Results.Conflict(new { errors = new[] { "a layout with that name already exists" } });
            var copy = GlanceJson.Deserialize<Layout>(JsonSerializer.Serialize(source, GlanceJson.Options))!;
            copy.Name = body.NewName;
            try { store.Save(copy); }
            catch (ArgumentException ex) { return Results.BadRequest(new { errors = new[] { ex.Message } }); }
            return Results.Ok();
        });

        // Draws the layout as sent (layout orientation, layout size) for the editor; writes nothing and sends nothing
        // to a screen. A "device" query value is accepted but does not change the output.
        app.MapPost("/api/render", async (HttpRequest request, SensorHub hub, SettingsStore settings, AssetStore assets,
            OverlayRenderer renderer, FontProvider fonts, TimeProvider time, UiInterest ui, CancellationToken ct) =>
        {
            ui.Touch();
            var (layout, error) = await ReadLayoutAsync(request, ct);
            if (layout is null) return Results.BadRequest(new { errors = new[] { error } });
            var errors = LayoutValidator.Validate(layout);
            if (errors.Count > 0) return Results.BadRequest(new { errors });

            using var frame = new SKBitmap(layout.Width, layout.Height);
            using (var canvas = new SKCanvas(frame))
            {
                DrawBackground(canvas, layout, assets);
                using var overlay = renderer.Render(layout, new RenderContext(hub, settings.Current, time.GetUtcNow(), assets, fonts));
                canvas.DrawBitmap(overlay, 0, 0);
            }
            return Results.File(JpegEncoder.Encode(frame).Data, "image/jpeg");
        });
    }

    private static IResult InvalidName() => Results.BadRequest(new { errors = new[] { "invalid layout name" } });

    private static async Task<(Layout?, string)> ReadLayoutAsync(HttpRequest request, CancellationToken ct)
    {
        var text = await ApiJson.ReadBodyAsync(request, MaxBodyBytes, ct);
        if (text is null) return (null, "layout is too large");
        try
        {
            var layout = GlanceJson.Deserialize<Layout>(text);
            return layout is null ? (null, "layout is empty") : (layout, "");
        }
        catch (Exception ex) when (ex is JsonException or NotSupportedException or InvalidOperationException)
        {
            return (null, "invalid layout JSON: " + ex.Message);
        }
    }

    private static void DrawBackground(SKCanvas canvas, Layout layout, AssetStore assets)
    {
        canvas.Clear(SKColors.Black);
        SKBitmap? bmp = null;
        switch (layout.Background)
        {
            case ColorBackground c when SKColor.TryParse(c.Color, out var color):
                canvas.Clear(color);
                return;
            case ImageBackground i:
                bmp = assets.GetImage(i.AssetId);
                break;
            case AnimationBackground a when assets.GetAnimation(a.AssetId) is { JpegFrames.Count: > 0 } anim:
                bmp = SKBitmap.Decode(anim.JpegFrames[0]);
                break;
        }
        if (bmp is null) return;
        using (bmp)
        {
            using var image = SKImage.FromBitmap(bmp);
            canvas.DrawImage(image, SKRect.Create(layout.Width, layout.Height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        }
    }
}
