using System;
using System.Linq;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class ConsoleIdentificationTests
{
    [Fact]
    public void MergePlatformTagsWithFilenamePlatformHint_EmptyTagsAndPlayStationParse_AddsPs5Family()
    {
        var parsed = new FilenameParseResult
        {
            PlatformLabel = "PlayStation",
            PlatformTags = new[] { "PlayStation" }
        };

        var merged = MainWindow.MergePlatformTagsWithFilenamePlatformHint(Array.Empty<string>(), parsed);

        Assert.Contains("PS5", merged);
        Assert.Contains("PlayStation", merged);
        Assert.Equal("PS5", MainWindow.DetermineConsoleLabelFromTags(merged));
    }

    [Fact]
    public void MergePlatformTagsWithFilenamePlatformHint_SteamTagsRemainAuthoritative()
    {
        var parsed = new FilenameParseResult
        {
            PlatformLabel = "PS5",
            PlatformTags = new[] { "PS5", "PlayStation" }
        };

        var merged = MainWindow.MergePlatformTagsWithFilenamePlatformHint(new[] { "Steam" }, parsed);

        Assert.Equal(new[] { "Steam" }, merged);
        Assert.Equal("Steam", MainWindow.DetermineConsoleLabelFromTags(merged));
    }

    [Fact]
    public void MergePlatformTagsWithFilenamePlatformHint_CustomPlatformAddsPlatformPrefixTag()
    {
        var parsed = new FilenameParseResult
        {
            PlatformLabel = "Switch",
            PlatformTags = Array.Empty<string>()
        };

        var merged = MainWindow.MergePlatformTagsWithFilenamePlatformHint(Array.Empty<string>(), parsed);

        Assert.Contains("Platform:Switch", merged);
        Assert.Equal("Switch", MainWindow.DetermineConsoleLabelFromTags(merged));
    }

    [Fact]
    public void MergePlatformTagsWithFilenamePlatformHint_MultipleTagsBlockFilenameFallback()
    {
        var parsed = new FilenameParseResult
        {
            PlatformLabel = "Switch",
            PlatformTags = Array.Empty<string>()
        };

        var merged = MainWindow.MergePlatformTagsWithFilenamePlatformHint(new[] { "Steam", "PS5", "PlayStation" }, parsed);

        Assert.DoesNotContain("Platform:Switch", merged);
        Assert.Equal("Multiple Tags", MainWindow.DetermineConsoleLabelFromTags(merged));
    }

    [Fact]
    public void ApplyFilenameParseResultToManualPlatformFlags_PlayStationMapsToPs5()
    {
        MainWindow.ApplyFilenameParseResultToManualPlatformFlags(
            new FilenameParseResult { PlatformLabel = "PlayStation", PlatformTags = new[] { "PlayStation" } },
            out var tagSteam,
            out var tagPc,
            out var tagEmulation,
            out var tagPs5,
            out var tagXbox,
            out var tagOther,
            out var customPlatformTag);

        Assert.False(tagSteam);
        Assert.False(tagPc);
        Assert.False(tagEmulation);
        Assert.True(tagPs5);
        Assert.False(tagXbox);
        Assert.False(tagOther);
        Assert.Equal(string.Empty, customPlatformTag);
    }

    [Fact]
    public void ApplyFilenameParseResultToManualPlatformFlags_CustomPlatformMapsToOtherWithCustomTag()
    {
        MainWindow.ApplyFilenameParseResultToManualPlatformFlags(
            new FilenameParseResult { PlatformLabel = "Switch", PlatformTags = Array.Empty<string>() },
            out var tagSteam,
            out var tagPc,
            out var tagEmulation,
            out var tagPs5,
            out var tagXbox,
            out var tagOther,
            out var customPlatformTag);

        Assert.False(tagSteam);
        Assert.False(tagPc);
        Assert.False(tagEmulation);
        Assert.False(tagPs5);
        Assert.False(tagXbox);
        Assert.True(tagOther);
        Assert.Equal("Switch", customPlatformTag);
    }

    [Fact]
    public void MergePlatformTagsWithFilenamePlatformHint_XboxPcParse_AddsCustomPlatformTag()
    {
        var parsed = new FilenameParseResult
        {
            PlatformLabel = "Xbox/Windows",
            PlatformTags = Array.Empty<string>()
        };

        var merged = MainWindow.MergePlatformTagsWithFilenamePlatformHint(Array.Empty<string>(), parsed);

        Assert.Contains("Platform:Xbox PC", merged);
        Assert.Equal("Xbox PC", MainWindow.DetermineConsoleLabelFromTags(merged));
    }

    [Fact]
    public void ApplyFilenameParseResultToManualPlatformFlags_OtherLeavesAllFlagsOff()
    {
        MainWindow.ApplyFilenameParseResultToManualPlatformFlags(
            new FilenameParseResult { PlatformLabel = "Other", PlatformTags = Array.Empty<string>() },
            out var tagSteam,
            out var tagPc,
            out var tagEmulation,
            out var tagPs5,
            out var tagXbox,
            out var tagOther,
            out var customPlatformTag);

        Assert.False(tagSteam);
        Assert.False(tagPc);
        Assert.False(tagEmulation);
        Assert.False(tagPs5);
        Assert.False(tagXbox);
        Assert.False(tagOther);
        Assert.Equal(string.Empty, customPlatformTag);
    }
}
