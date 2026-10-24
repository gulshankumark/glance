using Glance.Core.Notices;

namespace Glance.Service.Api;

public static class NoticesApi
{
    public static void MapNoticesApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/notices", (NoticeBoard board) => board.Active);
        app.MapPost("/api/notices/{id}/dismiss", (string id, NoticeBoard board) =>
        {
            board.Dismiss(id);
            return Results.NoContent();
        });
    }
}
