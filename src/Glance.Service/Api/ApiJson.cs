using System.Text.Json;
using System.Text.Json.Serialization;

namespace Glance.Service.Api;

/// <summary>JSON conventions for API responses: camelCase, enums as camelCase strings.</summary>
public static class ApiJson
{
    public static void Configure(JsonSerializerOptions o)
    {
        o.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        o.PropertyNameCaseInsensitive = true;
        if (!o.Converters.OfType<JsonStringEnumConverter>().Any())
            o.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    }

    public static readonly JsonSerializerOptions Options = Build();

    private static JsonSerializerOptions Build()
    {
        var o = new JsonSerializerOptions();
        Configure(o);
        return o;
    }

    /// <summary>NaN and infinity cannot be written as JSON numbers; report them as missing.</summary>
    public static double? Finite(double? v) => v is { } d && double.IsFinite(d) ? d : null;

    /// <summary>Reads at most <paramref name="max"/> bytes of the body as UTF-8 text; null when it is larger.</summary>
    public static async Task<string?> ReadBodyAsync(HttpRequest request, int max, CancellationToken ct)
    {
        using var ms = new MemoryStream();
        var buffer = new byte[16 * 1024];
        int n;
        while ((n = await request.Body.ReadAsync(buffer, ct)) > 0)
        {
            if (ms.Length + n > max) return null;
            ms.Write(buffer, 0, n);
        }
        return System.Text.Encoding.UTF8.GetString(ms.GetBuffer(), 0, (int)ms.Length);
    }
}
