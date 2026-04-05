using System.Linq;
using PixelVaultNative;
using Xunit;

namespace PixelVault.Native.Tests;

public sealed class LibraryPhotoMasonryLayoutTests
{
    [Fact]
    public void BuildLibraryDetailMasonryChunks_FirstItem_IsWideHero_WhenTwoColumns()
    {
        var files = new[] { "a.png", "b.png", "c.png" };
        var chunks = MainWindow.BuildLibraryDetailMasonryChunks(
            files,
            availableWidth: 720,
            gapPx: 8,
            baseWidth: 280,
            minWidth: 200,
            maxWidth: 360,
            includeTimelineFooter: false);

        Assert.NotEmpty(chunks);
        var first = chunks[0].Placements[0];
        Assert.Equal("a.png", first.File);
        Assert.True(first.Width > 300, "hero should span roughly two column widths");
    }

    [Fact]
    public void BuildLibraryDetailMasonryChunks_RespectsChunking_ForManyFiles()
    {
        var files = Enumerable.Range(0, 120).Select(i => $"f{i}.png").ToArray();
        var chunks = MainWindow.BuildLibraryDetailMasonryChunks(
            files,
            availableWidth: 900,
            gapPx: 8,
            baseWidth: 280,
            minWidth: 200,
            maxWidth: 400,
            includeTimelineFooter: false);

        Assert.True(chunks.Count >= 2, "large lists should split into multiple virtual rows");
        Assert.True(chunks.All(c => c.Placements.Count <= 42));
        var allFiles = chunks.SelectMany(c => c.Placements.Select(p => p.File)).ToList();
        Assert.Equal(files.Length, allFiles.Count);
        Assert.Equal(files.OrderBy(f => f).ToList(), allFiles.OrderBy(f => f).ToList());
    }
}
