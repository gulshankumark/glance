namespace Glance.Core.Sessions;

public enum DeviceStatus { Connected, Unsupported, Retrying }

public record DeviceState(string Key, string DriverId, string Name, DeviceStatus Status, int Width, int Height,
    string ModelCode, string Serial, string? Message, DateTimeOffset? NextRetry);
