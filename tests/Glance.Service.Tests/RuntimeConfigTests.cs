using System.Text.Json;
using Glance.Core.Rendering;
using Glance.Platform.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace Glance.Service.Tests;

public class RuntimeConfigTests
{
    /// <summary>Server GC (the Web SDK default) let animation frame buffers grow the service to ~800 MB (Task 15).</summary>
    [Fact]
    public void Service_uses_workstation_gc()
    {
        var dir = Path.GetDirectoryName(typeof(CoreRunner).Assembly.Location)!;
        using var config = JsonDocument.Parse(File.ReadAllText(Path.Combine(dir, "Glance.Service.runtimeconfig.json")));
        var props = config.RootElement.GetProperty("runtimeOptions").GetProperty("configProperties");
        Assert.False(props.GetProperty("System.GC.Server").GetBoolean());
    }

    [Fact]
    public async Task Service_uses_the_windows_jpeg_codec()
    {
        await using var f = new GlanceFactory();
        f.CreateClient();
        Assert.IsType<WicJpegCodec>(f.Services.GetRequiredService<IJpegCodec>());
    }
}
