using System.Collections.Generic;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class LibraryAchievementsPreviewTests
{
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
