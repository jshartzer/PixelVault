using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class AchievementNotificationTests : IDisposable
{
    readonly string directory = Path.Combine(Path.GetTempPath(), "PixelVault-notifications-" + Guid.NewGuid());
    string StorePath => Path.Combine(directory, "notifications.json");
    AchievementNotifications Create() => new(StorePath);
    static GameAchievementsFetchService.FetchResult Progress(int unlocked, int total = 4) => new()
    {
        GameTitle = "Test game", Rows = Enumerable.Range(1, total).Select(i => new GameAchievementsFetchService.AchievementRow
        { Provider = "steam", ProviderGameId = "123", ProviderAchievementId = i.ToString(), Title = "Achievement " + i,
            ProgressKnown = true, Unlocked = i <= unlocked }).ToList()
    };
    [Fact] public void BaselineIsSilentAndPersistedAcrossRestarts()
    {
        var store = Create();
        Assert.Empty(store.Observe(Progress(2), "account")); store.Save();
        store = Create(); Assert.Empty(store.Observe(Progress(2), "account"));
        Assert.Contains("75%", Assert.Single(store.Observe(Progress(3), "account")).Message);
        store.Save(); Assert.Single(Create().Data.History);
        Assert.Empty(Create().Observe(Progress(3), "account"));
    }
    [Fact] public void MultipleThresholdsProduceOnePopupAndCompletionIsAlwaysIncluded()
    {
        var store = Create(); store.Data.Thresholds = new() { 25, 50 };
        store.Observe(Progress(0), "a");
        Assert.Equal("All achievements completed!", Assert.Single(store.Observe(Progress(4), "a")).Message);
        Assert.Equal(3, store.Data.Snapshots.Single().Value.Milestones.Count);
        Assert.Empty(store.Observe(Progress(4), "a"));
    }
    [Fact] public void ErrorsAndUnknownProgressDoNotDestroyBaseline()
    {
        var store = Create(); store.Observe(Progress(0), "a");
        var failed = Progress(2); failed.ProgressErrorMessage = "offline";
        Assert.Empty(store.Observe(failed, "a"));
        var unknown = Progress(2); unknown.Rows[0].ProgressKnown = false;
        Assert.Empty(store.Observe(unknown, "a"));
        Assert.Contains("50%", Assert.Single(store.Observe(Progress(2), "a")).Message);
    }
    [Fact] public void AccountsAndProvidersHaveSeparateBaselines()
    {
        var store = Create(); store.Observe(Progress(0), "a");
        Assert.Empty(store.Observe(Progress(2), "b"));
        var retro = Progress(2); foreach (var row in retro.Rows) row.Provider = "retroachievements";
        Assert.Empty(store.Observe(retro, "a"));
        Assert.Single(store.Observe(Progress(2), "a"));
    }
    [Fact] public void CatalogChangeAndRegressionDoNotRepeatMilestones()
    {
        var store = Create(); store.Observe(Progress(2), "a");
        Assert.Empty(store.Observe(Progress(2, 2), "a"));
        store.Observe(Progress(0, 2), "a");
        Assert.Empty(store.Observe(Progress(2, 2), "a"));
    }
    [Fact] public void IndividualUnlocksAreOptionalAndOnlyAnnouncedOnce()
    {
        var store = Create(); store.Data.Thresholds = new() { 100 }; store.Data.IndividualUnlocks = true;
        store.Observe(Progress(0), "a");
        Assert.Contains("Achievement unlocked", Assert.Single(store.Observe(Progress(1), "a")).Message);
        store.Observe(Progress(0), "a"); Assert.Empty(store.Observe(Progress(1), "a"));
    }
    [Fact] public void DisabledNotificationsDoNotAdvanceBaseline()
    {
        var store = Create(); store.Observe(Progress(0), "a"); store.Data.Enabled = false;
        Assert.Empty(store.Observe(Progress(2), "a"));
        store.Data.Enabled = true; Assert.Single(store.Observe(Progress(2), "a"));
    }
    [Fact] public void DuplicateConflictingRowsAndEmptyResultsAreIgnored()
    {
        var store = Create(); store.Observe(Progress(0), "a");
        var conflict = Progress(2); conflict.Rows.Add(Progress(0).Rows[0]);
        Assert.Empty(store.Observe(conflict, "a")); Assert.Empty(store.Observe(Progress(0, 0), "a"));
        Assert.Single(store.Observe(Progress(1), "a"));
    }
    [Theory]
    [InlineData(0, 0, 1920, 1080, true)]
    [InlineData(-8, -8, 1928, 1048, false)]
    [InlineData(0, 0, 1920, 1040, false)]
    [InlineData(-1, -1, 1921, 1081, true)]
    public void OnlyWindowsCoveringEntireMonitorAreFullscreen(int left, int top, int right, int bottom, bool expected)
    {
        Assert.Equal(expected, AchievementFullscreenGuard.CoversMonitor(
            new() { Left = left, Top = top, Right = right, Bottom = bottom }, new() { Right = 1920, Bottom = 1080 }));
    }
    [Fact]
    public void BackgroundCompletionLoadsSavedGroupsAndMarksBaseCompletionWithoutDlc()
    {
        var guides = new AchievementGuideService(directory);
        var initial = Progress(0, 72);
        var definitions = guides.SyncDefinitionsAndLoadGuides("game", initial.Rows);
        foreach (var entry in definitions.Where(e => int.Parse(e.ProviderAchievementId) > 52))
            guides.SaveGuide(new AchievementGuideEdit { AchievementId = entry.AchievementId, GroupName = "Expansion" });
        var fresh = Progress(52, 72);
        fresh.Rows[51].UnlockUtcTicks = DateTime.UtcNow.AddMinutes(-3).Ticks;
        var game = new GameIndexEditorRow { GameId = "game", SteamAppId = "123", PlatformLabel = "Steam" };
        Assert.Empty(MainWindow.MarkCompletedAchievementGames(fresh, new[] { game }));
        guides.LoadSavedAchievementGroups(fresh.Rows);
        Assert.Same(game, Assert.Single(MainWindow.MarkCompletedAchievementGames(fresh, new[] { game })));
        Assert.True(game.IsCompleted100Percent);
        Assert.Equal(fresh.Rows[51].UnlockUtcTicks, game.CompletedUtcTicks);
        Assert.Empty(MainWindow.MarkCompletedAchievementGames(fresh, new[] { game }));
    }
    [Fact]
    public void BackgroundCompletionDoesNotMarkOtherGamesOrProviders()
    {
        var games = new[] {
            new GameIndexEditorRow { SteamAppId = "999", PlatformLabel = "Steam" },
            new GameIndexEditorRow { SteamAppId = "123", RetroAchievementsGameId = "123", PlatformLabel = "Emulation" }
        };
        Assert.Empty(MainWindow.MarkCompletedAchievementGames(Progress(4), games));
        Assert.All(games, g => Assert.False(g.IsCompleted100Percent));
    }
    [Fact]
    public void BackgroundCompletionRejectsFailedUnknownAndMixedGameResults()
    {
        var game = new GameIndexEditorRow { SteamAppId = "123", PlatformLabel = "Steam" };
        var failed = Progress(4); failed.ProgressErrorMessage = "unavailable";
        Assert.Empty(MainWindow.MarkCompletedAchievementGames(failed, new[] { game }));
        var unknown = Progress(4); unknown.Rows[0].ProgressKnown = false;
        Assert.Empty(MainWindow.MarkCompletedAchievementGames(unknown, new[] { game }));
        var mixed = Progress(4); mixed.Rows[0].ProviderGameId = "999";
        Assert.Empty(MainWindow.MarkCompletedAchievementGames(mixed, new[] { game }));
        Assert.False(game.IsCompleted100Percent);
    }
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        if (Directory.Exists(directory)) Directory.Delete(directory, true);
    }
}
