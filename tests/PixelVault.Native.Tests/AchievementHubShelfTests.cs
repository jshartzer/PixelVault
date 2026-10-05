using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class AchievementHubShelfTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "PixelVault-HubPins-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    string PinFile => Path.Combine(dir, "pins.json");
    static AchievementHubGame Game(string id, string provider = "steam") => new() { Id = id, Provider = provider, Title = "Game " + id };

    [Fact]
    public void Pins_PersistInOrderIgnoreInvalidKeysAndRecoverFromCorruption()
    {
        var pins = new AchievementHubPins(PinFile);
        Assert.True(pins.Toggle("steam:20"));
        Assert.True(pins.Toggle("retroachievements:5"));
        Assert.True(pins.Toggle("steam:7"));
        Assert.False(pins.Toggle("retroachievements:5"));
        Assert.Throws<ArgumentException>(() => pins.Toggle("steam:abc"));
        Assert.Equal(new[] { "steam:20", "steam:7" }, new AchievementHubPins(PinFile).Keys);

        File.WriteAllText(PinFile, """{"Version":1,"Games":["steam:1","steam:1","steam:0","other:3",null,"retroachievements:9"]}""");
        Assert.Equal(new[] { "steam:1", "retroachievements:9" }, new AchievementHubPins(PinFile).Keys);
        File.WriteAllText(PinFile, "{ not json");
        Assert.Empty(new AchievementHubPins(PinFile).Keys);
    }

    [Fact]
    public void Pins_FailedSaveLeavesPinsUnchanged()
    {
        Directory.CreateDirectory(PinFile);
        var pins = new AchievementHubPins(PinFile);
        var error = Assert.ThrowsAny<Exception>(() => pins.Toggle("steam:1"));
        Assert.True(error is IOException || error is UnauthorizedAccessException);
        Assert.Empty(pins.Keys);
    }

    [Fact]
    public void Selection_TenRecentAndPinnedInPinOrderForLibraryGamesOfConnectedProviders()
    {
        var now = DateTime.UtcNow.Ticks;
        var recent = Enumerable.Range(1, 12).ToDictionary(i => i.ToString(), i => now - i * TimeSpan.TicksPerMinute);
        var data = new Dictionary<string, AchievementHubStore.AccountData>
        {
            ["steam"] = new() { Recent = recent, Games = new() { ["30"] = new() { UpdatedUtc = now, Rows = new() } } }
        };
        var games = Enumerable.Range(1, 12).Select(i => Game(i.ToString())).Append(Game("30")).Append(Game("4", "retroachievements")).ToList();
        Assert.Equal(Enumerable.Range(1, 10).Select(i => i.ToString()), AchievementHubService.SelectRecent(games, data).Select(c => c.Game.Id));

        var pinned = AchievementHubService.SelectPinned(games, new[] { "steam:30", "steam:missing", "retroachievements:4", "steam:2", "steam:30" }, data);
        Assert.Equal(new[] { "steam:30", "steam:2" }, pinned.Select(c => c.Game.Key));
        Assert.Equal(0, pinned[0].LastPlayedUtc);
        Assert.NotNull(pinned[0].Snapshot);
        Assert.Equal(recent["2"], pinned[1].LastPlayedUtc);
    }

    [Fact]
    public async Task Refresh_FetchesPinnedGamesThatAreNotRecent()
    {
        var store = new AchievementHubStore(Path.Combine(dir, "store"));
        var fetched = new List<string>();
        var service = new AchievementHubService(store,
            (_, _) => Task.FromResult("""{"response":{"game_count":0}}"""),
            (game, _, _) => { fetched.Add(game.Id); return Task.FromResult(new GameAchievementsFetchService.FetchResult { Rows = new() {
                new() { Provider = "steam", ProviderGameId = game.Id, ProviderAchievementId = "A", ProgressKnown = true, Unlocked = true, Title = "A" } } }); });
        var session = new AchievementHubSession { SteamAccount = "acct", SteamKey = "key", Games = new() { Game("8"), Game("9") }, Pinned = new() { "steam:9" } };
        AchievementHubState? final = null;
        await service.LoadAsync(session, true, s => final = s, CancellationToken.None);
        Assert.Equal(new[] { "9" }, fetched);
        var card = Assert.Single(final!.Pinned);
        Assert.Single(card.Snapshot!.Rows);
        Assert.Empty(final.Cards);
    }

    [Fact]
    public void Window_PinnedCoversAreLargerArrowsScrollAndPinsToggle()
    {
        AchievementCollectionAccessTests.OnSta(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext());
            var sizes = new Dictionary<string, Size>();
            var pins = new AchievementHubPins(PinFile);
            pins.Toggle("steam:1");
            var window = new AchievementHubWindow(null!, cover: (game, w, h) => { sizes[game.Key + (w > 150 ? ":pinned" : ":recent")] = new Size(w, h);
                return new Border { Width = w, Height = h }; }, pins: pins)
                { Width = 1040, Height = 780, Left = -20000, Top = -20000, ShowActivated = false, ShowInTaskbar = false };
            void Pump(int ms)
            {
                var until = DateTime.UtcNow.AddMilliseconds(ms);
                while (DateTime.UtcNow < until) Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            }
            try
            {
                var now = DateTime.UtcNow.Ticks;
                var cards = Enumerable.Range(1, 10).Select(i => new AchievementHubCard { Game = Game(i.ToString()), LastPlayedUtc = now - i }).ToList();
                window.Show();
                window.RenderRecent(new AchievementHubState { Status = "Fixture", Cards = cards, Pinned = new() { cards[0] } });
                window.UpdateLayout();

                Assert.True(sizes["steam:1:pinned"].Width > sizes["steam:1:recent"].Width * 1.6);
                Assert.True(sizes["steam:1:pinned"].Height > sizes["steam:1:recent"].Height * 1.6);
                var all = Descendants(window).ToList();
                Assert.Contains(all.OfType<TextBlock>(), t => t.Text == "Pinned games");
                var recentRight = all.OfType<Border>().Single(b => Name(b) == "Scroll right: Recently played");
                var recentLeft = all.OfType<Border>().Single(b => Name(b) == "Scroll left: Recently played");
                Assert.Equal(Visibility.Visible, recentRight.Visibility);
                Assert.Equal(Visibility.Collapsed, recentLeft.Visibility);
                Assert.Equal(Visibility.Collapsed, all.OfType<Border>().Single(b => Name(b) == "Scroll right: Pinned games").Visibility);

                var scroller = all.OfType<ScrollViewer>().Single(s => Name(s) == "Recently played");
                recentRight.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
                Pump(250);
                recentRight.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
                window.UpdateLayout();
                var offset = scroller.HorizontalOffset;
                Assert.True(offset > 0);
                Assert.Equal(Visibility.Visible, recentLeft.Visibility);
                Pump(100);
                Assert.Equal(offset, scroller.HorizontalOffset);

                window.RenderRecent(new AchievementHubState { Status = "Again", Cards = cards, Pinned = new() { cards[0] } });
                window.UpdateLayout();
                Assert.Equal(offset, Descendants(window).OfType<ScrollViewer>().Single(s => Name(s) == "Recently played").HorizontalOffset, 1);

                Button Card(string title) => Descendants(window).OfType<Button>().Single(b => (b.ToolTip as string) == "Open " + title + " achievements"
                    && Descendants(window).OfType<ScrollViewer>().Single(s => Name(s) == "Recently played").IsAncestorOf(b));
                var second = Card("Game 2");
                var fourth = Card("Game 4");
                cards[3] = new AchievementHubCard { Game = cards[3].Game, LastPlayedUtc = cards[3].LastPlayedUtc,
                    Snapshot = new() { Rows = new() { new() { ProgressKnown = true, Unlocked = true } } } };
                window.RenderRecent(new AchievementHubState { Status = "Again", Cards = cards, Pinned = new() { cards[0] } });
                window.UpdateLayout();
                Assert.Same(second, Card("Game 2"));
                Assert.NotSame(fourth, Card("Game 4"));
                Assert.Contains("1 / 1", Name(Card("Game 4")));

                var pinThird = Descendants(window).OfType<Button>().Single(b => Name(b) == "Pin Game 3");
                pinThird.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal(new[] { "steam:1", "steam:3" }, pins.Keys);
                Assert.Equal(2, Descendants(window).OfType<Button>().Count(b => Name(b) == "Unpin Game 1"));
                var menu = Descendants(window).OfType<Button>().First(b => (b.ToolTip as string) == "Open Game 3 achievements").ContextMenu;
                Assert.NotNull(menu);
            }
            finally { window.Close(); SynchronizationContext.SetSynchronizationContext(null); }
        });
    }

    static string Name(DependencyObject d) => System.Windows.Automation.AutomationProperties.GetName(d);

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
