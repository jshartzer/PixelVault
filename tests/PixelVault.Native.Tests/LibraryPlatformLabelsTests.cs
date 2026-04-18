using System.Windows.Media;
using Xunit;

namespace PixelVaultNative.Tests;

/// <summary>
/// PV-PLN-UI-001 Step 12: guard rails for <see cref="LibraryPlatformLabels"/> so the guess-label
/// branching, Steam-manual-export gate, group sort, and badge brush mapping stay byte-identical —
/// these drive the intake preview, manual-metadata drawer, folder-tile badges, and the
/// platform-grouped folder list.
/// </summary>
public sealed class LibraryPlatformLabelsTests
{
    [Fact]
    public void PrimaryPlatformLabel_ReturnsParsedLabelVerbatim()
    {
        var parsed = new FilenameParseResult { PlatformLabel = "Xbox PC" };
        Assert.Equal("Xbox PC", LibraryPlatformLabels.PrimaryPlatformLabel(parsed));
    }

    [Fact]
    public void FilenameGuessLabel_PrefersSteamAppIdOverEverything()
    {
        var parsed = new FilenameParseResult
        {
            SteamAppId = "12345",
            NonSteamId = "ignored",
            RoutesToManualWhenMissingSteamAppId = true,
            PlatformLabel = "Steam"
        };
        Assert.Equal("Steam AppID 12345", LibraryPlatformLabels.FilenameGuessLabel(parsed));
    }

    [Fact]
    public void FilenameGuessLabel_FallsBackToNonSteamIdBeforeManualHint()
    {
        var parsed = new FilenameParseResult
        {
            NonSteamId = "ns-42",
            RoutesToManualWhenMissingSteamAppId = true,
            PlatformLabel = "Steam"
        };
        Assert.Equal("Non-Steam ID ns-42", LibraryPlatformLabels.FilenameGuessLabel(parsed));
    }

    [Fact]
    public void FilenameGuessLabel_UsesManualHintWhenNoIdsAndSteamRouting()
    {
        var parsed = new FilenameParseResult
        {
            RoutesToManualWhenMissingSteamAppId = true,
            PlatformLabel = "Steam"
        };
        Assert.Equal("Steam export | AppID needed", LibraryPlatformLabels.FilenameGuessLabel(parsed));
    }

    [Fact]
    public void FilenameGuessLabel_FallsBackToPlatformLabelWhenKnown()
    {
        var parsed = new FilenameParseResult { PlatformLabel = "Xbox" };
        Assert.Equal("Xbox", LibraryPlatformLabels.FilenameGuessLabel(parsed));
    }

    [Fact]
    public void FilenameGuessLabel_SaysNoConfidentMatchWhenLabelIsOther()
    {
        var parsed = new FilenameParseResult { PlatformLabel = "Other" };
        Assert.Equal("No confident match", LibraryPlatformLabels.FilenameGuessLabel(parsed));
    }

    [Fact]
    public void FilenameGuessLabel_OtherCompareIsCaseInsensitive()
    {
        var parsed = new FilenameParseResult { PlatformLabel = "other" };
        Assert.Equal("No confident match", LibraryPlatformLabels.FilenameGuessLabel(parsed));
    }

    [Fact]
    public void IsSteamManualExportWithoutAppId_TrueOnlyWhenAllGatesPass()
    {
        var needsAppId = new FilenameParseResult
        {
            RoutesToManualWhenMissingSteamAppId = true,
            SteamAppId = string.Empty,
            NonSteamId = string.Empty
        };
        Assert.True(LibraryPlatformLabels.IsSteamManualExportWithoutAppId(needsAppId));
    }

    [Fact]
    public void IsSteamManualExportWithoutAppId_FalseWhenAppIdAlreadyAttached()
    {
        var withApp = new FilenameParseResult
        {
            RoutesToManualWhenMissingSteamAppId = true,
            SteamAppId = "42",
            NonSteamId = string.Empty
        };
        Assert.False(LibraryPlatformLabels.IsSteamManualExportWithoutAppId(withApp));
    }

    [Fact]
    public void IsSteamManualExportWithoutAppId_FalseWhenNonSteamIdSatisfies()
    {
        var withNonSteam = new FilenameParseResult
        {
            RoutesToManualWhenMissingSteamAppId = true,
            SteamAppId = string.Empty,
            NonSteamId = "ns-1"
        };
        Assert.False(LibraryPlatformLabels.IsSteamManualExportWithoutAppId(withNonSteam));
    }

    [Fact]
    public void IsSteamManualExportWithoutAppId_FalseWhenNotRoutedToManual()
    {
        var notRouting = new FilenameParseResult
        {
            RoutesToManualWhenMissingSteamAppId = false,
            SteamAppId = string.Empty,
            NonSteamId = string.Empty
        };
        Assert.False(LibraryPlatformLabels.IsSteamManualExportWithoutAppId(notRouting));
    }

    [Theory]
    [InlineData("Steam", 0)]
    [InlineData("Emulation", 1)]
    [InlineData("PS5", 2)]
    [InlineData("Xbox", 3)]
    [InlineData("Xbox PC", 4)]
    [InlineData("PC", 5)]
    [InlineData("Multiple Tags", 6)]
    [InlineData("Other", 7)]
    [InlineData("PlayStation", 8)]
    [InlineData("", 8)]
    [InlineData("unknown label", 8)]
    public void PlatformGroupOrder_CoversAllKnownBucketsAndFallsBackToEight(string label, int expected)
    {
        Assert.Equal(expected, LibraryPlatformLabels.PlatformGroupOrder(label));
    }

    [Theory]
    [InlineData("Xbox", "#FF2E8B57")]
    [InlineData("Xbox PC", "#FF4D8F68")]
    [InlineData("Steam", "#FF2F6FDB")]
    [InlineData("Emulation", "#FFB26A3C")]
    [InlineData("PC", "#FF4F6D7A")]
    [InlineData("PS5", "#FF2563EB")]
    [InlineData("PlayStation", "#FF2563EB")]
    [InlineData("Unknown", "#FF8B6F47")]
    [InlineData("", "#FF8B6F47")]
    public void PreviewBadgeBrush_ReturnsSolidColorBrushWithExpectedColor(string label, string expectedArgb)
    {
        var brush = LibraryPlatformLabels.PreviewBadgeBrush(label);
        var solid = Assert.IsType<SolidColorBrush>(brush);
        Assert.Equal(expectedArgb, solid.Color.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }
}
