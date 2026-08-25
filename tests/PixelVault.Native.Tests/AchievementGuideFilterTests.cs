using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class AchievementGuideFilterTests
{
    readonly AchievementGuideEntry entry = new()
    {
        Title = "Hidden Collector",
        Description = "Find every hidden item.",
        ProviderAchievementId = "ACH_COLLECT_ALL",
        GuideText = "Check the final room before leaving.",
        Tags = "collectible, missable",
        IsMissable = true
    };

    [Fact]
    public void MatchesGuideFilter_AppliesGuideStateAndMissableFilters()
    {
        Assert.True(AchievementGuideWindow.MatchesGuideFilter(entry, null, "guided", ""));
        Assert.False(AchievementGuideWindow.MatchesGuideFilter(entry, null, "unguided", ""));
        Assert.True(AchievementGuideWindow.MatchesGuideFilter(entry, null, "missable", ""));
    }

    [Fact]
    public void MatchesGuideFilter_LockedRequiresKnownLockedProgress()
    {
        Assert.True(AchievementGuideWindow.MatchesGuideFilter(
            entry,
            new GameAchievementsFetchService.AchievementRow { ProgressKnown = true, Unlocked = false },
            "locked",
            ""));
        Assert.False(AchievementGuideWindow.MatchesGuideFilter(
            entry,
            new GameAchievementsFetchService.AchievementRow { ProgressKnown = false },
            "locked",
            ""));
    }

    [Theory]
    [InlineData("collector")]
    [InlineData("ACH_COLLECT")]
    [InlineData("final room")]
    [InlineData("MISSABLE")]
    public void MatchesGuideFilter_SearchesOfficialAndAuthoredFieldsCaseInsensitively(string search)
    {
        Assert.True(AchievementGuideWindow.MatchesGuideFilter(entry, null, "all", search));
    }

    [Fact]
    public void MatchesGuideFilter_SearchRejectsUnrelatedText()
    {
        Assert.False(AchievementGuideWindow.MatchesGuideFilter(entry, null, "all", "multiplayer wins"));
    }
}
