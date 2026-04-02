using System.Collections.Generic;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

/// <summary>
/// Covers Steam rename → path propagation used before metadata/delete/move (ImportWorkflow).
/// Phase C3 / MainWindow intake tie-in.
/// </summary>
public sealed class SteamRenamePathMappingTests
{
    [Fact]
    public void ApplySteamRenameMapToReviewItems_UpdatesPathAndFileName_WhenKeyMatches()
    {
        var oldPath = @"C:\upload\2561580_20240101120000.png";
        var newPath = @"C:\upload\MyGame_20240101120000.png";
        var map = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase) { { oldPath, newPath } };
        var item = new ReviewItem { FilePath = oldPath, FileName = "2561580_20240101120000.png" };

        MainWindow.ApplySteamRenameMapToReviewItems(new List<ReviewItem> { item }, map);

        Assert.Equal(newPath, item.FilePath);
        Assert.Equal("MyGame_20240101120000.png", item.FileName);
    }

    [Fact]
    public void ApplySteamRenameMapToReviewItems_NoOp_WhenListOrMapNullOrMapEmpty()
    {
        var item = new ReviewItem { FilePath = @"C:\a.png", FileName = "a.png" };
        MainWindow.ApplySteamRenameMapToReviewItems(null, new Dictionary<string, string> { { @"C:\a.png", @"C:\b.png" } });
        Assert.Equal(@"C:\a.png", item.FilePath);

        MainWindow.ApplySteamRenameMapToReviewItems(new List<ReviewItem> { item }, null);
        Assert.Equal(@"C:\a.png", item.FilePath);

        MainWindow.ApplySteamRenameMapToReviewItems(new List<ReviewItem> { item }, new Dictionary<string, string>());
        Assert.Equal(@"C:\a.png", item.FilePath);
    }

    [Fact]
    public void ApplySteamRenameMapToManualMetadataItems_UpdatesPathAndFileName_WhenKeyMatches()
    {
        var oldPath = @"D:\in\108710_screenshot.png";
        var newPath = @"D:\in\GameName_screenshot.png";
        var map = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase) { { oldPath, newPath } };
        var item = new ManualMetadataItem { FilePath = oldPath, FileName = "108710_screenshot.png" };

        MainWindow.ApplySteamRenameMapToManualMetadataItems(new List<ManualMetadataItem> { item }, map);

        Assert.Equal(newPath, item.FilePath);
        Assert.Equal("GameName_screenshot.png", item.FileName);
    }

    [Fact]
    public void ResolveTopLevelPathsAfterSteamRename_ReplacesMappedPaths_PassesThroughOthers()
    {
        var a = @"C:\u\1.png";
        var b = @"C:\u\2.png";
        var bRenamed = @"C:\u\two.png";
        var map = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase) { { b, bRenamed } };

        var result = MainWindow.ResolveTopLevelPathsAfterSteamRename(new[] { a, b }, map);

        Assert.Equal(new[] { a, bRenamed }, result);
    }

    [Fact]
    public void ResolveTopLevelPathsAfterSteamRename_NullOldToNew_KeepsOriginalPaths()
    {
        var paths = new[] { @"C:\x\a.jpg", @"C:\x\b.jpg" };
        var result = MainWindow.ResolveTopLevelPathsAfterSteamRename(paths, null);
        Assert.Equal(paths, result);
    }

    [Fact]
    public void ResolveTopLevelPathsAfterSteamRename_NullTopLevel_ReturnsEmpty()
    {
        var result = MainWindow.ResolveTopLevelPathsAfterSteamRename(null, new Dictionary<string, string>());
        Assert.Empty(result);
    }

    [Fact]
    public void ResolveTopLevelPathsAfterSteamRename_SkipsWhitespaceEntries()
    {
        var map = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase) { { @"C:\ok.png", @"C:\renamed.png" } };
        var result = MainWindow.ResolveTopLevelPathsAfterSteamRename(new[] { @"C:\ok.png", "", "   ", null }, map);
        Assert.Single(result);
        Assert.Equal(@"C:\renamed.png", result[0]);
    }
}
