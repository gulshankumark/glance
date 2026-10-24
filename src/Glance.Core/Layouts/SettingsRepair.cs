using System.Text.Json;
using System.Text.Json.Nodes;

namespace Glance.Core.Layouts;

/// <summary>
/// Reads settings.json field by field: a field that cannot be deserialized or fails validation falls back to its
/// default and is reported, while every other field is kept. Generic over <see cref="AppSettings"/> properties, so
/// new fields are covered without changes; only the dictionary-valued ones are repaired per entry.
/// </summary>
public static class SettingsRepair
{
    private static readonly string[] Dictionaries = ["devices", "aliases", "rolePins"];

    /// <summary>Throws <see cref="JsonException"/> only when the text is not a JSON object.</summary>
    public static (AppSettings Settings, IReadOnlyList<string> Repairs) Load(string json)
    {
        if (JsonNode.Parse(json) is not JsonObject root) throw new JsonException("settings file is not a JSON object");
        var repairs = new List<string>();
        var accepted = new JsonObject();

        foreach (var (name, value) in root)
        {
            if (Dictionaries.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
            var candidate = (JsonObject)accepted.DeepClone();
            candidate[name] = value?.DeepClone();
            var problem = Problem<AppSettings>(candidate, s => s.Validate());
            if (problem is null) accepted[name] = value?.DeepClone();
            else repairs.Add($"{name}: {problem} → default");
        }

        foreach (var (name, value) in root)
        {
            if (!Dictionaries.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
            if (value is null) continue;   // null means "empty"
            if (value is not JsonObject entries) { repairs.Add($"{name}: not an object → default"); continue; }
            accepted[name] = name.Equals("devices", StringComparison.OrdinalIgnoreCase)
                ? RepairDevices(entries, repairs)
                : RepairStrings(name, entries, repairs);
        }

        var settings = accepted.Deserialize<AppSettings>(GlanceJson.Options) ?? new AppSettings();
        return (settings, repairs);
    }

    private static JsonObject RepairDevices(JsonObject entries, List<string> repairs)
    {
        var result = new JsonObject();
        foreach (var (key, entry) in entries)
        {
            if (entry is not JsonObject fields) { repairs.Add($"devices.{key}: not an object → dropped"); continue; }
            var good = new JsonObject();
            foreach (var (field, value) in fields)
            {
                var candidate = (JsonObject)good.DeepClone();
                candidate[field] = value?.DeepClone();
                var problem = Problem<DeviceSettings>(candidate, d => new AppSettings { Devices = { [key] = d } }.Validate());
                if (problem is null) good[field] = value?.DeepClone();
                else repairs.Add($"devices.{key}.{field}: {problem} → default");
            }
            result[key] = good;
        }
        return result;
    }

    private static JsonObject RepairStrings(string name, JsonObject entries, List<string> repairs)
    {
        var result = new JsonObject();
        foreach (var (key, value) in entries)
        {
            if (value is JsonValue v && v.TryGetValue<string>(out _)) result[key] = value.DeepClone();
            else repairs.Add($"{name}.{key}: not a string → dropped");
        }
        return result;
    }

    /// <summary>Null when <paramref name="candidate"/> deserializes and validates; otherwise the reason.</summary>
    private static string? Problem<T>(JsonObject candidate, Func<T, IReadOnlyList<string>> validate)
    {
        try
        {
            var value = candidate.Deserialize<T>(GlanceJson.Options);
            if (value is null) return "invalid value";
            var errors = validate(value);
            return errors.Count == 0 ? null : string.Join(", ", errors);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException)
        {
            return "invalid value";
        }
    }
}
