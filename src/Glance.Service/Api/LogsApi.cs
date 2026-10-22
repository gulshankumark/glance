using System.IO.Compression;
using Glance.Core.Layouts;

namespace Glance.Service.Api;

public static class LogsApi
{
    public static void MapLogsApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/logs", (GlancePaths paths) =>
        {
            var ms = new MemoryStream();
            using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var file in Directory.EnumerateFiles(paths.Logs, "*.log"))
                {
                    try
                    {
                        // Serilog keeps the current file open for writing, so share it.
                        using var input = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                        var entry = zip.CreateEntry(Path.GetFileName(file), CompressionLevel.Fastest);
                        using var output = entry.Open();
                        input.CopyTo(output);
                    }
                    catch (IOException) { /* a file that vanished or cannot be read is skipped */ }
                }
            }
            ms.Position = 0;
            return Results.File(ms, "application/zip", "glance-logs.zip");
        });
    }
}
