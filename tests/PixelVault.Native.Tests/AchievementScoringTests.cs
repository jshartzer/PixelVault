using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.Data.Sqlite;
using PixelVaultNative;
using Xunit;
using static PixelVaultNative.Tests.AchievementCollectionAccessTests;

namespace PixelVaultNative.Tests;

public sealed class AchievementScoringTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "PixelVault-Scores-" + Guid.NewGuid().ToString("N"));
    AchievementGuideService Store() => new(root);
    public void Dispose() { try { Directory.Delete(root, true); } catch (IOException) { } }

    static GameAchievementsFetchService.AchievementRow Achievement(double? rarity = 3, bool earned = true,
        string id = "A", string game = "1", string provider = "steam") => new()
    {
        Provider = provider, ProviderGameId = game, ProviderAchievementId = id,
        Title = "Achievement " + id, ProgressKnown = true, Unlocked = earned,
        GlobalUnlockPercent = rarity, UnlockUtcTicks = earned ? DateTime.UtcNow.AddDays(-1).Ticks : 0
    };

    [Theory]
    [InlineData(100d, 5)] [InlineData(75.001, 5)] [InlineData(75d, 10)]
    [InlineData(50.001, 10)] [InlineData(50d, 15)] [InlineData(25.001, 15)]
    [InlineData(25d, 20)] [InlineData(10.001, 20)] [InlineData(10d, 25)]
    [InlineData(5.001, 25)] [InlineData(5d, 30)] [InlineData(2.001, 30)]
    [InlineData(2d, 35)] [InlineData(1.001, 35)] [InlineData(1d, 40)]
    [InlineData(.100001, 40)] [InlineData(.1, 45)] [InlineData(.01, 45)]
    [InlineData(.009999, 50)] [InlineData(0d, 50)]
    [InlineData(null, 5)] [InlineData(-1d, 5)] [InlineData(101d, 5)]
    [InlineData(double.NaN, 5)] [InlineData(double.PositiveInfinity, 5)]
    public void Bands_HandleExactBoundariesAndUnavailableValues(double? rarity, int points) =>
        Assert.Equal(points, AchievementScoring.Points(rarity));

    [Fact]
    public void Awards_AreDurableFrozenAndIdempotentWhileLockedValuesRefresh()
    {
        var service = Store();
        var earned = Achievement(.1);
        var locked = Achievement(80, false, "B");
        service.SyncAchievementScores(" Alice ", new[] { earned, locked, earned });
        Assert.Equal(45, earned.AwardedPoints);
        Assert.Null(locked.AwardedPoints);
        service = Store();
        earned = Achievement(90); locked = Achievement(.001, false, "B");
        service.SyncAchievementScores("alice", new[] { earned, locked });
        Assert.Equal(5, earned.Points);
        Assert.Equal(45, earned.AwardedPoints);
        Assert.Equal(50, locked.Points);
        var totals = Assert.Single(service.LoadAchievementScoreTotals("ALICE")).Value;
        Assert.Equal(45, totals.Earned);
        Assert.Equal(0, totals.ProvisionalAwards);
        var stats = AchievementCollectionStats.Build(new[] { earned, locked }, DateTime.UtcNow.Ticks, totals);
        Assert.Equal(45, stats.EarnedPoints); Assert.Equal(95, stats.AvailablePoints);
    }

    [Fact]
    public void ProvisionalAwards_FinalizeExactlyOnceAndMissingRarityNeverDowngradesKnownValues()
    {
        var service = Store(); var row = Achievement(null);
        service.SyncAchievementScores("alice", new[] { row });
        Assert.Equal(5, row.AwardedPoints); Assert.True(row.AwardProvisional);
        Assert.Equal(1, service.LoadAchievementScoreTotals("alice")["steam:1"].ProvisionalAwards);
        row.GlobalUnlockPercent = .001;
        service.SyncAchievementScores("alice", new[] { row });
        Assert.Equal(50, row.AwardedPoints); Assert.False(row.AwardProvisional);
        row.GlobalUnlockPercent = null;
        service.SyncAchievementScores("alice", new[] { row });
        Assert.Equal(50, row.Points); Assert.False(row.PointsProvisional);
        row.GlobalUnlockPercent = 95;
        service.SyncAchievementScores("alice", new[] { row });
        Assert.Equal(5, row.Points); Assert.Equal(50, row.AwardedPoints);
    }

    [Fact]
    public void Awards_AreIsolatedByAccountProviderAndGameAndRequireKnownProgress()
    {
        var service = Store();
        var row = Achievement(); service.SyncAchievementScores("alice", new[] { row });
        var otherAccount = Achievement(.001, false); service.SyncAchievementScores("bob", new[] { otherAccount });
        Assert.Null(otherAccount.AwardedPoints);
        var otherProvider = Achievement(null, true, provider: "retroachievements");
        var otherGame = Achievement(.1, true, game: "2");
        var unknown = Achievement(.001, id: "unknown"); unknown.ProgressKnown = false;
        service.SyncAchievementScores("alice", new[] { otherProvider, otherGame, unknown });
        Assert.Equal(5, otherProvider.AwardedPoints); Assert.True(otherProvider.AwardProvisional);
        Assert.Equal(45, otherGame.AwardedPoints); Assert.Null(unknown.AwardedPoints);
        var anonymous = Achievement(.001, id: "anonymous"); service.SyncAchievementScores(" ", new[] { anonymous });
        Assert.Null(anonymous.AwardedPoints);
        Assert.Empty(service.LoadAchievementScoreTotals("bob"));
        Assert.Equal(3, service.LoadAchievementScoreTotals("alice").Count);
    }

    [Fact]
    public void Awards_SurviveRelockedOrRemovedAchievementsAndMetadataSync()
    {
        var service = Store(); var row = Achievement(.1);
        var definition = Assert.Single(service.SyncDefinitionsAndLoadGuides("pv-game", new[] { row }));
        service.SaveGuide(new AchievementGuideEdit { AchievementId = definition.AchievementId, GuideText = "Keep my guide." });
        service.SyncAchievementScores("alice", new[] { row });
        var locked = Achievement(90, false);
        service.SyncAchievementScores("alice", new[] { locked });
        Assert.Equal(45, locked.AwardedPoints);
        service.SyncDefinitionsAndLoadGuides("pv-game", new[] { Achievement(80, false, "new") });
        var totals = service.LoadAchievementScoreTotals("alice")["steam:1"];
        Assert.Equal(45, totals.Earned);
        Assert.Equal("Keep my guide.", service.LoadGuideEntry("steam", "1", "A").GuideText);
        var stats = AchievementCollectionStats.Build(Array.Empty<GameAchievementsFetchService.AchievementRow>(), DateTime.UtcNow.Ticks, totals);
        Assert.Equal(45, stats.EarnedPoints); Assert.Equal(45, stats.AvailablePoints);
    }

    [Fact]
    public async Task ConcurrentObservations_CreateOnlyOneAward()
    {
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => Store().SyncAchievementScores("alice", new[] { Achievement(.1) }))));
        Assert.Equal(45, Store().LoadAchievementScoreTotals("alice")["steam:1"].Earned);
    }

    [Fact]
    public async Task Hub_BackfillsLegacySnapshotsAndPreservesAwardsAcrossRefreshAndCacheLoss()
    {
        var dir = Path.Combine(root, "achievement-hub"); var token = CancellationToken.None;
        var legacy = new AchievementHubStore(dir);
        await legacy.SaveGameAsync("steam", "alice", "1", new() { Rows = new() { Achievement(.1) } }, token);
        var store = new AchievementHubStore(dir, Store());
        var loaded = await store.LoadAsync("steam", "alice", token);
        Assert.Equal(45, loaded.Games["1"].Rows[0].AwardedPoints);
        var light = Achievement(null); light.Title = "Refreshed title";
        await store.SaveGameAsync("steam", "alice", "1", new() { NotificationOnly = true, Rows = new() { light } }, token);
        await store.SaveGameAsync("steam", "alice", "1", new() { Rows = new() { Achievement(95) } }, token);
        loaded = await store.LoadAsync("steam", "alice", token);
        Assert.Equal(5, loaded.Games["1"].Rows[0].Points);
        Assert.Equal(45, loaded.Games["1"].Rows[0].AwardedPoints);
        var json = File.ReadAllText(Assert.Single(Directory.GetFiles(dir, "*.json")));
        Assert.DoesNotContain("AwardedPoints", json); // SQLite alone owns awards.
        foreach (var path in Directory.GetFiles(dir, "*.json")) File.Delete(path);
        loaded = await store.LoadAsync("steam", "alice", token);
        var games = new[] { new AchievementHubGame { Provider = "steam", Id = "1", Title = "Game" } };
        var collection = AchievementHubCollection.Build(games.Concat(games), new Dictionary<string, AchievementHubStore.AccountData> { ["steam"] = loaded });
        Assert.Equal(45, collection.EarnedPoints); Assert.Equal(1, collection.LibraryGames);
        Assert.Empty(loaded.Games);
    }

    [Fact]
    public async Task Hub_RarityBackfillFinalizesProvisionalAwardsWithoutDoubleAwarding()
    {
        var store = new AchievementHubStore(Path.Combine(root, "achievement-hub"), Store());
        await store.SaveGameAsync("steam", "alice", "1", new() { NotificationOnly = true, Rows = new() { Achievement(null) } }, default);
        await store.ApplyRarityAsync("steam", "alice", "1", new Dictionary<string, double> { ["A"] = .001 }, default);
        await store.ApplyRarityAsync("steam", "alice", "1", new Dictionary<string, double> { ["A"] = 99 }, default);
        var saved = (await store.LoadAsync("steam", "alice", default)).Games["1"];
        Assert.Equal(50, saved.Rows[0].AwardedPoints); Assert.False(saved.Rows[0].AwardProvisional);
        Assert.Equal(50, saved.ScoreTotals.Earned);
    }

    [Fact]
    public void Migration_AddsScoreColumnsWithoutLosingAuthoredGuides()
    {
        var service = Store();
        var entry = service.SyncDefinitionsAndLoadGuides("pv-game", new[] { Achievement() }).Single();
        service.SaveGuide(new AchievementGuideEdit { AchievementId = entry.AchievementId, GuideText = "Preserved." });
        using (var connection = new SqliteConnection("Data Source=" + service.DatabasePath))
        {
            connection.Open();
            foreach (var column in new[] { "score_points", "score_rarity_percent", "score_rule_version", "score_updated_utc_ticks", "score_provisional" })
            {
                using var command = connection.CreateCommand();
                command.CommandText = "ALTER TABLE achievement_definition DROP COLUMN " + column;
                command.ExecuteNonQuery();
            }
        }
        service = Store(); var row = Achievement(); service.SyncAchievementScores("alice", new[] { row });
        Assert.Equal(30, row.AwardedPoints);
        Assert.Equal("Preserved.", service.LoadGuideEntry("steam", "1", "A").GuideText);
    }

    [Fact]
    public void AccountScopedGameRefresh_PersistsScoresBeforeRenderingAndRetainsFrozenSummary()
    {
        OnSta(() =>
        {
            var results = new Queue<GameAchievementsFetchService.FetchResult>(new[] { Result(Achievement(.1)), Result(Achievement(95)) });
            var window = CreateWindow(() => Task.FromResult(results.Dequeue()), Store());
            typeof(AchievementsInfoWindow).GetField("_scoreSteamAccount", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, "alice");
            var priorContext = SynchronizationContext.Current;
            SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(window.Dispatcher));
            try
            {
                for (var i = 0; i < 2; i++)
                {
                    var task = (Task)typeof(AchievementsInfoWindow).GetMethod("LoadAsync", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null)!;
                    var frame = new System.Windows.Threading.DispatcherFrame();
                    _ = task.ContinueWith(_ => window.Dispatcher.BeginInvoke(new Action(() => frame.Continue = false)));
                    System.Windows.Threading.Dispatcher.PushFrame(frame);
                    task.GetAwaiter().GetResult();
                    Assert.Contains("45 / 45 PV pts", Field<TextBlock>(window, "_earnedSummary").Text);
                    Assert.Equal(45, Store().LoadAchievementScoreTotals("alice")["steam:1"].Earned);
                }
            }
            finally { window.Close(); SynchronizationContext.SetSynchronizationContext(priorContext); }
        });
    }

    [Fact]
    public void MonthlyPoints_UseLocalUnlockDatesAndFrozenAwardsRatherThanImportTime()
    {
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
        var now = new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);
        var start = AchievementScoring.MonthStartUtc(now, zone);
        var first = Achievement(.001, id: "first"); first.UnlockUtcTicks = start.Ticks;
        var recent = Achievement(3, id: "recent"); recent.UnlockUtcTicks = now.Ticks;
        var previous = Achievement(.1, id: "previous"); previous.UnlockUtcTicks = start.AddTicks(-1).Ticks;
        var future = Achievement(.001, id: "future"); future.UnlockUtcTicks = now.AddTicks(1).Ticks;
        var undated = Achievement(null, id: "undated"); undated.UnlockUtcTicks = 0;
        var service = Store();
        service.SyncAchievementScores("alice", new[] { first, recent, previous, future, undated });
        first.GlobalUnlockPercent = 95;
        service.SyncAchievementScores("alice", new[] { first });
        var totals = service.LoadAchievementScoreTotals("alice", now, zone)["steam:1"];
        Assert.Equal(new DateTime(2026, 10, 1, 5, 0, 0, DateTimeKind.Utc), start);
        Assert.Equal(80, totals.EarnedThisMonth);
        Assert.Equal(50, first.AwardedPoints);
        Assert.Empty(service.LoadAchievementScoreTotals("bob", now, zone));
        // Provider catalog changes cannot remove historical monthly awards.
        service.SyncDefinitionsAndLoadGuides("pv-game", new[] { Achievement(95, false, "replacement") });
        Assert.Equal(80, service.LoadAchievementScoreTotals("alice", now, zone)["steam:1"].EarnedThisMonth);
    }

    [Theory]
    [InlineData(2026, 11, 15, 2026, 11, 1, 5)] // Month starts before the autumn clock change.
    [InlineData(2027, 1, 1, 2026, 12, 1, 6)] // UTC New Year's Day is still December in Chicago.
    [InlineData(2026, 4, 15, 2026, 4, 1, 5)]
    public void MonthBoundary_UsesTheOffsetAtLocalMonthStart(int year, int month, int day,
        int startYear, int startMonth, int startDay, int utcHour)
    {
        var now = new DateTime(year, month, day, 0, 30, 0, DateTimeKind.Utc);
        Assert.Equal(new DateTime(startYear, startMonth, startDay, utcHour, 0, 0, DateTimeKind.Utc),
            AchievementScoring.MonthStartUtc(now, TimeZoneInfo.FindSystemTimeZoneById("America/Chicago")));
    }

    [Fact]
    public void MissingUnlockDates_CanBeFilledWithoutChangingFinalizedPoints()
    {
        var service = Store(); var row = Achievement(.1); row.UnlockUtcTicks = 0;
        service.SyncAchievementScores("alice", new[] { row });
        var now = new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc);
        var zone = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
        Assert.Equal(0, service.LoadAchievementScoreTotals("alice", now, zone)["steam:1"].EarnedThisMonth);
        row.UnlockUtcTicks = new DateTime(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc).Ticks;
        row.GlobalUnlockPercent = 95;
        service.SyncAchievementScores("alice", new[] { row });
        Assert.Equal(45, row.AwardedPoints);
        Assert.Equal(45, service.LoadAchievementScoreTotals("alice", now, zone)["steam:1"].EarnedThisMonth);
    }

    [Fact]
    public void CollectionSummary_IsScopedDeduplicatedAndChoosesOnlyValidEarnedRarity()
    {
        var game = new AchievementHubGame { Provider = "steam", Id = "1", Title = "Game" };
        var hiddenGame = new AchievementHubGame { Provider = "steam", Id = "2", Title = "Outside library" };
        var rare = Achievement(.005, id: "rare"); rare.IconUrlColor = "https://example.com/rare.png";
        var locked = Achievement(.000001, false, "locked");
        var unknown = Achievement(0, id: "unknown"); unknown.ProgressKnown = false;
        var invalid = Achievement(double.NaN, id: "invalid");
        var accounts = new Dictionary<string, AchievementHubStore.AccountData> { ["steam"] = new() { Games = new()
        {
            ["1"] = new() { Rows = new() { rare, locked, unknown, invalid }, ScoreTotals = new() { Earned = 75, EarnedThisMonth = 25 } },
            ["2"] = new() { Rows = new() { Achievement(0, game: "2") }, ScoreTotals = new() { Earned = 50, EarnedThisMonth = 50 } }
        } } };
        var collection = AchievementHubCollection.Build(new[] { game, game }, accounts);
        Assert.Same(rare, collection.RarestEarned.Row);
        Assert.Equal(25, collection.PointsThisMonth);
        Assert.Equal(75, collection.EarnedPoints);
        Assert.Equal("https://example.com/rare.png", collection.RarestEarned.Row.IconUrlColor);
        var signature = collection.Signature;
        accounts["steam"].Games["1"].ScoreTotals.EarnedThisMonth = 30;
        Assert.NotEqual(signature, AchievementHubCollection.Build(new[] { game }, accounts).Signature);
        var noCache = AchievementHubCollection.Build(new[] { game }, new Dictionary<string, AchievementHubStore.AccountData>
        { ["steam"] = new() { Scores = new Dictionary<string, AchievementScoreTotals> { ["steam:1"] = new() { Earned = 75, EarnedThisMonth = 25 } } } });
        Assert.Equal(25, noCache.PointsThisMonth);
        Assert.Null(noCache.RarestEarned);
        var empty = AchievementHubCollection.Build(new[] { hiddenGame }, new Dictionary<string, AchievementHubStore.AccountData>());
        Assert.Null(empty.RarestEarned); Assert.Equal(0, empty.PointsThisMonth);
    }

    [Fact]
    public void RarestFeature_PreservesItsTargetAcrossFiltersAndFailureAndReloadsAfterReset()
    {
        OnSta(() =>
        {
            var game = new AchievementHubGame { Provider = "steam", Id = "1", Title = "Game" };
            var row = Achievement(.005); var window = new AchievementHubWindow(null!);
            AchievementDetailWindow? opened = null;
            window.ShowAchievementDetail = detail => { opened = detail; detail.Close(); return true; };
            var collection = AchievementHubCollection.Build(new[] { game }, new Dictionary<string, AchievementHubStore.AccountData>
                { ["steam"] = new() { Games = new() { ["1"] = new() { Rows = new() { row } } } } });
            try
            {
                window.RenderRecent(new() { Collection = collection });
                var feature = Assert.IsType<Button>(Field<ContentControl>(window, "scoreRarest").Content);
                Field<TextBox>(window, "collectionSearch").Text = "no matches";
                window.RenderRecent(new() { Status = "Refresh failed" });
                Assert.Same(feature, Field<ContentControl>(window, "scoreRarest").Content);
                feature.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Contains(row.Title, opened!.Title);
                window.ResetCollection();
                Assert.IsType<TextBlock>(Field<ContentControl>(window, "scoreRarest").Content);
                window.RenderRecent(new() { Collection = collection });
                Assert.IsType<Button>(Field<ContentControl>(window, "scoreRarest").Content);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void GridAndListCards_ShowSavedAwardsAndExplainProvisionalPointsAccessibly()
    {
        OnSta(() =>
        {
            var row = Achievement(.1); Store().SyncAchievementScores("alice", new[] { row });
            var window = CreateWindow(() => Task.FromResult(Result(row)));
            try
            {
                foreach (var name in new[] { "BuildAchievementGridCard", "BuildAchievementRowCard" })
                {
                    var card = (Border)typeof(AchievementsInfoWindow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!
                        .Invoke(window, new object[] { row, "PixelVault.Tests" })!;
                    card.Measure(new Size(name.Contains("Grid") ? 164 : 680, 220));
                    card.Arrange(new Rect(new Point(), card.DesiredSize));
                    Assert.Contains("45 PV pts earned", AutomationProperties.GetName(card));
                    Assert.Contains("do not change", (string)card.ToolTip);
                }
                var provisional = Achievement(null, id: "provisional"); Store().SyncAchievementScores("bob", new[] { provisional });
                var chip = AchievementCollectionUi.ScoreChip(provisional);
                Assert.Contains("provisional", AutomationProperties.GetName(chip));
                Assert.Contains("finalizes", (string)chip.ToolTip);
            }
            finally { window.Close(); }
        });
    }
}
