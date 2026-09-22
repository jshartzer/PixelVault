using System.Collections.Generic;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class LibraryAchievementsPreviewTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("Base Game")]
    [InlineData(" base game ")]
    public void BaseGameCompletion_IgnoresLockedAndUnknownDlc(string? group)
    {
        GameAchievementsFetchService.AchievementRow[] rows =
        {
            new() { GroupName = group, ProgressKnown = true, Unlocked = true },
            new() { GroupName = "Expansion", ProgressKnown = true, Unlocked = false },
            new() { GroupName = "Bonus Pack", ProgressKnown = false }
        };
        Assert.True(MainWindow.LibraryGameProfileAchievementsAreFullyUnlocked(rows));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public void BaseGameCompletion_RequiresEveryBaseAchievement(bool known, bool unlocked)
    {
        GameAchievementsFetchService.AchievementRow[] rows =
        {
            new() { ProgressKnown = true, Unlocked = true },
            new() { GroupName = "Base Game", ProgressKnown = known, Unlocked = unlocked }
        };
        Assert.False(MainWindow.LibraryGameProfileAchievementsAreFullyUnlocked(rows));
    }

    [Fact]
    public void BaseGameCompletion_RequiresNonemptyBaseGame()
    {
        Assert.False(MainWindow.LibraryGameProfileAchievementsAreFullyUnlocked(null!));
        Assert.False(MainWindow.LibraryGameProfileAchievementsAreFullyUnlocked(Array.Empty<GameAchievementsFetchService.AchievementRow>()));
        Assert.False(MainWindow.LibraryGameProfileAchievementsAreFullyUnlocked(new GameAchievementsFetchService.AchievementRow[]
        {
            new() { GroupName = "Expansion", ProgressKnown = true, Unlocked = true }
        }));
    }

    [Fact]
    public void BuildRecentAchievementsPreviewRows_ReturnsFiveMostRecentUnlockedRows()
    {
        var rows = new List<GameAchievementsFetchService.AchievementRow>
        {
            new() { Title = "Locked", ProgressKnown = true, Unlocked = false, UnlockUtcTicks = 0 },
            new() { Title = "Unknown", ProgressKnown = false, Unlocked = false, UnlockUtcTicks = 0 },
            new() { Title = "Six", ProgressKnown = true, Unlocked = true, UnlockUtcTicks = 6 },
            new() { Title = "Two", ProgressKnown = true, Unlocked = true, UnlockUtcTicks = 2 },
            new() { Title = "Five", ProgressKnown = true, Unlocked = true, UnlockUtcTicks = 5 },
            new() { Title = "One", ProgressKnown = true, Unlocked = true, UnlockUtcTicks = 1 },
            new() { Title = "Four", ProgressKnown = true, Unlocked = true, UnlockUtcTicks = 4 },
            new() { Title = "Three", ProgressKnown = true, Unlocked = true, UnlockUtcTicks = 3 }
        };

        var recent = MainWindow.BuildRecentAchievementsPreviewRows(rows, 5);

        Assert.Collection(
            recent,
            row => Assert.Equal("Six", row.Title),
            row => Assert.Equal("Five", row.Title),
            row => Assert.Equal("Four", row.Title),
            row => Assert.Equal("Three", row.Title),
            row => Assert.Equal("Two", row.Title));
    }

    [Fact]
    public void BuildRecentAchievementsPreviewRows_PlacesUndatedUnlockedRowsAfterDatedRows()
    {
        var rows = new List<GameAchievementsFetchService.AchievementRow>
        {
            new() { Title = "No Date A", ProgressKnown = true, Unlocked = true, UnlockUtcTicks = 0 },
            new() { Title = "Dated", ProgressKnown = true, Unlocked = true, UnlockUtcTicks = 10 },
            new() { Title = "No Date B", ProgressKnown = true, Unlocked = true, UnlockUtcTicks = 0 }
        };

        var recent = MainWindow.BuildRecentAchievementsPreviewRows(rows, 5);

        Assert.Equal("Dated", recent[0].Title);
        Assert.Contains(recent, row => row.Title == "No Date A");
        Assert.Contains(recent, row => row.Title == "No Date B");
    }

    [Fact]
    public void LibraryGameProfileCanOpenAchievementGuide_RequiresStableProviderIdentity()
    {
        var rows = new List<GameAchievementsFetchService.AchievementRow>
        {
            new() { Title = "Display-only row" },
            new()
            {
                Title = "Guide-compatible row",
                Provider = GameAchievementsFetchService.RetroAchievementsProvider,
                ProviderGameId = "123",
                ProviderAchievementId = "456"
            }
        };

        Assert.True(MainWindow.LibraryGameProfileCanOpenAchievementGuide(rows));
        Assert.False(MainWindow.LibraryGameProfileCanOpenAchievementGuide(rows.Take(1)));
    }
}
