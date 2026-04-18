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

        // Scope to the first chunk only — BuildLibraryDetailMasonryChunks caps each chunk's paint
        // height, so roomy (3 cols, max ~6 rows per chunk) splits 18 files across multiple chunks,
        // each with its own row-0 placements. Summing Y=0 across all chunks would make "first row"
        // count proportional to chunk count, not column count, which defeats the density comparison.
        var compactFirstRowCount = compact.FirstOrDefault()?.Placements.Count(placement => Math.Abs(placement.Y) < 0.001d) ?? 0;
        var roomyFirstRowCount = roomy.FirstOrDefault()?.Placements.Count(placement => Math.Abs(placement.Y) < 0.001d) ?? 0;

        Assert.True(compactFirstRowCount > roomyFirstRowCount, $"compact={compactFirstRowCount}, roomy={roomyFirstRowCount}");
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
        // Landscape inputs enter the aspect >= 1.55 branch of BuildLibraryQuiltShapePreferenceOrder,
        // which leads with (2,1) for ~4 of 6 pattern buckets. The quilt packer then naturally fills
        // any trailing single-column slots with (1,1) cells, so the observed rectangle share is a
        // meaningful fraction (~1/3) rather than a strict majority. This test asserts the algorithm
        // actually uses its rectangle preference when fed landscape input; AllLandscapeInputsStillMixInSquares
        // is the sibling covering the square-fill side of the same quilt.
        //
        // Without a mediaMap the test used to rely on ResolveLibraryDetailAspectRatio's filename-hash
        // fallback across {0.56 .. 2.0}, where only 3 of 8 bins even enter the rectangle-preferring
        // branch. Combined with LibraryDetailFileLayoutHash previously being randomized per-process,
        // that made this test flake per dotnet test invocation. Both issues fixed:
        // LibraryDetailFileLayoutHash is now a deterministic FNV-1a, and this test now explicitly
        // supplies a landscape mediaMap so the assertion exercises the branch it names.
        var files = Enumerable.Range(1, 24).Select(index => $"capture-{index}.png").ToList();
        var mediaMap = files.ToDictionary(
            file => file,
            _ => new LibraryDetailMediaLayoutInfo
            {
                PixelWidth = 1920,
                PixelHeight = 1080
            });

        var quilt = MainWindow.BuildLibraryDetailMasonryChunks(
            files,
            1600,
            4,
            SettingsService.LibraryPhotoTileCompactPreset,
            160,
            420,
            false,
            mediaMap);
        var placements = quilt.SelectMany(chunk => chunk.Placements).ToList();
        var rectangles = placements.Count(placement => placement.Width > placement.Height);
        var squares = placements.Count(placement => placement.Width == placement.Height);

        Assert.True(
            rectangles >= placements.Count / 4 && rectangles > 0,
            $"expected landscape input to produce a meaningful fraction of rectangles (>= 25%); rect={rectangles}, sq={squares}, total={placements.Count}");
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
