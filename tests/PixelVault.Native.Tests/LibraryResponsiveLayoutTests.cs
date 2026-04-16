using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class LibraryResponsiveLayoutTests
{
    [Fact]
    public void CalculateResponsiveLibraryDetailLayoutForWidth_WideViewportUsesRoomyDensity()
    {
        var fromCompactPreference = MainWindow.CalculateResponsiveLibraryDetailLayoutForWidth(
            viewportWidth: 1600,
            applySavedPhotoTileSizePreference: true,
            preferredPhotoTileSize: 260,
            fixedPhotoColumns: 0);
        var fromRoomyPreference = MainWindow.CalculateResponsiveLibraryDetailLayoutForWidth(
            viewportWidth: 1600,
            applySavedPhotoTileSizePreference: true,
            preferredPhotoTileSize: 560,
            fixedPhotoColumns: 0);

        Assert.Equal(SettingsService.LibraryPhotoTileRoomyPreset, fromCompactPreference.TileSize);
        Assert.Equal(SettingsService.LibraryPhotoTileRoomyPreset, fromRoomyPreference.TileSize);
        Assert.Equal(fromCompactPreference.Columns, fromRoomyPreference.Columns);
    }

    [Fact]
    public void CalculateResponsiveLibraryDetailLayoutForWidth_NarrowViewportUsesCompactDensity()
    {
        var fromCompactPreference = MainWindow.CalculateResponsiveLibraryDetailLayoutForWidth(
            viewportWidth: 900,
            applySavedPhotoTileSizePreference: true,
            preferredPhotoTileSize: 260,
            fixedPhotoColumns: 0);
        var fromRoomyPreference = MainWindow.CalculateResponsiveLibraryDetailLayoutForWidth(
            viewportWidth: 900,
            applySavedPhotoTileSizePreference: true,
            preferredPhotoTileSize: 560,
            fixedPhotoColumns: 0);

        Assert.Equal(SettingsService.LibraryPhotoTileCompactPreset, fromCompactPreference.TileSize);
        Assert.Equal(SettingsService.LibraryPhotoTileCompactPreset, fromRoomyPreference.TileSize);
        Assert.Equal(fromCompactPreference.Columns, fromRoomyPreference.Columns);
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
            preferredPhotoTileSize: 560,
            fixedPhotoColumns: 0);

        Assert.Equal(SettingsService.LibraryPhotoTileRoomyPreset, wide.TileSize);
        Assert.Equal(SettingsService.LibraryPhotoTileCompactPreset, narrow.TileSize);
        Assert.True(wide.TileSize > narrow.TileSize);
        Assert.True(wide.Columns < narrow.Columns);
    }

    [Fact]
    public void CalculateResponsiveLibraryDetailLayoutForWidth_TimelineUsesCompactDensityEarlier()
    {
        var photo = MainWindow.CalculateResponsiveLibraryDetailLayoutForWidth(
            viewportWidth: 1240,
            timelineView: false,
            applySavedPhotoTileSizePreference: true,
            preferredPhotoTileSize: 560,
            fixedPhotoColumns: 0);
        var timeline = MainWindow.CalculateResponsiveLibraryDetailLayoutForWidth(
            viewportWidth: 1240,
            timelineView: true,
            applySavedPhotoTileSizePreference: true,
            preferredPhotoTileSize: 560,
            fixedPhotoColumns: 0);

        Assert.Equal(SettingsService.LibraryPhotoTileRoomyPreset, photo.TileSize);
        Assert.Equal(SettingsService.LibraryPhotoTileCompactPreset, timeline.TileSize);
        Assert.True(timeline.Columns >= photo.Columns);
    }
}
