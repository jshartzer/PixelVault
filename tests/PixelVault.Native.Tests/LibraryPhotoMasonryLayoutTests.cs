using System.Linq;
using PixelVaultNative;
using Xunit;

namespace PixelVault.Native.Tests;

public sealed class LibraryPhotoMasonryLayoutTests
{
    [Fact]
    public void BuildLibraryDetailMasonryChunks_FirstItem_IsWideHero_WhenLayoutHasRoom()
    {
        var files = new[] { "a.png", "b.png", "c.png", "d.png", "e.png" };
        var media = files.ToDictionary(
            file => file,
            _ => new LibraryDetailMediaLayoutInfo { PixelWidth = 3840, PixelHeight = 2160 },
            System.StringComparer.OrdinalIgnoreCase);
        var chunks = MainWindow.BuildLibraryDetailMasonryChunks(
            files,
            availableWidth: 720,
            gapPx: 8,
            baseWidth: 280,
            minWidth: 200,
            maxWidth: 360,
            includeTimelineFooter: false,
            mediaLayoutByFile: media);

        Assert.NotEmpty(chunks);
        var first = chunks[0].Placements[0];
        Assert.Equal("a.png", first.File);
        Assert.True(first.Width > 300, "hero should span roughly two column widths");
    }

    [Fact]
    public void BuildLibraryDetailMasonryChunks_RespectsChunking_ForManyFiles()
    {
        var files = Enumerable.Range(0, 120).Select(i => $"f{i}.png").ToArray();
        var media = files.ToDictionary(
            file => file,
            _ => new LibraryDetailMediaLayoutInfo { PixelWidth = 2560, PixelHeight = 1440 },
            System.StringComparer.OrdinalIgnoreCase);
        var chunks = MainWindow.BuildLibraryDetailMasonryChunks(
            files,
            availableWidth: 900,
            gapPx: 8,
            baseWidth: 280,
            minWidth: 200,
            maxWidth: 400,
            includeTimelineFooter: false,
            mediaLayoutByFile: media);

        Assert.True(chunks.Count >= 2, "large lists should split into multiple virtual rows");
        Assert.True(chunks.All(c => c.Placements.Count <= 42));
        var allFiles = chunks.SelectMany(c => c.Placements.Select(p => p.File)).ToList();
        Assert.Equal(files.Length, allFiles.Count);
        Assert.Equal(files.OrderBy(f => f).ToList(), allFiles.OrderBy(f => f).ToList());
    }

    [Fact]
    public void BuildLibraryDetailMasonryChunks_UsesFewerColumns_ForSparseGroups()
    {
        var files = new[] { "single.png" };
        var media = new System.Collections.Generic.Dictionary<string, LibraryDetailMediaLayoutInfo>(System.StringComparer.OrdinalIgnoreCase)
        {
            ["single.png"] = new LibraryDetailMediaLayoutInfo { PixelWidth = 3840, PixelHeight = 2160 }
        };

        var chunks = MainWindow.BuildLibraryDetailMasonryChunks(
            files,
            availableWidth: 1400,
            gapPx: 8,
            baseWidth: 520,
            minWidth: 280,
            maxWidth: 560,
            includeTimelineFooter: false,
            mediaLayoutByFile: media);

        Assert.Single(chunks);
        Assert.Single(chunks[0].Placements);
        Assert.True(chunks[0].Placements[0].Width >= 500, "single-item groups should breathe instead of being forced into a tiny column");
    }

    [Fact]
    public void BuildLibraryDetailMasonryChunks_UsesMediaAspectRatio_ForTileHeight()
    {
        var files = new[] { "wide.png", "portrait.png" };
        var media = new System.Collections.Generic.Dictionary<string, LibraryDetailMediaLayoutInfo>(System.StringComparer.OrdinalIgnoreCase)
        {
            ["wide.png"] = new LibraryDetailMediaLayoutInfo { PixelWidth = 3840, PixelHeight = 2160 },
            ["portrait.png"] = new LibraryDetailMediaLayoutInfo { PixelWidth = 1080, PixelHeight = 1920 }
        };

        var chunks = MainWindow.BuildLibraryDetailMasonryChunks(
            files,
            availableWidth: 540,
            gapPx: 8,
            baseWidth: 280,
            minWidth: 200,
            maxWidth: 360,
            includeTimelineFooter: false,
            mediaLayoutByFile: media);

        var placements = chunks.SelectMany(chunk => chunk.Placements).ToDictionary(p => p.File, System.StringComparer.OrdinalIgnoreCase);
        Assert.True(placements["portrait.png"].Height > placements["wide.png"].Height, "portrait media should occupy more vertical space than wide media at comparable widths");
    }
}
