namespace Glance.Core.Layouts;

public enum TemperatureUnit { Celsius, Fahrenheit }

public sealed class DeviceSettings
{
    public string? ActiveLayout { get; set; }
    public int Rotation { get; set; }
}

public sealed class AppSettings
{
    public const int DefaultPort = 8735;

    public int Port { get; set; } = DefaultPort;
    public bool NetworkAccess { get; set; }
    public string? PasswordHash { get; set; }
    public bool Https { get; set; }
    public string? HttpsPfxPassword { get; set; }
    public TemperatureUnit Unit { get; set; } = TemperatureUnit.Celsius;
    public bool Clock24h { get; set; } = true;

    /// <summary>
    /// Most frames per second sent for an animated background with overlay items (each one decoded, composed and
    /// encoded); frames in between are skipped, the animation keeps its speed. Null: every frame at its own timing.
    /// </summary>
    public int? MaxOverlayAnimationFps { get; set; }
    public Dictionary<string, DeviceSettings> Devices { get; set; } = new();
    public Dictionary<string, string> Aliases { get; set; } = new();
    public Dictionary<string, string> RolePins { get; set; } = new();

    public IReadOnlyList<string> Validate()
    {
        var e = new List<string>();
        if (Port is < 1 or > 65535) e.Add("port must be 1..65535");
        if (MaxOverlayAnimationFps is < 1 or > 60) e.Add("maxOverlayAnimationFps must be 1..60 or null");
        if (Devices is null || Aliases is null || RolePins is null)
        {
            e.Add("devices, aliases and rolePins must not be null");
            return e;
        }
        foreach (var (key, d) in Devices)
        {
            if (d is null) e.Add($"device '{key}' is null");
            else if (d.Rotation is not (0 or 90 or 180 or 270)) e.Add($"device '{key}': rotation must be 0, 90, 180 or 270");
        }
        return e;
    }
}
