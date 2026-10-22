using Glance.Core.Layouts;
using Glance.Core.Rendering;
using Glance.Core.Sessions;

namespace Glance.Service.Api;

public static class DevicesApi
{
    public sealed record DevicePatch(int? Rotation, string? ActiveLayout);

    public sealed record DeviceView(string Key, string DriverId, string Name, DeviceStatus Status, int Width, int Height,
        string ModelCode, string Serial, string? Message, DateTimeOffset? NextRetry,
        int Rotation, string? ActiveLayout, bool LayoutMismatch);

    public static void MapDevicesApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/devices", (DeviceManager devices, SettingsStore settings, LayoutStore layouts) =>
            Payload(devices, settings, layouts));

        app.MapPut("/api/devices/{key}", (string key, DevicePatch body, DeviceManager devices, SettingsStore settings, LayoutStore layouts) =>
        {
            if (devices.Devices.All(d => d.Key != key)) return Results.NotFound(new { error = "unknown device" });
            if (body.Rotation is { } r && r is not (0 or 90 or 180 or 270))
                return Results.BadRequest(new { error = "rotation must be 0, 90, 180 or 270" });
            var layoutName = body.ActiveLayout is null ? null : layouts.Get(body.ActiveLayout)?.Name;
            if (body.ActiveLayout is not null && layoutName is null)
                return Results.BadRequest(new { error = $"layout '{body.ActiveLayout}' does not exist" });
            try
            {
                settings.Update(s =>
                {
                    if (!s.Devices.TryGetValue(key, out var d)) s.Devices[key] = d = new DeviceSettings();
                    if (body.Rotation is { } rot) d.Rotation = rot;
                    if (layoutName is not null) d.ActiveLayout = layoutName;
                });
            }
            catch (ArgumentException ex) { return Results.BadRequest(new { error = ex.Message }); }
            return Results.Ok(Payload(devices, settings, layouts).FirstOrDefault(d => d.Key == key));
        });
    }

    public static IReadOnlyList<DeviceView> Payload(DeviceManager devices, SettingsStore settings, LayoutStore layouts)
    {
        var all = settings.Current.Devices;
        return devices.Devices.Select(d =>
        {
            all.TryGetValue(d.Key, out var s);
            var rotation = s?.Rotation ?? 0;
            var active = s?.ActiveLayout;
            var layout = active is null ? null : layouts.Get(active);
            var mismatch = layout is not null && (layout.Width, layout.Height) != Orientation.Canvas(d.Width, d.Height, rotation);
            return new DeviceView(d.Key, d.DriverId, d.Name, d.Status, d.Width, d.Height, d.ModelCode, d.Serial,
                d.Message, d.NextRetry, rotation, active, mismatch);
        }).ToList();
    }
}
