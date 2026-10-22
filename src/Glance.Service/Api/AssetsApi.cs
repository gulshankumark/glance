using Glance.Core.Media;
using Microsoft.AspNetCore.Http.Features;

namespace Glance.Service.Api;

public static class AssetsApi
{
    public const long MultipartOverhead = 64 * 1024;

    /// <summary>64 MB unless the Glance:MaxUploadBytes setting lowers it (used by tests).</summary>
    public static long MaxUploadBytes(IConfiguration c) => c.GetValue<long?>("Glance:MaxUploadBytes") ?? MediaImporter.MaxBytes;

    private static readonly object TooLarge = new { error = "file is too large" };

    public static void MapAssetsApi(this IEndpointRouteBuilder app)
    {
        app.MapPost("/api/assets", async (HttpRequest request, AssetStore store, IConfiguration config, CancellationToken ct) =>
        {
            var max = MaxUploadBytes(config);
            var bodyLimit = max + MultipartOverhead;
            if (request.ContentLength > bodyLimit) return Results.Json(TooLarge, statusCode: StatusCodes.Status413PayloadTooLarge);
            if (request.HttpContext.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit) limit.MaxRequestBodySize = bodyLimit;
            if (!request.HasFormContentType) return Results.BadRequest(new { error = "expected multipart/form-data" });
            IFormCollection form;
            try { form = await request.ReadFormAsync(ct); }
            catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
            {
                return Results.Json(TooLarge, statusCode: StatusCodes.Status413PayloadTooLarge);
            }
            catch (InvalidDataException ex) when (ex.Message.Contains("length limit", StringComparison.OrdinalIgnoreCase))
            {
                return Results.Json(TooLarge, statusCode: StatusCodes.Status413PayloadTooLarge);
            }
            catch (Exception ex) when (ex is InvalidDataException or IOException or BadHttpRequestException)
            {
                return Results.BadRequest(new { error = "could not read the upload: " + ex.Message });
            }
            var file = form.Files.GetFile("file");
            if (file is null || file.Length == 0) return Results.BadRequest(new { error = "file is required" });
            if (file.Length > max) return Results.Json(TooLarge, statusCode: StatusCodes.Status413PayloadTooLarge);
            if (!Enum.TryParse<MediaKind>(form["kind"].ToString(), ignoreCase: true, out var kind) || !Enum.IsDefined(kind))
                return Results.BadRequest(new { error = "kind must be image, gif or zt" });
            var rotation = 0;
            if (form["ztRotation"].ToString() is { Length: > 0 } text
                && (!int.TryParse(text, out rotation) || rotation is not (0 or 90 or 180 or 270)))
                return Results.BadRequest(new { error = "ztRotation must be 0, 90, 180 or 270" });

            var data = new byte[file.Length];
            await using (var input = file.OpenReadStream())
                await input.ReadExactlyAsync(data, ct);
            try { return Results.Ok(store.Import(data, kind, rotation)); }
            catch (InvalidMediaException ex) { return Results.BadRequest(new { error = ex.Message }); }
        });

        app.MapGet("/api/assets", (AssetStore store) => store.List());

        app.MapDelete("/api/assets/{id}", (string id, AssetStore store) =>
            store.Delete(id) ? Results.NoContent() : Results.NotFound());

        app.MapGet("/api/assets/{id}/thumbnail.jpg", (string id, AssetStore store) =>
            store.GetThumbnail(id) is { } jpeg ? Results.File(jpeg, "image/jpeg") : Results.NotFound());
    }
}
