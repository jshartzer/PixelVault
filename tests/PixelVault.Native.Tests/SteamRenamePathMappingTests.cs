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

        SteamImportRename.ApplySteamRenameMapToReviewItems(new List<ReviewItem> { item }, map);

        Assert.Equal(newPath, item.FilePath);
        Assert.Equal("MyGame_20240101120000.png", item.FileName);
    }

    [Fact]
    public void ApplySteamRenameMapToReviewItems_NoOp_WhenListOrMapNullOrMapEmpty()
    {
        var item = new ReviewItem { FilePath = @"C:\a.png", FileName = "a.png" };
        SteamImportRename.ApplySteamRenameMapToReviewItems(null, new Dictionary<string, string> { { @"C:\a.png", @"C:\b.png" } });
        Assert.Equal(@"C:\a.png", item.FilePath);

        SteamImportRename.ApplySteamRenameMapToReviewItems(new List<ReviewItem> { item }, null);
        Assert.Equal(@"C:\a.png", item.FilePath);

        SteamImportRename.ApplySteamRenameMapToReviewItems(new List<ReviewItem> { item }, new Dictionary<string, string>());
        Assert.Equal(@"C:\a.png", item.FilePath);
    }

    [Fact]
    public void ApplySteamRenameMapToManualMetadataItems_UpdatesPathAndFileName_WhenKeyMatches()
    {
        var oldPath = @"D:\in\108710_screenshot.png";
        var newPath = @"D:\in\GameName_screenshot.png";
        var map = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase) { { oldPath, newPath } };
        var item = new ManualMetadataItem { FilePath = oldPath, FileName = "108710_screenshot.png" };

        SteamImportRename.ApplySteamRenameMapToManualMetadataItems(new List<ManualMetadataItem> { item }, map);

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

        var result = SteamImportRename.ResolveTopLevelPathsAfterSteamRename(new[] { a, b }, map);

        Assert.Equal(new[] { a, bRenamed }, result);
    }

    [Fact]
    public void ResolveTopLevelPathsAfterSteamRename_NullOldToNew_KeepsOriginalPaths()
    {
        var paths = new[] { @"C:\x\a.jpg", @"C:\x\b.jpg" };
        var result = SteamImportRename.ResolveTopLevelPathsAfterSteamRename(paths, null);
        Assert.Equal(paths, result);
    }

    [Fact]
    public void ResolveTopLevelPathsAfterSteamRename_NullTopLevel_ReturnsEmpty()
    {
        var result = SteamImportRename.ResolveTopLevelPathsAfterSteamRename(null, new Dictionary<string, string>());
        Assert.Empty(result);
    }

    [Fact]
    public void ResolveTopLevelPathsAfterSteamRename_SkipsWhitespaceEntries()
    {
        var map = new Dictionary<string, string>(System.StringComparer.OrdinalIgnoreCase) { { @"C:\ok.png", @"C:\renamed.png" } };
        var result = SteamImportRename.ResolveTopLevelPathsAfterSteamRename(new[] { @"C:\ok.png", "", "   ", null }, map);
        Assert.Single(result);
        Assert.Equal(@"C:\renamed.png", result[0]);
    }

    [Fact]
    public void TryBuildSteamRenameBase_AppIdPrefix_ReplacesWithCanonicalTitle()
    {
        Assert.True(SteamImportRename.TryBuildSteamRenameBase("2561580_20240101120000", "2561580", "My Game", null, out var nb));
        Assert.Equal("My Game_20240101120000", nb);
    }

    [Fact]
    public void TryBuildSteamRenameBase_TitleHintUnderscore_ReplacesSegment()
    {
        Assert.True(SteamImportRename.TryBuildSteamRenameBase("OldHint_20240101120000", "999", "New Title", "OldHint", out var nb));
        Assert.Equal("New Title_20240101120000", nb);
    }

    [Fact]
    public void TryBuildSteamRenameBase_AlreadyCanonicalTitleUnderscore_ReturnsSameBase()
    {
        Assert.True(SteamImportRename.TryBuildSteamRenameBase("My Game_screenshot_1", "1", "My Game", null, out var nb));
        Assert.Equal("My Game_screenshot_1", nb);
    }

    [Fact]
    public void SteamAppIdLooksLikeFilenamePrefix_RequiresBoundary_AfterDigitRun()
    {
        Assert.True(SteamImportRename.SteamAppIdLooksLikeFilenamePrefix("108710", "108710_screenshot"));
        Assert.True(SteamImportRename.SteamAppIdLooksLikeFilenamePrefix("2561580", "2561580_20240101120000"));
        Assert.True(SteamImportRename.SteamAppIdLooksLikeFilenamePrefix("730", "730"));
        Assert.True(SteamImportRename.SteamAppIdLooksLikeFilenamePrefix("730", "730-screenshot"));
        Assert.False(SteamImportRename.SteamAppIdLooksLikeFilenamePrefix("108710", "1087100_screenshot"));
        Assert.False(SteamImportRename.SteamAppIdLooksLikeFilenamePrefix("108710", "10871000_timestamp"));
        Assert.False(SteamImportRename.SteamAppIdLooksLikeFilenamePrefix("12", "123_something"));
        Assert.False(SteamImportRename.SteamAppIdLooksLikeFilenamePrefix("730", "730x"));
        Assert.False(SteamImportRename.SteamAppIdLooksLikeFilenamePrefix("2561580", "2561580.20240101120000"));
        Assert.False(SteamImportRename.SteamAppIdLooksLikeFilenamePrefix("1", "1_screenshot"));
    }

    [Fact]
    public void TryBuildSteamRenameBase_DoesNotTreatLongerNumericStem_AsShorterAppId()
    {
        Assert.False(SteamImportRename.TryBuildSteamRenameBase("1087100_screenshot", "108710", "My Game", null, out _));
    }
}
