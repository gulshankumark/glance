using Glance.Core.Layouts;
using Glance.Core.Rendering;
using Glance.Core.Sensors;

namespace Glance.Service.Api;

public static class SensorsApi
{
    private const int MaxAliasLength = 100;

    public sealed record AliasBody(string? Id, string? Alias);
    public sealed record RoleBody(string? SensorId);

    public static void MapSensorsApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/sensors", (SensorHub hub, SettingsStore settings, ISensorSource source, UiInterest ui) =>
        {
            ui.Touch();
            var current = hub.Current.Values;
            var aliases = settings.Current.Aliases;
            var sensors = hub.Sensors.Select(s => new
            {
                s.Id, s.Hardware, s.HardwareKind, s.Name, s.Kind,
                Unit = ValueFormatter.Unit(s.Kind, TemperatureUnit.Celsius) is { Length: > 0 } u ? u : null,
                Value = current.TryGetValue(s.Id, out var v) ? ApiJson.Finite(v) : null,
                Alias = aliases.GetValueOrDefault(s.Id),
            });
            var roles = Roles.All.ToDictionary(r => r, r => hub.Resolve(new SensorRef(true, r)));
            var diagnostics = source.Diagnostics.Select(d => new { d.Code, d.Message });
            var pins = settings.Current.RolePins.ToDictionary(kv => kv.Key, kv => kv.Value);
            return new { sensors, roles, pins, diagnostics };
        });

        app.MapPut("/api/sensors/alias", (AliasBody body, SensorHub hub, SettingsStore settings) =>
        {
            var id = body.Id ?? "";
            if (hub.Info(id) is null) return Results.NotFound(new { error = "unknown sensor" });
            var alias = body.Alias?.Trim();
            if (alias is { Length: > MaxAliasLength }) return Results.BadRequest(new { error = $"alias must be at most {MaxAliasLength} characters" });
            settings.Update(s =>
            {
                if (string.IsNullOrEmpty(alias)) s.Aliases.Remove(id);
                else s.Aliases[id] = alias;
            });
            return Results.NoContent();
        });

        app.MapPut("/api/roles/{role}", (string role, RoleBody body, SensorHub hub, SettingsStore settings) =>
        {
            if (!Roles.All.Contains(role)) return Results.NotFound(new { error = "unknown role" });
            if (!string.IsNullOrEmpty(body.SensorId) && hub.Info(body.SensorId) is null)
                return Results.BadRequest(new { error = "unknown sensor" });
            settings.Update(s =>
            {
                if (string.IsNullOrEmpty(body.SensorId)) s.RolePins.Remove(role);
                else s.RolePins[role] = body.SensorId;
            });
            return Results.NoContent();
        });
    }
}
