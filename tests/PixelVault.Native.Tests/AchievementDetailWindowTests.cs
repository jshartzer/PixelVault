using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class AchievementDetailWindowTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "PixelVault-Detail-" + Guid.NewGuid().ToString("N"));
    public void Dispose() { try { if (Directory.Exists(dir)) Directory.Delete(dir, true); } catch (IOException) { } }

    [Theory]
    [InlineData("https://steamcdn-a.akamaihd.net/steamcommunity/public/images/apps/2139460/abc.jpg",
        "https://shared.fastly.steamstatic.com/community_assets/images/apps/2139460/abc.jpg|https://steamcdn-a.akamaihd.net/steamcommunity/public/images/apps/2139460/abc.jpg")]
    [InlineData("https://shared.fastly.steamstatic.com/community_assets/images/apps/5/x.jpg",
        "https://shared.fastly.steamstatic.com/community_assets/images/apps/5/x.jpg")]
    [InlineData("https://steamcdn-a.akamaihd.net/steamcommunity/public/images/apps/43000/", "")]
    [InlineData("https://media.retroachievements.org/Badge/609830.png", "https://media.retroachievements.org/Badge/609830.png")]
    public void SteamBadgeUrls_PreferTheCurrentCommunityAssetsHost(string url, string expected)
    {
        Assert.Equal(expected, string.Join("|", GameAchievementsFetchService.SteamAchievementIconCandidates(url)));
    }

    [Fact]
    public void SteamBadgeUrls_ResolveNewFetchesToTheWorkingHostAndDropEmptyNames()
    {
        Assert.Equal("https://shared.fastly.steamstatic.com/community_assets/images/apps/7/abc.jpg",
            GameAchievementsFetchService.ResolveSteamAchievementIconUrl(7, "abc"));
        Assert.Equal("https://shared.fastly.steamstatic.com/community_assets/images/apps/7/abc.jpg",
            GameAchievementsFetchService.ResolveSteamAchievementIconUrl(7, "https://steamcdn-a.akamaihd.net/steamcommunity/public/images/apps/7/abc.jpg"));
        Assert.Null(GameAchievementsFetchService.ResolveSteamAchievementIconUrl(7, "https://steamcdn-a.akamaihd.net/steamcommunity/public/images/apps/7/"));
    }

    static GameAchievementsFetchService.AchievementRow Row(string id, bool earned, double? pct, long ticks = 0) => new()
    {
        Provider = "steam", ProviderGameId = "55", ProviderAchievementId = id, Title = id, Description = id + " description",
        ProgressKnown = true, Unlocked = earned, UnlockUtcTicks = ticks, GlobalUnlockPercent = pct
    };

    [Fact]
    public void Window_ShowsProgressRarityContextAndGuideAndManagesPins()
    {
        AchievementCollectionAccessTests.OnSta(() =>
        {
            var guides = new AchievementGuideService(dir);
            var earnedAt = DateTime.UtcNow.AddDays(-2).Ticks;
            var rows = new List<GameAchievementsFetchService.AchievementRow>
            {
                Row("Rare", true, 2.5, earnedAt), Row("Common", true, 60, earnedAt), Row("Grind", false, 30), Row("Other", false, 50)
            };
            rows[2].CounterCurrent = 37; rows[2].CounterMinimum = 0; rows[2].CounterTarget = 100;
            var entry = guides.SyncDefinitionsAndLoadGuides("pv-game", rows).Single(e => e.ProviderAchievementId == "Rare");
            guides.SaveGuide(new AchievementGuideEdit { AchievementId = entry.AchievementId, GuideText = "Beat the boss without healing.",
                SourceUrl = "https://example.com/guide", SourceTitle = "Example guide", Tags = "boss", IsMissable = true });
            var game = new AchievementHubGame { Provider = "steam", Id = "55", Title = "Test Game", Folder = new LibraryFolderInfo { GameId = "pv-game" } };
            var hubPinned = false;

            var rare = new AchievementDetailWindow(null!, game, rows[0], rows, DateTime.UtcNow.Ticks, guides, () => hubPinned, () => hubPinned = !hubPinned);
            try
            {
                var texts = Descendants(rare.Content).OfType<TextBlock>().Select(t => t.Text).ToList();
                Assert.Contains(texts, t => t.StartsWith("Earned "));
                Assert.Contains(texts, t => t.StartsWith("2.5") && t.Contains("% of players") && t.Contains("Silver rarity"));
                Assert.Contains("#1 rarest of 2 in this game", texts);
                Assert.Contains("2 of 4 earned · 50%", texts);
                Assert.Contains("Missable", texts);
                Assert.Contains("boss", texts);
                Assert.Contains(Descendants(rare.Content).OfType<TextBox>(), b => b.Text == "Beat the boss without healing.");
                Assert.Contains(Descendants(rare.Content).OfType<TextBlock>(), t => t.Inlines.OfType<Hyperlink>().Any(h => h.NavigateUri == new Uri("https://example.com/guide")));
                Assert.Contains(Descendants(rare.Content).OfType<Button>(), b => b.Content as string == "Edit guide");
                var nextUp = Descendants(rare.Content).OfType<Button>().Single(b => b.Content as string == "Add to Next Up");
                Assert.Equal(Visibility.Collapsed, nextUp.Visibility);
                var gamePin = Descendants(rare.Content).OfType<Button>().Single(b => b.Content as string == "Pin game to hub");
                gamePin.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.True(hubPinned);
                Assert.Equal("Unpin game from hub", gamePin.Content);
            }
            finally { rare.Close(); }

            var other = Row("Fresh", false, null);
            var unsynced = new AchievementDetailWindow(null!, game, rows[2], rows.Append(other), 0, guides);
            try
            {
                var texts = Descendants(unsynced.Content).OfType<TextBlock>().Select(t => t.Text).ToList();
                Assert.Contains("Not earned yet", texts);
                Assert.Contains(texts, t => t.StartsWith("37 / 100 · 37"));
                Assert.Contains("No guide written for this achievement yet.", texts);
                Assert.Contains(Descendants(unsynced.Content).OfType<Button>(), b => b.Content as string == "Add guide");
                Assert.DoesNotContain(Descendants(unsynced.Content).OfType<Button>(), b => (b.Content as string)?.Contains("hub") == true);
                var nextUp = Descendants(unsynced.Content).OfType<Button>().Single(b => b.Content as string == "Add to Next Up");
                nextUp.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.Equal("Remove from Next Up", nextUp.Content);
                Assert.Contains(guides.LoadPinnedAchievements("steam", "55"), e => e.ProviderAchievementId == "Grind");
            }
            finally { unsynced.Close(); }

            Assert.Null(guides.LoadGuideEntry("steam", "55", "Fresh"));
            var fresh = new AchievementDetailWindow(null!, game, other, rows.Append(other), 0, guides);
            try
            {
                Assert.Contains(Descendants(fresh.Content).OfType<TextBlock>(), t => t.Text == "Rarity unavailable");
                Descendants(fresh.Content).OfType<Button>().Single(b => b.Content as string == "Add to Next Up")
                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Assert.NotNull(guides.LoadGuideEntry("steam", "55", "Fresh"));
                Assert.Contains(guides.LoadPinnedAchievements("steam", "55"), e => e.ProviderAchievementId == "Fresh");
            }
            finally { fresh.Close(); }
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        });
    }

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
