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

        Assert.Equal(260, compact.TileSize);
        Assert.Equal(420, roomy.TileSize);
        Assert.True(compact.Columns > roomy.Columns);
    }

    [Fact]
    public void CalculateResponsiveLibraryDetailLayoutForWidth_IgnoresLegacyFixedColumnSetting()
    {
        var auto = MainWindow.CalculateResponsiveLibraryDetailLayoutForWidth(
            viewportWidth: 1600,
            applySavedPhotoTileSizePreference: true,
            preferredPhotoTileSize: 260,
            fixedPhotoColumns: 0);
        var legacyPinned = MainWindow.CalculateResponsiveLibraryDetailLayoutForWidth(
            viewportWidth: 1600,
            applySavedPhotoTileSizePreference: true,
            preferredPhotoTileSize: 260,
            fixedPhotoColumns: 8);

        Assert.Equal(auto.Columns, legacyPinned.Columns);
        Assert.Equal(auto.TileSize, legacyPinned.TileSize);
    }

    [Fact]
    public void CalculateResponsiveLibraryDetailLayoutForWidth_ReactsToViewportWidth()
    {
        var wide = MainWindow.CalculateResponsiveLibraryDetailLayoutForWidth(
            viewportWidth: 1800,
            applySavedPhotoTileSizePreference: true,
            preferredPhotoTileSize: 260,
            fixedPhotoColumns: 0);
        var narrow = MainWindow.CalculateResponsiveLibraryDetailLayoutForWidth(
            viewportWidth: 960,
            applySavedPhotoTileSizePreference: true,
            preferredPhotoTileSize: 260,
            fixedPhotoColumns: 0);

        Assert.Equal(wide.TileSize, narrow.TileSize);
        Assert.True(narrow.Columns < wide.Columns);
    }
}
