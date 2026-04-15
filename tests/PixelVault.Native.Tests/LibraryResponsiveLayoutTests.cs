using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class LibraryResponsiveLayoutTests
{
    [Fact]
    public void CalculateResponsiveLibraryDetailLayoutForWidth_AutoMode_UsesPreferredCaptureSize()
    {
        var compact = MainWindow.CalculateResponsiveLibraryDetailLayoutForWidth(
            viewportWidth: 1600,
            applySavedPhotoTileSizePreference: true,
            preferredPhotoTileSize: 260,
            fixedPhotoColumns: 0);
        var roomy = MainWindow.CalculateResponsiveLibraryDetailLayoutForWidth(
            viewportWidth: 1600,
            applySavedPhotoTileSizePreference: true,
            preferredPhotoTileSize: 420,
            fixedPhotoColumns: 0);

        Assert.True(compact.TileSize < roomy.TileSize);
        Assert.True(compact.Columns > roomy.Columns);
    }

    [Fact]
    public void CalculateResponsiveLibraryDetailLayoutForWidth_FixedColumns_StayFixedAcrossViewportSizes()
    {
        var wide = MainWindow.CalculateResponsiveLibraryDetailLayoutForWidth(
            viewportWidth: 2200,
            applySavedPhotoTileSizePreference: true,
            preferredPhotoTileSize: 260,
            fixedPhotoColumns: 8);
        var narrow = MainWindow.CalculateResponsiveLibraryDetailLayoutForWidth(
            viewportWidth: 1100,
            applySavedPhotoTileSizePreference: true,
            preferredPhotoTileSize: 420,
            fixedPhotoColumns: 8);

        Assert.Equal(8, wide.Columns);
        Assert.Equal(8, narrow.Columns);
        Assert.True(narrow.TileSize < wide.TileSize);
    }
}
