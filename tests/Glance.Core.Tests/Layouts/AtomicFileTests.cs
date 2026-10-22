using Glance.Core.Layouts;
using Glance.Testing;

namespace Glance.Core.Tests.Layouts;

public class AtomicFileTests
{
    [Fact]
    public async Task Write_waits_out_a_reader_briefly_holding_the_target_open()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "kestrel.json");
        File.WriteAllText(path, "old");
        var reader = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);   // like a config reload
        var release = Task.Delay(150).ContinueWith(_ => reader.Dispose());

        AtomicFile.Write(path, "new");

        await release;
        Assert.Equal("new", File.ReadAllText(path));
        Assert.Empty(Directory.GetFiles(dir.Path, "*.tmp"));
    }

    [Fact]
    public void Write_bytes_replaces_the_file()
    {
        using var dir = new TempDir();
        var path = Path.Combine(dir.Path, "a.bin");
        AtomicFile.Write(path, new byte[] { 1, 2 });
        AtomicFile.Write(path, new byte[] { 3 });
        Assert.Equal(new byte[] { 3 }, File.ReadAllBytes(path));
    }
}
