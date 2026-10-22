using System.Text.Json;
using Glance.Core.Layouts;
using Glance.Testing;
using Microsoft.Extensions.Time.Testing;

namespace Glance.Core.Tests.Layouts;

public class LayoutStoreTests
{
    private static Layout Sample() => JsonSerializer.Deserialize<Layout>(LayoutJsonTests.Sample, GlanceJson.Options)!;

    [Fact]
    public void Store_keeps_last_good_layout_when_file_is_corrupted()
    {
        using var dir = new TempDir(); var store = new LayoutStore(new GlancePaths(dir.Path), TimeProvider.System);
        store.Save(Sample());
        File.WriteAllText(Path.Combine(dir.Path, "layouts", "Gaming.json"), "{ \"name\": ");   // half-written
        store.Reload("Gaming");
        Assert.Equal(4, store.Get("Gaming")!.Items.Count);
        Assert.True(store.Errors.ContainsKey("Gaming"));
    }

    [Fact]
    public void Good_edit_replaces_layout_clears_error_and_raises_Changed()
    {
        using var dir = new TempDir(); var store = new LayoutStore(new GlancePaths(dir.Path), TimeProvider.System);
        store.Save(Sample());
        var file = Path.Combine(dir.Path, "layouts", "Gaming.json");
        File.WriteAllText(file, "{ nope");
        store.Reload("Gaming");
        var l = Sample(); l.Items.RemoveAt(0);
        File.WriteAllText(file, JsonSerializer.Serialize(l, GlanceJson.Options));
        var changed = new List<string>(); store.Changed += changed.Add;
        store.Reload("Gaming");
        Assert.Equal(3, store.Get("Gaming")!.Items.Count);
        Assert.Empty(store.Errors);
        Assert.Equal(new[] { "Gaming" }, changed);
    }

    [Fact]
    public void Invalid_content_and_name_mismatch_keep_last_good()
    {
        using var dir = new TempDir(); var store = new LayoutStore(new GlancePaths(dir.Path), TimeProvider.System);
        store.Save(Sample());
        var file = Path.Combine(dir.Path, "layouts", "Gaming.json");
        var bad = Sample(); bad.Width = 99999;
        File.WriteAllText(file, JsonSerializer.Serialize(bad, GlanceJson.Options));
        store.Reload("Gaming");
        Assert.Equal(1280, store.Get("Gaming")!.Width);
        Assert.Contains("width", store.Errors["Gaming"]);

        var other = Sample(); other.Name = "Other";
        File.WriteAllText(file, JsonSerializer.Serialize(other, GlanceJson.Options));
        store.Reload("Gaming");
        Assert.Contains("does not match", store.Errors["Gaming"]);
        Assert.Null(store.Get("Other"));
    }

    [Fact]
    public void Unknown_item_type_is_an_error_not_an_exception()
    {
        using var dir = new TempDir(); var paths = new GlancePaths(dir.Path);
        File.WriteAllText(Path.Combine(paths.Layouts, "X.json"),
            """{"name":"X","width":1,"height":1,"background":{"type":"color","color":"#000000"},"items":[{"type":"bogus","id":"a"}]}""");
        var store = new LayoutStore(paths, TimeProvider.System);
        Assert.Empty(store.Names);
        Assert.True(store.Errors.ContainsKey("X"));
    }

    [Fact]
    public void Loads_existing_files_on_construction_and_lists_sorted_names()
    {
        using var dir = new TempDir(); var paths = new GlancePaths(dir.Path);
        using (var first = new LayoutStore(paths, TimeProvider.System))
        {
            var b = Sample(); b.Name = "B"; first.Save(b);
            first.Save(Sample());
        }
        var store = new LayoutStore(paths, TimeProvider.System);
        Assert.Equal(new[] { "B", "Gaming" }, store.Names);
    }

    [Theory]
    [InlineData("../evil")] [InlineData("a/b")] [InlineData("con")]
    public void Save_rejects_path_traversing_or_reserved_names_and_writes_nothing(string name)
    {
        using var dir = new TempDir(); var store = new LayoutStore(new GlancePaths(dir.Path), TimeProvider.System);
        var l = Sample(); l.Name = name;
        Assert.Throws<ArgumentException>(() => store.Save(l));
        Assert.Empty(Directory.GetFiles(dir.Path, "*", SearchOption.AllDirectories));
        Assert.Null(store.Get(name));
        Assert.False(store.Delete(name));
        store.Reload(name);   // must not throw
    }

    [Fact]
    public void Delete_removes_file_and_layout()
    {
        using var dir = new TempDir(); var store = new LayoutStore(new GlancePaths(dir.Path), TimeProvider.System);
        store.Save(Sample());
        Assert.True(store.Delete("Gaming"));
        Assert.False(store.Delete("Gaming"));
        Assert.Null(store.Get("Gaming"));
        Assert.False(File.Exists(Path.Combine(dir.Path, "layouts", "Gaming.json")));
    }

    [Fact]
    public void Throwing_Changed_subscriber_does_not_escape_Reload()
    {
        using var dir = new TempDir(); var store = new LayoutStore(new GlancePaths(dir.Path), TimeProvider.System);
        store.Changed += _ => throw new InvalidOperationException("boom");
        store.Save(Sample());
        File.WriteAllText(Path.Combine(dir.Path, "layouts", "Gaming.json"), "{}");
        store.Reload("Gaming");
    }

    [Fact]
    public void Watcher_debounces_and_reloads_edits_made_on_disk()
    {
        using var dir = new TempDir(); var time = new FakeTimeProvider();
        using var store = new LayoutStore(new GlancePaths(dir.Path), time);
        var changed = new System.Collections.Concurrent.ConcurrentQueue<string>();
        store.Changed += changed.Enqueue;
        store.StartWatching();

        File.WriteAllText(Path.Combine(dir.Path, "layouts", "Gaming.json"), JsonSerializer.Serialize(Sample(), GlanceJson.Options));
        var deadline = DateTime.UtcNow.AddSeconds(10);
        while (changed.IsEmpty && DateTime.UtcNow < deadline)
        {
            time.Advance(TimeSpan.FromMilliseconds(300));
            Thread.Sleep(20);
        }
        Assert.Equal(4, store.Get("Gaming")!.Items.Count);

        File.WriteAllText(Path.Combine(dir.Path, "layouts", "Gaming.json"), "{ half");   // watcher must survive garbage
        deadline = DateTime.UtcNow.AddSeconds(10);
        while (!store.Errors.ContainsKey("Gaming") && DateTime.UtcNow < deadline)
        {
            time.Advance(TimeSpan.FromMilliseconds(300));
            Thread.Sleep(20);
        }
        Assert.True(store.Errors.ContainsKey("Gaming"));
        Assert.Equal(4, store.Get("Gaming")!.Items.Count);
    }

    [Fact]
    public void Watcher_debounce_coalesces_rapid_writes_into_one_reload_after_250ms()
    {
        using var dir = new TempDir(); var time = new FakeTimeProvider();
        using var store = new LayoutStore(new GlancePaths(dir.Path), time);
        var count = 0; store.Changed += _ => Interlocked.Increment(ref count);
        store.StartWatching();
        var file = Path.Combine(dir.Path, "layouts", "Gaming.json");
        for (var i = 0; i < 3; i++)
        {
            var l = Sample(); l.Width = 1000 + i;
            File.WriteAllText(file, JsonSerializer.Serialize(l, GlanceJson.Options));
            Thread.Sleep(400);   // real time: let the watcher deliver; fake time is frozen
        }
        time.Advance(TimeSpan.FromMilliseconds(249));
        Assert.Equal(0, Volatile.Read(ref count));
        Assert.Null(store.Get("Gaming"));
        time.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal(1, Volatile.Read(ref count));
        Assert.Equal(1002, store.Get("Gaming")!.Width);
        time.Advance(TimeSpan.FromSeconds(5));
        Assert.Equal(1, Volatile.Read(ref count));
    }
}
