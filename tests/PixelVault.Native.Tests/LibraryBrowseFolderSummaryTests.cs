using PixelVaultNative;
using Xunit;

namespace PixelVault.Native.Tests;

public sealed class LibraryBrowseFolderSummaryTests
{
    [Fact]
    public void FromFolderView_Copies_Core_Fields()
    {
        var view = new MainWindow.LibraryBrowserFolderView
        {
            ViewKey = "vk1",
            GameId = "game-1",
            Name = "Example Game",
            PrimaryFolderPath = @"D:\lib\example",
            PrimaryPlatformLabel = "Steam",
            PlatformLabels = new[] { "Steam", "PC" },
            PlatformSummaryText = "Steam · PC",
            FileCount = 42,
            PreviewImagePath = @"D:\covers\a.png",
            NewestCaptureUtcTicks = 100,
            NewestRecentSortUtcTicks = 200,
            SteamAppId = "123",
            SteamGridDbId = "sg456",
            IsCompleted100Percent = true,
            CompletedUtcTicks = 300,
            IsMergedAcrossPlatforms = true,
            IsTimelineProjection = false
        };

        var s = LibraryBrowseFolderSummary.FromFolderView(view);
        Assert.NotNull(s);
        Assert.Equal("vk1", s.ViewKey);
        Assert.Equal("game-1", s.GameId);
        Assert.Equal("Example Game", s.Name);
        Assert.Equal(@"D:\lib\example", s.PrimaryFolderPath);
        Assert.Equal("Steam", s.PrimaryPlatformLabel);
        Assert.Equal(new[] { "Steam", "PC" }, s.PlatformLabels);
        Assert.Equal("Steam · PC", s.PlatformSummaryText);
        Assert.Equal(42, s.FileCount);
        Assert.Equal(@"D:\covers\a.png", s.PreviewImagePath);
        Assert.Equal(100, s.NewestCaptureUtcTicks);
        Assert.Equal(200, s.NewestRecentSortUtcTicks);
        Assert.Equal("123", s.SteamAppId);
        Assert.Equal("sg456", s.SteamGridDbId);
        Assert.True(s.IsCompleted100Percent);
        Assert.Equal(300, s.CompletedUtcTicks);
        Assert.True(s.IsMergedAcrossPlatforms);
        Assert.False(s.IsTimelineProjection);
    }

    [Fact]
    public void FromFolderView_Null_Returns_Null()
    {
        Assert.Null(LibraryBrowseFolderSummary.FromFolderView(null));
    }

    [Fact]
    public void FromFolderView_Clones_PlatformLabels()
    {
        var labels = new[] { "PS5" };
        var view = new MainWindow.LibraryBrowserFolderView { PlatformLabels = labels };
        var s = LibraryBrowseFolderSummary.FromFolderView(view);
        Assert.NotNull(s);
        labels[0] = "Xbox";
        Assert.Equal("PS5", s.PlatformLabels[0]);
    }
}
