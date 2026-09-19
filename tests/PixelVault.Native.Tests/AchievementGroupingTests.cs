using System.Collections.Generic;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class AchievementGroupingTests
{
    [Fact]
    public void BuildAchievementGroups_UsesBaseFallbackAndCalculatesIndependentPercentages()
    {
        var groups = AchievementsInfoWindow.BuildAchievementGroups(new[]
        {
            Row("Base one", "", 0, true),
            Row("Base two", "Base Game", 0, false),
            Row("DLC one", "Frozen Wilds", 20, true),
            Row("DLC two", "Frozen Wilds", 20, true),
            Row("DLC three", "Frozen Wilds", 20, false)
        });

        Assert.Collection(groups,
            group =>
            {
                Assert.Equal("Base Game", group.Name);
                Assert.Equal(2, group.Total);
                Assert.Equal(1, group.Earned);
                Assert.Equal(50, group.Percent);
            },
            group =>
            {
                Assert.Equal("Frozen Wilds", group.Name);
                Assert.Equal(3, group.Total);
                Assert.Equal(2, group.Earned);
                Assert.Equal(67, group.Percent);
            });
    }

    [Fact]
    public void BuildAchievementGroups_DoesNotClaimPercentageWhenProgressIsUnknown()
    {
        var groups = AchievementsInfoWindow.BuildAchievementGroups(new[]
        {
            new GameAchievementsFetchService.AchievementRow
            {
                Title = "Unknown",
                GroupName = "Expansion",
                GroupSortOrder = 10,
                ProgressKnown = false
            }
        });

        var group = Assert.Single(groups);
        Assert.False(group.ProgressKnown);
        Assert.Null(group.Percent);
    }

    [Fact]
    public void BuildAchievementGridSections_PutsRemainingAchievementsBeforeObtainedAchievements()
    {
        var sections = AchievementsInfoWindow.BuildAchievementGridSections(new[]
        {
            Row("Obtained one", "Base Game", 0, true),
            Row("Remaining one", "Base Game", 0, false),
            Row("Obtained two", "Base Game", 0, true),
            Row("Remaining two", "Base Game", 0, false)
        });

        Assert.Collection(sections,
            section =>
            {
                Assert.Equal("not-obtained", section.Key);
                Assert.Equal("Not obtained", section.Title);
                Assert.Equal(2, section.Rows.Count);
                Assert.All(section.Rows, row => Assert.False(row.Unlocked));
            },
            section =>
            {
                Assert.Equal("obtained", section.Key);
                Assert.Equal("Obtained", section.Title);
                Assert.Equal(2, section.Rows.Count);
                Assert.All(section.Rows, row => Assert.True(row.Unlocked));
            });
    }

    [Fact]
    public void BuildAchievementGridSections_KeepsEmptyRemainingSectionWhenGroupIsComplete()
    {
        var sections = AchievementsInfoWindow.BuildAchievementGridSections(new[]
        {
            Row("Complete", "Base Game", 0, true)
        });

        Assert.Equal(2, sections.Count);
        var section = sections[0];
        Assert.Equal("not-obtained", section.Key);
        Assert.Empty(section.Rows);
    }

    [Fact]
    public void BuildAchievementGridSections_UsesProgressUnknownWhenNoPlayerProgressExists()
    {
        var section = Assert.Single(AchievementsInfoWindow.BuildAchievementGridSections(new[]
        {
            new GameAchievementsFetchService.AchievementRow { Title = "Unknown" }
        }));

        Assert.Equal("unknown", section.Key);
        Assert.Equal("Progress unknown", section.Title);
    }

    static GameAchievementsFetchService.AchievementRow Row(
        string title,
        string group,
        int groupOrder,
        bool unlocked)
    {
        return new GameAchievementsFetchService.AchievementRow
        {
            Title = title,
            GroupName = group,
            GroupSortOrder = groupOrder,
            ProgressKnown = true,
            Unlocked = unlocked
        };
    }
}
