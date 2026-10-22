using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Glance.Core.Layouts;

public static class GlanceJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>
    /// Deserializes with <see cref="Options"/>. .NET 8 requires the polymorphic "type" discriminator to be the
    /// first property, but hand-edited and API-supplied JSON may order properties freely, so "type" is moved first
    /// in the background and every object in items[] before deserializing.
    /// </summary>
    public static T? Deserialize<T>(string json)
    {
        var node = JsonNode.Parse(json);
        if (node is JsonObject root)
        {
            if (root["background"] is JsonObject bg) TypeFirst(bg);
            if (root["items"] is JsonArray items)
                foreach (var i in items) if (i is JsonObject o) TypeFirst(o);
        }
        return node.Deserialize<T>(Options);
    }

    private static void TypeFirst(JsonObject o)
    {
        if (!o.TryGetPropertyValue("type", out var type) || type is null || o.First().Key == "type") return;
        var rest = o.ToList();
        o.Clear();
        o.Add("type", type);
        foreach (var (k, v) in rest) if (k != "type") o.Add(k, v);
    }
}

public static class AtomicFile
{
    /// <summary>Writes to a temp file in the same directory, then swaps it into place.</summary>
    public static void Write(string path, string text) => Write(path, tmp => File.WriteAllText(tmp, text));

    /// <inheritdoc cref="Write(string, string)"/>
    public static void Write(string path, byte[] bytes) => Write(path, tmp => File.WriteAllBytes(tmp, bytes));

    private static void Write(string path, Action<string> writeTemp)
    {
        var tmp = Path.Combine(Path.GetDirectoryName(path)!, $".{Guid.NewGuid():N}.tmp");
        try
        {
            writeTemp(tmp);
            // A reader (config reload, antivirus) can hold the target open for a moment; retry briefly.
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    if (File.Exists(path)) File.Replace(tmp, path, null);
                    else File.Move(tmp, path);
                    return;
                }
                catch (IOException) when (attempt < 20) { Thread.Sleep(25); }
            }
        }
        finally
        {
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch (IOException) { }
        }
    }
}
