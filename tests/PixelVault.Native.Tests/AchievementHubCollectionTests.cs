using System.Windows;
using System.Windows.Controls;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class AchievementHubCollectionTests
{
    static readonly long Now = DateTime.UtcNow.Ticks;
    static AchievementHubGame Game(string id, string title, string provider = "steam") => new() { Id = id, Title = title, Provider = provider };
    static GameAchievementsFetchService.AchievementRow Row(string game, string id, bool earned = false, long ticks = 0, double? percent = null,
        string provider = "steam", string description = "") => new()
    {
        Provider = provider, ProviderGameId = game, ProviderAchievementId = id, Title = id, Description = description,
        ProgressKnown = true, Unlocked = earned, UnlockUtcTicks = ticks, GlobalUnlockPercent = percent
    };
    static AchievementHubStore.Snapshot Snap(params GameAchievementsFetchService.AchievementRow[] rows) => new() { UpdatedUtc = Now - 10, Rows = rows.ToList() };

    static AchievementHubCollection Fixture() => AchievementHubCollection.Build(
        new[] { Game("1", "Alpha"), Game("1", "Alpha duplicate folder"), Game("2", "Beta"), Game("3", "No Cheevos"), Game("4", "Never checked"),
            Game("1", "Alpha", "retroachievements"), Game("9", "Disconnected", "other") },
        new Dictionary<string, AchievementHubStore.AccountData>
        {
            ["steam"] = new() { Games = new()
            {
                ["1"] = Snap(Row("1", "Dragon", true, Now - 100, 0.005, description: "Slay the dragon"), Row("1", "Coin", false, 0, 50),
                    Row("1", "Dragon", true, Now - 100, 0.005), Row("1", "Old", true, 0, 7)),
                ["2"] = Snap(Row("2", "Crown", true, Now - 50, 2), Row("2", "Gem", false, 0, null)),
                ["3"] = Snap()
            } },
            ["retroachievements"] = new() { Games = new() { ["1"] = Snap(Row("1", "Dragon", true, Now - 10, 30, "retroachievements")) } }
        });

    [Fact]
    public void MissableFilter_ComposesWithStatusGameAndRarityWithoutChangingTotals()
    {
        var c = Fixture();
        c.Entries.Single(e => e.Game.Key == "steam:1" && e.Row.Title == "Coin").Row.IsMissable = true;
        c.Entries.Single(e => e.Game.Key == "steam:2" && e.Row.Title == "Crown").Row.IsMissable = true;
        Assert.Equal(2, c.Query(new() { Missable = AchievementMissableFilter.Missable }, Now).Count);
        Assert.Equal("Coin", Assert.Single(c.Query(new() { Missable = AchievementMissableFilter.Missable,
            Status = AchievementHubStatusFilter.Unearned, GameKey = "steam:1", Rarity = AchievementHubRarityFilter.Common }, Now)).Row.Title);
        Assert.Equal("Crown", Assert.Single(c.Query(new() { Missable = AchievementMissableFilter.Missable,
            Status = AchievementHubStatusFilter.Earned }, Now)).Row.Title);
        Assert.Equal(4, c.Query(new() { Missable = AchievementMissableFilter.NotMarkedMissable }, Now).Count);
        Assert.Equal(4, c.Earned);
        Assert.Equal(2, c.Unearned);
    }

    [Fact]
    public void MissableMarkers_OnlyAppearOnUnfinishedAchievementsInBothHubViews()
    {
        AchievementCollectionAccessTests.OnSta(() =>
        {
            var locked = Row("1", "Locked"); locked.IsMissable = true;
            var earned = Row("1", "Earned", true); earned.IsMissable = true;
            var unmarked = Row("1", "Unmarked");
            var window = new AchievementHubWindow(null!);
            try
            {
                AchievementHubCollection Collection(params GameAchievementsFetchService.AchievementRow[] rows) =>
                    AchievementHubCollection.Build(new[] { Game("1", "Test Game") },
                        new Dictionary<string, AchievementHubStore.AccountData> { ["steam"] = new() { Games = new() { ["1"] = Snap(rows) } } });
                window.RenderRecent(new AchievementHubState { Collection = Collection(locked, earned, unmarked) });
                void CheckMarkers()
                {
                    var entries = Descendants(window.Content).OfType<Button>().Where(b => (b.ToolTip as string)?.StartsWith("Test Game") == true).ToList();
                    var marked = entries.Single(b => (b.ToolTip as string)!.Contains("Locked"));
                    Assert.Single(Descendants(marked.Content).OfType<Border>(), b => b.Tag as string == "AchievementMissable");
                    Assert.Single(Descendants(marked.Content).OfType<TextBlock>(), t => t.Text == "Missable");
                    foreach (var other in entries.Where(b => b != marked))
                    {
                        Assert.DoesNotContain(Descendants(other.Content).OfType<FrameworkElement>(), e => e.Tag as string == "AchievementMissable");
                        Assert.DoesNotContain("Missable", AutomationName(other));
                    }
                }
                CheckMarkers();
                var boxes = Descendants(window.Content).OfType<ComboBox>().ToList();
                boxes.Single(b => AutomationName(b) == "Grid or list view").SelectedIndex = 1;
                CheckMarkers();
                boxes.Single(b => AutomationName(b) == "Filter by missable status").SelectedIndex = 1;
                Assert.Equal(2, window.CollectionMatches.Count);
                boxes.Single(b => AutomationName(b) == "Filter by earned status").SelectedIndex = 2;
                Assert.Single(window.CollectionMatches);
                var newlyEarned = Row("1", "Locked", true); newlyEarned.IsMissable = true;
                window.RenderRecent(new AchievementHubState { Collection = Collection(newlyEarned, earned, unmarked) });
                Assert.Empty(window.CollectionMatches);
                boxes.Single(b => AutomationName(b) == "Filter by earned status").SelectedIndex = 0;
                Assert.Equal(2, window.CollectionMatches.Count);
                Assert.DoesNotContain(Descendants(window.Content).OfType<FrameworkElement>(), e => e.Tag as string == "AchievementMissable");
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void Build_DeduplicatesFoldersAndRowsKeepsProviderEditionsAndCountsCoverage()
    {
        var c = Fixture();
        Assert.Equal(6, c.Entries.Count);
        Assert.Equal(2, c.Entries.Count(e => e.Row.ProviderAchievementId == "Dragon"));
        Assert.Equal(5, c.LibraryGames);
        Assert.Equal(4, c.GamesWithProgress);
        Assert.Equal(1, c.GamesWithoutAchievements);
        Assert.Equal(1, c.GamesWithoutProgress);
        Assert.Equal(4, c.Earned); Assert.Equal(2, c.Unearned);
        Assert.Equal(new[] { "retroachievements:1", "steam:1", "steam:2" }, c.Games.Select(g => g.Key));
    }

    [Fact]
    public void Query_SearchesNameDescriptionAndGameAndFiltersWithoutChangingTotals()
    {
        var c = Fixture();
        Assert.Single(c.Query(new() { Search = "SLAY" }, Now));
        Assert.Equal(2, c.Query(new() { Search = "beta" }, Now).Count);
        Assert.Equal(2, c.Query(new() { Search = "dragon" }, Now).Count);
        Assert.Equal(4, c.Query(new() { Status = AchievementHubStatusFilter.Earned }, Now).Count);
        Assert.Equal(new[] { "Coin", "Gem" }, c.Query(new() { Status = AchievementHubStatusFilter.Unearned, Sort = AchievementHubSort.Name }, Now).Select(e => e.Row.Title));
        Assert.Equal(3, c.Query(new() { GameKey = "steam:1" }, Now).Count);
        Assert.Equal("Dragon", Assert.Single(c.Query(new() { Rarity = AchievementHubRarityFilter.UberRare }, Now)).Row.Title);
        Assert.Equal("Gem", Assert.Single(c.Query(new() { Rarity = AchievementHubRarityFilter.Unavailable }, Now)).Row.Title);
        Assert.Equal(new[] { "Dragon", "Coin" }, c.Query(new() { Rarity = AchievementHubRarityFilter.Common }, Now).Select(e => e.Row.Title));
        Assert.Equal(4, c.Earned);
    }

    [Fact]
    public void Query_SortsWithMissingValuesLastAndDeterministicTies()
    {
        var c = Fixture();
        Assert.Equal(new[] { "Dragon", "Crown", "Old", "Dragon", "Coin", "Gem" },
            c.Query(new() { Sort = AchievementHubSort.RarestFirst }, Now).Take(6).Select(e => e.Row.Title));
        Assert.Equal("Gem", c.Query(new() { Sort = AchievementHubSort.MostCommonFirst }, Now).Last().Row.Title);
        var newest = c.Query(new() { Sort = AchievementHubSort.NewestEarned }, Now);
        Assert.Equal(new[] { "retroachievements:1", "steam:2", "steam:1" }, newest.Take(3).Select(e => e.Game.Key));
        Assert.Equal(new[] { "Coin", "Gem", "Old" }, newest.Skip(3).Select(e => e.Row.Title));
        Assert.Equal(new[] { "Dragon", "Crown", "Dragon" }, c.Query(new() { Sort = AchievementHubSort.OldestEarned }, Now).Take(3).Select(e => e.Row.Title));
        Assert.Equal(new[] { "retroachievements:1", "steam:1", "steam:1", "steam:1", "steam:2", "steam:2" },
            c.Query(new() { Sort = AchievementHubSort.Game }, Now).Select(e => e.Game.Key));
        Assert.Equal(new[] { "Dragon", "Old" }, c.Query(new() { Sort = AchievementHubSort.Game, GameKey = "steam:1" }, Now).Skip(1).Select(e => e.Row.Title));
        Assert.Equal(new[] { "steam:1Coin", "steam:2Crown", "retroachievements:1Dragon", "steam:1Dragon", "steam:2Gem", "steam:1Old" },
            c.Query(new() { Sort = AchievementHubSort.Name }, Now).Select(e => e.Game.Key + e.Row.Title));

        var mixed = AchievementHubCollection.Build(
            new[] { Game("1", "Zebra"), Game("2", "Alpha") },
            new Dictionary<string, AchievementHubStore.AccountData>
            {
                ["steam"] = new() { Games = new()
                {
                    ["1"] = Snap(Row("1", "Apple", false, 0, 10), Row("1", "Mango", false, 0, 10)),
                    ["2"] = Snap(Row("2", "Banana", false, 0, 10))
                } }
            });
        Assert.Equal(new[] { "Apple", "Banana", "Mango" },
            mixed.Query(new() { Sort = AchievementHubSort.RarestFirst }, Now).Select(e => e.Row.Title));
    }

    [Fact]
    public void Window_PagesFiltersSwitchesViewsAndOpensTheGame()
    {
        AchievementCollectionAccessTests.OnSta(() =>
        {
            AchievementHubGame? opened = null;
            var window = new AchievementHubWindow(null!, open: (_, game) => opened = game);
            try
            {
                var game = Game("1", "Big Game");
                var rows = Enumerable.Range(0, 250).Select(i => Row("1", "A" + i.ToString("000"), i % 2 == 0, i % 2 == 0 ? Now - i : 0, i)).ToArray();
                var collection = AchievementHubCollection.Build(new[] { game },
                    new Dictionary<string, AchievementHubStore.AccountData> { ["steam"] = new() { Games = new() { ["1"] = Snap(rows) } } });
                window.RenderRecent(new AchievementHubState { Status = "Fixture", Collection = collection });
                Assert.Equal(250, window.CollectionMatches.Count);
                Assert.Equal(AchievementHubWindow.CollectionPageSize, window.CollectionShown);
                var more = Descendants(window.Content).OfType<Button>().Single(b => b.Content as string == "Show more");
                more.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(200, window.CollectionShown);
                window.RenderRecent(new AchievementHubState { Status = "Same data", Collection = AchievementHubCollection.Build(new[] { game },
                    new Dictionary<string, AchievementHubStore.AccountData> { ["steam"] = new() { Games = new() { ["1"] = Snap(rows) } } }) });
                Assert.Equal(200, window.CollectionShown);
                var firstEntry = Descendants(window.Content).OfType<Button>().First(b => (b.ToolTip as string)?.StartsWith("Big Game") == true);
                var restamped = Snap(rows); restamped.UpdatedUtc = Now;
                window.RenderRecent(new AchievementHubState { Status = "Re-saved", Collection = AchievementHubCollection.Build(new[] { game },
                    new Dictionary<string, AchievementHubStore.AccountData> { ["steam"] = new() { Games = new() { ["1"] = restamped } } }) });
                Assert.Equal(200, window.CollectionShown);
                Assert.Same(firstEntry, Descendants(window.Content).OfType<Button>().First(b => (b.ToolTip as string)?.StartsWith("Big Game") == true));
                var changed = rows.Select(r => r).ToArray();
                changed[0] = Row("1", "A000", false, 0, 0);
                window.RenderRecent(new AchievementHubState { Status = "Changed", Collection = AchievementHubCollection.Build(new[] { game },
                    new Dictionary<string, AchievementHubStore.AccountData> { ["steam"] = new() { Games = new() { ["1"] = Snap(changed) } } }) });
                Assert.NotSame(firstEntry, Descendants(window.Content).OfType<Button>().First(b => (b.ToolTip as string)?.StartsWith("Big Game") == true));
                window.RenderRecent(new AchievementHubState { Status = "Back", Collection = AchievementHubCollection.Build(new[] { game },
                    new Dictionary<string, AchievementHubStore.AccountData> { ["steam"] = new() { Games = new() { ["1"] = Snap(rows) } } }) });

                var boxes = Descendants(window.Content).OfType<ComboBox>().ToList();
                boxes.Single(b => AutomationName(b) == "Filter by earned status").SelectedIndex = 1;
                Assert.Equal(125, window.CollectionMatches.Count);
                Assert.Equal(100, window.CollectionShown);
                boxes.Single(b => AutomationName(b) == "Grid or list view").SelectedIndex = 1;
                Assert.Contains(Descendants(window.Content).OfType<TextBlock>(), t => t.Text == "Rarity unavailable" || t.Text.EndsWith("% of players"));
                window.ShowAchievementDetail = w =>
                {
                    var openGame = Descendants(w.Content).OfType<Button>().Single(b => b.Content as string == "Open game achievements");
                    openGame.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    return true;
                };
                var entry = Descendants(window.Content).OfType<Button>().First(b => (b.ToolTip as string)?.StartsWith("Big Game") == true);
                entry.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Same(game, opened);
                Assert.Contains(Descendants(window.Content).OfType<TextBlock>(), t => t.Text.Contains("Totals ignore filters") && t.Text.Contains("125 of 250"));
            }
            finally { window.Close(); }
        });
    }

    static string AutomationName(DependencyObject d) => System.Windows.Automation.AutomationProperties.GetName(d);

    static IEnumerable<DependencyObject> Descendants(object root)
    {
        if (root is not DependencyObject parent) yield break;
        foreach (var child in LogicalTreeHelper.GetChildren(parent).OfType<DependencyObject>())
        {
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }
}
