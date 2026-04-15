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

        var compact = MainWindow.BuildLibraryDetailMasonryChunks(files, 1600, 4, 260, 160, 420, false);
        var roomy = MainWindow.BuildLibraryDetailMasonryChunks(files, 1600, 4, 420, 160, 567, false);

        var compactFirstRowCount = compact.SelectMany(chunk => chunk.Placements).Count(placement => Math.Abs(placement.Y) < 0.001d);
        var roomyFirstRowCount = roomy.SelectMany(chunk => chunk.Placements).Count(placement => Math.Abs(placement.Y) < 0.001d);

        Assert.True(compactFirstRowCount > roomyFirstRowCount);
    }

    [Fact]
    public void BuildLibraryDetailMasonryChunks_RoomyDensityUsesTallerQuiltCells()
    {
        var files = Enumerable.Range(1, 12).Select(index => $"capture-{index}.png").ToList();

        var compact = MainWindow.BuildLibraryDetailMasonryChunks(files, 1600, 4, 260, 160, 420, false);
        var roomy = MainWindow.BuildLibraryDetailMasonryChunks(files, 1600, 4, 420, 160, 567, false);

        var compactFirstPlacement = compact.SelectMany(chunk => chunk.Placements).First();
        var roomyFirstPlacement = roomy.SelectMany(chunk => chunk.Placements).First();

        Assert.True(roomyFirstPlacement.Height > compactFirstPlacement.Height);
    }
}
