using PixelVaultNative;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class LibraryThumbnailMemoryTests
{
    [Fact]
    public void ImmediateLookup_DoesNotDecodeDiskThumbnail_AndReusesWorkerBitmap()
    {
        AchievementCollectionAccessTests.OnSta(() =>
        {
            var root = Path.Combine(Path.GetTempPath(), "pv-thumbnail-memory-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var source = Path.Combine(root, "source.png");
                WritePng(source);
                var cache = new LibraryBitmapLruCache(8);
                var pipeline = new LibraryThumbnailPipeline(root, _ => false, (_, _) => null!, _ => { }, cache.TryGet, cache.Store);
                // Seed the actual disk cache, then clear only RAM (the next view is cold).
                var bitmap = pipeline.LoadImageSource(source, 160);
                Assert.NotNull(bitmap);
                Assert.True(SpinWait.SpinUntil(() => Directory.GetFiles(root, "*.png").Length > 1, 5000));
                cache.Clear();

                Assert.Null(pipeline.TryLoadCachedVisualImmediate(source, 160));
                var loaded = pipeline.LoadImageSource(source, 160);
                Assert.NotNull(loaded);
                Assert.True(loaded.IsFrozen);
                Assert.Same(loaded, pipeline.TryLoadCachedVisualImmediate(source, 150));

                File.SetLastWriteTimeUtc(source, File.GetLastWriteTimeUtc(source).AddSeconds(2));
                Assert.Null(pipeline.TryLoadCachedVisualImmediate(source, 160));
                var replacement = pipeline.LoadImageSource(source, 160);
                Assert.NotSame(loaded, replacement);
                Assert.Same(replacement, pipeline.TryLoadCachedVisualImmediate(source, 160));
                // Await the second background cache write before cleaning up the fixture.
                Assert.True(SpinWait.SpinUntil(() => Directory.GetFiles(root, "*.png").Length > 2, 5000));
            }
            finally { Directory.Delete(root, true); }
        });
    }

    [Fact]
    public void ImmediateVideoLookup_DoesNotGenerateOrDecodeAPoster()
    {
        var root = Path.Combine(Path.GetTempPath(), "pv-video-memory-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "video.mp4");
            File.WriteAllBytes(source, new byte[] { 1 });
            var calls = 0;
            var cache = new LibraryBitmapLruCache(8);
            var pipeline = new LibraryThumbnailPipeline(root, _ => true, (_, _) => { calls++; return null!; }, _ => { }, cache.TryGet, cache.Store);
            Assert.Null(pipeline.TryLoadCachedVisualImmediate(source, 160));
            Assert.Equal(0, calls);
        }
        finally { Directory.Delete(root, true); }
    }

    static void WritePng(string path)
    {
        var bitmap = BitmapSource.Create(8, 8, 96, 96, PixelFormats.Bgra32, null, new byte[8 * 8 * 4], 8 * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
