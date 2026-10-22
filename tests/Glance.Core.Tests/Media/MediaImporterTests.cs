using Glance.Core.Layouts;
using Glance.Core.Media;
using Glance.Testing;
using SkiaSharp;
using static Glance.Testing.TestMedia;

namespace Glance.Core.Tests.Media;

public class MediaImporterTests
{
    private static AssetStore NewStore(TempDir dir) => new(new GlancePaths(dir.Path));

    [Fact] public void Zt_authored_at_180_is_normalised_to_layout_orientation()
    {
        using var dir = new TempDir(); var store = NewStore(dir);
        var info = store.Import(Zt((41, CornerJpeg(1280, 480, corner: (1279, 479)))), MediaKind.Zt, ztAuthoredRotation: 180);
        using var f0 = SKBitmap.Decode(store.GetAnimation(info.Id)!.JpegFrames[0]);
        Assert.True(IsRedish(f0.GetPixel(2, 2)));
    }

    [Fact] public void Zt_authored_at_90_swaps_dimensions()
    {
        using var dir = new TempDir(); var store = NewStore(dir);
        var info = store.Import(Zt((41, CornerJpeg(480, 1280, corner: (0, 0)))), MediaKind.Zt, ztAuthoredRotation: 90);
        Assert.Equal((1280, 480), (info.Width, info.Height));
        var a = store.GetAnimation(info.Id)!;
        Assert.Equal((1280, 480), (a.Width, a.Height));
    }

    [Fact] public void Zt_without_rotation_keeps_frame_bytes()
    {
        using var dir = new TempDir(); var store = NewStore(dir);
        var j = TinyJpeg(SKColors.Blue);
        var info = store.Import(Zt((41, j), (82, j)), MediaKind.Zt);
        var a = store.GetAnimation(info.Id)!;
        Assert.Equal(j, a.JpegFrames[1]);
        Assert.Equal(new[] { 41, 41 }, a.DelaysMs);
        Assert.Equal(2, info.FrameCount);
    }

    [Fact] public void Zt_with_non_jpeg_frame_or_mixed_sizes_is_rejected()
    {
        using var dir = new TempDir(); var store = NewStore(dir);
        Assert.Throws<InvalidMediaException>(() => store.Import(Zt((41, new byte[] { 1, 2, 3, 4 })), MediaKind.Zt));
        Assert.Throws<InvalidMediaException>(() =>
            store.Import(Zt((41, TinyJpeg(SKColors.Red)), (82, CornerJpeg(16, 16, (0, 0)))), MediaKind.Zt));
        Assert.Empty(store.List());
    }

    [Fact] public void Imports_gif_as_animation()
    {
        using var dir = new TempDir(); var store = NewStore(dir);
        var info = store.Import(Convert.FromHexString(Gif2), MediaKind.Gif);
        var a = store.GetAnimation(info.Id)!;
        Assert.Equal(new[] { 100, 200 }, a.DelaysMs);
        Assert.Equal(2, a.JpegFrames.Count);
        Assert.Equal((1, 1), (a.Width, a.Height));
        using var f0 = SKBitmap.Decode(a.JpegFrames[0]);
        using var f1 = SKBitmap.Decode(a.JpegFrames[1]);
        Assert.True(f0.GetPixel(0, 0).Red < 60);
        Assert.True(f1.GetPixel(0, 0).Red > 200);
    }

    [Fact] public void Gif_sub_rectangle_frame_keeps_untouched_pixels_when_not_disposed()
    {
        using var dir = new TempDir(); var store = NewStore(dir);
        var a = store.GetAnimation(store.Import(Convert.FromHexString(GifSubRectKeep), MediaKind.Gif).Id)!;
        Assert.Equal((2, 1), (a.Width, a.Height));
        using var f0 = SKBitmap.Decode(a.JpegFrames[0]);
        using var f1 = SKBitmap.Decode(a.JpegFrames[1]);
        Assert.True(f0.GetPixel(0, 0).Red > 200 && f0.GetPixel(1, 0).Red is > 90 and < 170);
        Assert.True(f1.GetPixel(0, 0).Red > 200, "pixel outside the sub-rectangle must keep frame 0's colour");
        Assert.True(f1.GetPixel(1, 0).Red < 60);
    }

    [Fact] public void Gif_frame_after_restore_to_background_starts_from_a_cleared_canvas()
    {
        using var dir = new TempDir(); var store = NewStore(dir);
        var a = store.GetAnimation(store.Import(Convert.FromHexString(GifSubRectRestore), MediaKind.Gif).Id)!;
        using var f1 = SKBitmap.Decode(a.JpegFrames[1]);
        Assert.True(f1.GetPixel(0, 0).Red < 60, "disposed pixel must not show frame 0's white");
        Assert.True(f1.GetPixel(1, 0).Red > 200);
    }

    [Fact] public void Gif_whose_frames_exceed_the_pixel_budget_is_rejected_from_the_header()
    {
        var huge = Convert.FromHexString(Gif2);
        huge[6] = 0x00; huge[7] = 0x20; huge[8] = 0x00; huge[9] = 0x20;   // 8192x8192 logical screen, 2 frames = 134M pixels
        var ex = Assert.Throws<InvalidMediaException>(() => MediaImporter.Import(huge, MediaKind.Gif, maxTotalPixels: 100_000_000));
        Assert.Contains("pixels", ex.Message);
        Assert.Equal(1_000_000_000, MediaImporter.MaxTotalPixels);
    }

    [Fact] public void Zt_frames_exceeding_the_pixel_budget_are_rejected_from_jpeg_headers()
    {
        var j = TinyJpeg(SKColors.Red);   // 8x8 = 64 pixels per frame
        var zt = Zt((41, j), (82, j), (123, j));
        Assert.Throws<InvalidMediaException>(() => MediaImporter.Import(zt, MediaKind.Zt, maxTotalPixels: 191));
        Assert.Equal(3, MediaImporter.Import(zt, MediaKind.Zt, maxTotalPixels: 192).Frames.Count);
    }

    [Fact] public void Over_budget_encoded_total_is_rejected_for_gif_and_zt()
    {
        var gif = Convert.FromHexString(Gif2);
        Assert.Throws<InvalidMediaException>(() => MediaImporter.Import(gif, MediaKind.Gif, maxTotalEncodedBytes: 10));
        var j = CornerJpeg(32, 32, (0, 0));
        var zt = Zt((41, j), (82, j));
        Assert.Throws<InvalidMediaException>(() => MediaImporter.Import(zt, MediaKind.Zt, maxTotalEncodedBytes: j.Length + 1));
        Assert.Throws<InvalidMediaException>(() => MediaImporter.Import(zt, MediaKind.Zt, 180, maxTotalEncodedBytes: j.Length + 1));
        Assert.Equal(256L * 1024 * 1024, MediaImporter.MaxTotalEncodedBytes);
    }

    [Fact] public void Repeated_GetAnimation_returns_the_cached_instance_and_survives_file_removal()
    {
        using var dir = new TempDir(); var paths = new GlancePaths(dir.Path); var store = new AssetStore(paths);
        var info = store.Import(Convert.FromHexString(Gif2), MediaKind.Gif);
        var first = store.GetAnimation(info.Id)!;
        Directory.Delete(Path.Combine(paths.Assets, info.Id, "frames"), recursive: true);   // a disk re-read would now fail
        Assert.Same(first, store.GetAnimation(info.Id));
    }

    [Fact] public void Gif_zero_delay_becomes_100ms()
    {
        using var dir = new TempDir(); var store = NewStore(dir);
        var hex = Gif2.Replace("21F904000A00", "21F904000000");
        var info = store.Import(Convert.FromHexString(hex), MediaKind.Gif);
        Assert.Equal(new[] { 100, 200 }, store.GetAnimation(info.Id)!.DelaysMs);
    }

    [Fact] public void Rejects_exe_renamed_and_giant_dimensions()
    {
        using var dir = new TempDir(); var store = NewStore(dir);
        Assert.Throws<InvalidMediaException>(() => store.Import("MZ\u0090\0garbage"u8.ToArray(), MediaKind.Gif));
        var giant = Convert.FromHexString(Gif2); giant[6] = 0x60; giant[7] = 0xEA; giant[8] = 0x60; giant[9] = 0xEA; // 60000x60000
        Assert.Throws<InvalidMediaException>(() => store.Import(giant, MediaKind.Gif));
        Assert.Empty(store.List());
    }

    [Fact] public void Rejects_wrong_kind_empty_oversize_and_truncated()
    {
        using var dir = new TempDir(); var store = NewStore(dir);
        Assert.Throws<InvalidMediaException>(() => store.Import(Array.Empty<byte>(), MediaKind.Image));
        Assert.Throws<InvalidMediaException>(() => store.Import(Convert.FromHexString(Gif2), MediaKind.Image));
        Assert.Throws<InvalidMediaException>(() => store.Import(Png(4, 4, SKColors.Red), MediaKind.Gif));
        var big = new byte[MediaImporter.MaxBytes + 1]; big[0] = 0x47; big[1] = 0x49; big[2] = 0x46; big[3] = 0x38;
        Assert.Throws<InvalidMediaException>(() => store.Import(big, MediaKind.Gif));
        var png = Png(64, 64, SKColors.Red);
        Assert.Throws<InvalidMediaException>(() => store.Import(png[..(png.Length / 2)], MediaKind.Image));
        Assert.Throws<InvalidMediaException>(() => store.Import(Zt((41, TinyJpeg(SKColors.Red))), MediaKind.Zt, 45));
    }

    [Fact] public void Image_over_max_dimension_is_rejected()
    {
        using var dir = new TempDir(); var store = NewStore(dir);
        Assert.Throws<InvalidMediaException>(() => store.Import(Png(MediaImporter.MaxDimension + 1, 1, SKColors.Red), MediaKind.Image));
    }

    [Fact] public void Imports_png_and_jpeg_images_and_returns_independent_bitmaps()
    {
        using var dir = new TempDir(); var store = NewStore(dir);
        var info = store.Import(Png(20, 10, SKColors.Lime), MediaKind.Image);
        Assert.Equal(("image", 20, 10, 1), (info.Kind, info.Width, info.Height, info.FrameCount));
        using var a = store.GetImage(info.Id)!;
        using var b = store.GetImage(info.Id)!;
        Assert.NotSame(a, b);
        Assert.Equal(SKColors.Lime, a.GetPixel(3, 3));
        a.Erase(SKColors.Red);                       // mutating one copy must not affect the next
        using var c = store.GetImage(info.Id)!;
        Assert.Equal(SKColors.Lime, c.GetPixel(3, 3));
        Assert.Null(store.GetAnimation(info.Id));

        var jpg = store.Import(TinyJpeg(SKColors.Blue), MediaKind.Image);
        Assert.Equal((8, 8), (jpg.Width, jpg.Height));
    }

    [Fact] public void Large_still_is_downscaled_at_import_to_the_stored_maximum_keeping_aspect()
    {
        using var dir = new TempDir(); var store = NewStore(dir);
        var info = store.Import(Png(5000, 2000, SKColors.Green), MediaKind.Image);
        Assert.Equal((MediaImporter.MaxStoredImageDimension, 1638), (info.Width, info.Height));
        using var bmp = store.GetImage(info.Id)!;
        Assert.Equal((4096, 1638), (bmp.Width, bmp.Height));
        var tall = store.Import(Png(1000, 4500, SKColors.Green), MediaKind.Image);
        Assert.Equal((910, 4096), (tall.Width, tall.Height));
        var small = store.Import(Png(1280, 480, SKColors.Green), MediaKind.Image);
        Assert.Equal((1280, 480), (small.Width, small.Height));
    }

    [Fact] public void Import_stores_a_small_thumbnail_that_is_served_without_decoding_the_asset()
    {
        using var dir = new TempDir(); var paths = new GlancePaths(dir.Path); var store = new AssetStore(paths);
        var info = store.Import(Png(1000, 500, SKColors.Red), MediaKind.Image);
        var file = Path.Combine(paths.Assets, info.Id, "thumb.jpg");
        Assert.True(File.Exists(file));
        var thumb = store.GetThumbnail(info.Id)!;
        Assert.Equal(File.ReadAllBytes(file), thumb);
        using var bmp = SKBitmap.Decode(thumb);
        Assert.Equal((AssetStore.ThumbnailMaxWidth, 160), (bmp.Width, bmp.Height));
        Assert.Equal(0, store.CachedImageBytes);   // the full-size image was never pinned for a thumbnail
        Assert.Null(store.GetThumbnail(Guid.NewGuid().ToString("N")));
        Assert.Null(store.GetThumbnail("../x"));
    }

    [Fact] public void Thumbnail_is_made_lazily_for_older_assets_from_the_still_or_the_first_frame_only()
    {
        using var dir = new TempDir(); var paths = new GlancePaths(dir.Path); var store = new AssetStore(paths);
        var img = store.Import(Png(100, 50, SKColors.Red), MediaKind.Image);
        var anim = store.Import(Convert.FromHexString(Gif2), MediaKind.Gif);
        File.Delete(Path.Combine(paths.Assets, img.Id, "thumb.jpg"));                 // as imported before thumbnails existed
        File.Delete(Path.Combine(paths.Assets, anim.Id, "thumb.jpg"));
        File.Delete(Path.Combine(paths.Assets, anim.Id, "frames", "0001.jpg"));      // only frame 0 may be read

        using (var t = SKBitmap.Decode(store.GetThumbnail(img.Id)!)) Assert.Equal((100, 50), (t.Width, t.Height));
        using (var t = SKBitmap.Decode(store.GetThumbnail(anim.Id)!)) Assert.Equal((1, 1), (t.Width, t.Height));
        Assert.True(File.Exists(Path.Combine(paths.Assets, img.Id, "thumb.jpg")));     // stored for next time
        Assert.True(File.Exists(Path.Combine(paths.Assets, anim.Id, "thumb.jpg")));
        Assert.Equal(0, store.CachedImageBytes);
    }

    [Fact] public void Image_cache_is_bounded_by_bytes_and_evicts_the_least_recently_used()
    {
        using var dir = new TempDir(); var paths = new GlancePaths(dir.Path);
        var store = new AssetStore(paths, imageCacheBudgetBytes: 100_000);    // 100x100 BGRA = 40 000 bytes
        var a = store.Import(Png(100, 100, SKColors.Red), MediaKind.Image);
        var b = store.Import(Png(100, 100, SKColors.Green), MediaKind.Image);
        var c = store.Import(Png(100, 100, SKColors.Blue), MediaKind.Image);
        store.GetImage(a.Id)!.Dispose();
        store.GetImage(b.Id)!.Dispose();
        store.GetImage(a.Id)!.Dispose();   // a is now more recent than b
        store.GetImage(c.Id)!.Dispose();   // over budget: b goes
        Assert.Equal(80_000, store.CachedImageBytes);
        foreach (var x in new[] { a, b, c }) File.Delete(Path.Combine(paths.Assets, x.Id, "image.png"));
        Assert.NotNull(store.GetImage(a.Id));   // still cached
        Assert.NotNull(store.GetImage(c.Id));
        Assert.Null(store.GetImage(b.Id));      // evicted, and its file is gone
    }

    [Fact] public void Default_image_cache_budget_holds_one_maximal_stored_still() =>
        Assert.True(AssetStore.DefaultImageCacheBudgetBytes >= 4L * MediaImporter.MaxStoredImageDimension * MediaImporter.MaxStoredImageDimension);

    [Fact] public void List_delete_and_missing_assets()
    {
        using var dir = new TempDir(); var store = NewStore(dir);
        var a = store.Import(Png(4, 4, SKColors.Red), MediaKind.Image);
        var b = store.Import(Convert.FromHexString(Gif2), MediaKind.Gif);
        Assert.Equal(new[] { a.Id, b.Id }.Order(), store.List().Select(i => i.Id).Order());
        Assert.Matches("^[0-9a-f]{32}$", a.Id);
        Assert.Equal("gif", b.Kind);

        Assert.True(store.Delete(a.Id));
        Assert.False(store.Delete(a.Id));
        Assert.Null(store.GetImage(a.Id));
        Assert.Single(store.List());

        Assert.Null(store.GetAnimation(Guid.NewGuid().ToString("N")));
        Assert.Null(store.GetImage("../nope"));
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../layouts")]
    [InlineData("..\\layouts")]
    [InlineData("")]
    [InlineData("ABCDEFABCDEFABCDEFABCDEFABCDEFAB")]     // upper case is not a generated id
    [InlineData("0123456789abcdef0123456789abcde")]      // 31 chars
    [InlineData("0123456789abcdef0123456789abcdef\n")]   // trailing newline must not slip past the pattern
    public void Delete_rejects_non_generated_ids_and_never_touches_other_directories(string id)
    {
        using var dir = new TempDir(); var paths = new GlancePaths(dir.Path); var store = new AssetStore(paths);
        File.WriteAllText(Path.Combine(paths.Layouts, "keep.json"), "{}");
        Assert.False(store.Delete(id));
        Assert.True(File.Exists(Path.Combine(paths.Layouts, "keep.json")));
    }

    [Fact] public async Task Concurrent_readers_during_import_and_delete_stay_consistent()
    {
        using var dir = new TempDir(); var store = NewStore(dir);
        var anim = store.Import(Convert.FromHexString(Gif2), MediaKind.Gif);
        var img = store.Import(Png(16, 16, SKColors.Red), MediaKind.Image);
        var errors = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        using var cts = new CancellationTokenSource();
        var readers = Enumerable.Range(0, 4).Select(_ => Task.Run(() =>
        {
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    Assert.Equal(2, store.GetAnimation(anim.Id)!.JpegFrames.Count);
                    using var b = store.GetImage(img.Id)!;
                    Assert.Equal(16, b.Width);
                    store.List();
                }
            }
            catch (Exception e) { errors.Add(e); }
        })).ToArray();
        for (var i = 0; i < 10; i++)
        {
            var x = store.Import(Png(4, 4, SKColors.Blue), MediaKind.Image);
            Assert.True(store.Delete(x.Id));
        }
        cts.Cancel(); await Task.WhenAll(readers);
        Assert.Empty(errors);
    }

    [Fact] public void Reopened_store_sees_existing_assets_and_ignores_stray_directories()
    {
        using var dir = new TempDir(); var paths = new GlancePaths(dir.Path);
        var info = new AssetStore(paths).Import(Convert.FromHexString(Gif2), MediaKind.Gif);
        Directory.CreateDirectory(Path.Combine(paths.Assets, "not-an-asset"));
        Directory.CreateDirectory(Path.Combine(paths.Assets, Guid.NewGuid().ToString("N"))); // no meta.json
        var again = new AssetStore(paths);
        Assert.Equal(info, Assert.Single(again.List()));
        Assert.Equal(2, again.GetAnimation(info.Id)!.JpegFrames.Count);
    }
}
