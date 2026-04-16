using System;
using System.Linq;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class LibraryPhotoMasonryLayoutTests
{
    [Fact]
    public void BuildLibraryDetailMasonryChunks_CompactDensityFitsMoreTilesIntoFirstRow()
    {
        var files = Enumerable.Range(1, 18).Select(index => $"capture-{index}.png").ToList();

        var compact = MainWindow.BuildLibraryDetailMasonryChunks(files, 1600, 4, SettingsService.LibraryPhotoTileCompactPreset, 160, 420, false);
        var roomy = MainWindow.BuildLibraryDetailMasonryChunks(files, 1600, 4, SettingsService.LibraryPhotoTileRoomyPreset, 160, 756, false);

        var compactFirstRowCount = compact.SelectMany(chunk => chunk.Placements).Count(placement => Math.Abs(placement.Y) < 0.001d);
        var roomyFirstRowCount = roomy.SelectMany(chunk => chunk.Placements).Count(placement => Math.Abs(placement.Y) < 0.001d);

        Assert.True(compactFirstRowCount > roomyFirstRowCount);
    }

    [Fact]
    public void BuildLibraryDetailMasonryChunks_RoomyDensityUsesTallerQuiltCells()
    {
        var files = Enumerable.Range(1, 12).Select(index => $"capture-{index}.png").ToList();

        var compact = MainWindow.BuildLibraryDetailMasonryChunks(files, 1600, 4, SettingsService.LibraryPhotoTileCompactPreset, 160, 420, false);
        var roomy = MainWindow.BuildLibraryDetailMasonryChunks(files, 1600, 4, SettingsService.LibraryPhotoTileRoomyPreset, 160, 756, false);

        var compactFirstPlacement = compact.SelectMany(chunk => chunk.Placements).First();
        var roomyFirstPlacement = roomy.SelectMany(chunk => chunk.Placements).First();

        Assert.True(roomyFirstPlacement.Height > compactFirstPlacement.Height);
    }

    [Fact]
    public void BuildLibraryDetailMasonryChunks_PrefersRectanglesOverSquares()
    {
        var files = Enumerable.Range(1, 24).Select(index => $"capture-{index}.png").ToList();

        var quilt = MainWindow.BuildLibraryDetailMasonryChunks(files, 1600, 4, SettingsService.LibraryPhotoTileCompactPreset, 160, 420, false);
        var placements = quilt.SelectMany(chunk => chunk.Placements).ToList();
        var rectangles = placements.Count(placement => placement.Width > placement.Height);
        var squares = placements.Count(placement => placement.Width == placement.Height);

        Assert.True(rectangles > squares);
    }

    [Fact]
    public void BuildLibraryDetailMasonryChunks_UsesOccasionalHeroTiles()
    {
        var files = Enumerable.Range(1, 24).Select(index => $"capture-{index}.png").ToList();

        var quilt = MainWindow.BuildLibraryDetailMasonryChunks(files, 1600, 4, SettingsService.LibraryPhotoTileCompactPreset, 160, 420, false);
        var placements = quilt.SelectMany(chunk => chunk.Placements).ToList();
        var baselineSide = placements.Min(placement => Math.Min(placement.Width, placement.Height));

        Assert.Contains(
            placements,
            placement => placement.Height > baselineSide * 1.5d || placement.Width > baselineSide * 2.5d);
    }

    [Fact]
    public void BuildLibraryDetailMasonryChunks_DoesNotUseTripleWideRibbons()
    {
        var files = Enumerable.Range(1, 30).Select(index => $"capture-{index}.png").ToList();

        var quilt = MainWindow.BuildLibraryDetailMasonryChunks(files, 1600, 4, SettingsService.LibraryPhotoTileRoomyPreset, 160, 756, false);

        Assert.DoesNotContain(
            quilt.SelectMany(chunk => chunk.Placements),
            placement => placement.Width > placement.Height * 2.3d);
    }

    [Fact]
    public void BuildLibraryDetailMasonryChunks_AllLandscapeInputsStillMixInSquares()
    {
        var files = Enumerable.Range(1, 18).Select(index => $"capture-{index}.png").ToList();
        var mediaMap = files.ToDictionary(
            file => file,
            _ => new LibraryDetailMediaLayoutInfo
            {
                PixelWidth = 1920,
                PixelHeight = 1080
            });

        var quilt = MainWindow.BuildLibraryDetailMasonryChunks(
            files,
            1080,
            4,
            SettingsService.LibraryPhotoTileCompactPreset,
            160,
            420,
            false,
            mediaMap);

        Assert.Contains(quilt.SelectMany(chunk => chunk.Placements), placement => placement.Width == placement.Height);
    }
}
