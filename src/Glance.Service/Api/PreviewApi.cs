using Glance.Core.Sessions;

namespace Glance.Service.Api;

public static class PreviewApi
{
    public static void MapPreviewApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/preview/{key}.jpg", (string key, DeviceManager devices, HttpResponse response) =>
        {
            var jpeg = devices.Session(key)?.GetPreviewJpeg();
            if (jpeg is null) return Results.NotFound();
            response.Headers.CacheControl = "no-store";
            return Results.File(jpeg, "image/jpeg");
        });
    }
}
