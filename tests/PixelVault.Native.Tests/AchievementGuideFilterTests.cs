using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class AchievementGuideFilterTests
{
    [Fact]
    public void GuideAchievementProgressLabel_DescribesIconStateWithoutGlyphs()
    {
        Assert.Equal("progress unknown", AchievementGuideWindow.GuideAchievementProgressLabel(null));
        Assert.Equal("progress unknown", AchievementGuideWindow.GuideAchievementProgressLabel(new GameAchievementsFetchService.AchievementRow()));
        Assert.Equal("locked", AchievementGuideWindow.GuideAchievementProgressLabel(new GameAchievementsFetchService.AchievementRow
        {
            ProgressKnown = true,
            Unlocked = false
        }));
        Assert.Equal("unlocked", AchievementGuideWindow.GuideAchievementProgressLabel(new GameAchievementsFetchService.AchievementRow
        {
            ProgressKnown = true,
            Unlocked = true
        }));
    }

    readonly AchievementGuideEntry entry = new()
    {
        AchievementId = 42,
        Title = "Hidden Collector",
        Description = "Find every hidden item.",
        ProviderAchievementId = "ACH_COLLECT_ALL",
        GuideText = "Check the final room before leaving.",
        Tags = "collectible, missable",
        GroupName = "Story DLC",
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
    [InlineData("story dlc")]
    public void MatchesGuideFilter_SearchesOfficialAndAuthoredFieldsCaseInsensitively(string search)
    {
        Assert.True(AchievementGuideWindow.MatchesGuideFilter(entry, null, "all", search));
    }

    [Fact]
    public void MatchesGuideFilter_SearchRejectsUnrelatedText()
    {
        Assert.False(AchievementGuideWindow.MatchesGuideFilter(entry, null, "all", "multiplayer wins"));
    }

    [Fact]
    public void EffectiveGuideGroupName_UsesBaseGameFallback()
    {
        Assert.Equal("Base Game", AchievementGuideWindow.EffectiveGuideGroupName(null));
        Assert.Equal("Base Game", AchievementGuideWindow.EffectiveGuideGroupName(new AchievementGuideEntry()));
        Assert.Equal("The Bright Lord", AchievementGuideWindow.EffectiveGuideGroupName(new AchievementGuideEntry
        {
            GroupName = "  The Bright Lord  "
        }));
    }

    [Fact]
    public void IsPreferredGuideEntry_MatchesProviderAchievementFromAchievementWindow()
    {
        Assert.True(AchievementGuideWindow.IsPreferredGuideEntry(entry, 0, "ACH_COLLECT_ALL"));
        Assert.False(AchievementGuideWindow.IsPreferredGuideEntry(entry, 0, "ACH_OTHER"));
    }

    [Fact]
    public void IsPreferredGuideEntry_PrioritizesDatabaseIdDuringGuideReload()
    {
        Assert.True(AchievementGuideWindow.IsPreferredGuideEntry(entry, 42, "ACH_OTHER"));
        Assert.False(AchievementGuideWindow.IsPreferredGuideEntry(entry, 99, "ACH_COLLECT_ALL"));
    }
}
