using System.Text.Json;
using Glance.Core.Layouts;

namespace Glance.Service.Api;

public static class SettingsApi
{
    /// <param name="MaxOverlayAnimationFps">Absent: unchanged; null: cleared; a whole number: the new cap.</param>
    public sealed record SettingsPatch(TemperatureUnit? Unit, bool? Clock24h, JsonElement MaxOverlayAnimationFps);

    public static void MapSettingsApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/settings", (SettingsStore store) => View(store.Current));

        app.MapPut("/api/settings", (SettingsPatch body, SettingsStore store) =>
        {
            if (body.Unit is { } u && !Enum.IsDefined(u)) return Results.BadRequest(new { error = "unknown unit" });
            var fps = body.MaxOverlayAnimationFps;
            int? cap = null;
            if (fps.ValueKind == JsonValueKind.Number && fps.TryGetInt32(out var n)) cap = n;
            else if (fps.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))
                return Results.BadRequest(new { error = "maxOverlayAnimationFps must be a whole number or null" });
            try
            {
                store.Update(s =>
                {
                    if (body.Unit is { } unit) s.Unit = unit;
                    if (body.Clock24h is { } c) s.Clock24h = c;
                    if (fps.ValueKind != JsonValueKind.Undefined) s.MaxOverlayAnimationFps = cap;
                });
            }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            return Results.Ok(View(store.Current));
        });
    }

    /// <summary>The public view of the settings: never the password hash or certificate password.</summary>
    private static object View(AppSettings s) => new
    {
        s.Unit, s.Clock24h, s.MaxOverlayAnimationFps, s.Port, s.NetworkAccess, s.Https,
        HasPassword = !string.IsNullOrEmpty(s.PasswordHash),
    };
}
