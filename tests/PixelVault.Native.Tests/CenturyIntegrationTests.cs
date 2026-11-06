using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using PixelVault.Tracking;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public class CenturyIntegrationTests
{
    static readonly CenturyAchievementDefinition Road = CenturyAchievementCatalog.Find("steam", "1850570", CenturyAchievementCatalog.DeathStrandingRoadsId);
    static IEnumerable<DependencyObject> Tree(DependencyObject root)
    {
        yield return root;
        foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            foreach (var item in Tree(child)) yield return item;
    }

    [Fact]
    public void TrackerLinkUsesCenturyIdentityWithoutChangingProgressOrNotes()
    {
        var root = Path.Combine(Path.GetTempPath(), "pv-century-link-" + Guid.NewGuid().ToString("N"));
        var store = new AchievementTrackingStore(root);
        store.AddDefinition(new() { Id = "roads", Provider = "steam", GameId = "1850570", GameTitle = Road.GameTitle,
            Title = "Road Building", Items = new() { new() { Id = "one", Name = "Road 1" } } });
        store.SetManualProgress("roads", "alice", "one", 1); store.SaveNote("roads", "alice", "one", "Keep this note");
        var history = store.History("roads", "alice").Count;
        var row = AchievementsInfoWindow.CenturyPresentation(Road);
        Assert.True(row.HasStableAchievementIdentity); Assert.False(row.HasStableProviderIdentity);
        store.SetAchievementLink("roads", row);
        Assert.Equal(Road.Id, store.Definitions().Single(d => d.Id == "roads").AchievementId);
        var state = Assert.Single(store.Load("roads", "alice"));
        Assert.Equal(1, state.ManualCurrent); Assert.Equal("Keep this note", state.Note); Assert.Equal(history, store.History("roads", "alice").Count);
        Assert.Contains(store.AchievementChoices("alice", null), r => r.CenturyDefinitionId == Road.Id);
        row.ProviderGameId = "42"; Assert.False(row.HasStableAchievementIdentity);
        Assert.Throws<InvalidDataException>(() => store.SetAchievementLink("roads", row));
    }

    [Fact]
    public void TrackersAlwaysUseUnlockedCenturyArtAndTimelineRequiresAConfirmedDatedUnlock() => AchievementCollectionAccessTests.OnSta(() =>
    {
        var row = AchievementsInfoWindow.CenturyPresentation(Road);
        var image = Tree(TrackerIconVisual.Create(null, row)).OfType<Image>().Last();
        var expected = new Image(); var earned = AchievementsInfoWindow.CenturyPresentation(Road); earned.ProgressKnown = earned.Unlocked = true;
        AchievementsInfoWindow.StartAchievementBadgeDownload(expected, earned, "test");
        var actualBitmap = Assert.IsAssignableFrom<BitmapSource>(image.Source); var expectedBitmap = Assert.IsAssignableFrom<BitmapSource>(expected.Source);
        Assert.Equal(expectedBitmap.PixelWidth, actualBitmap.PixelWidth);
        var actualPixels = new byte[actualBitmap.PixelWidth * actualBitmap.PixelHeight * 4]; var expectedPixels = new byte[actualPixels.Length];
        actualBitmap.CopyPixels(actualPixels, actualBitmap.PixelWidth * 4, 0); expectedBitmap.CopyPixels(expectedPixels, expectedBitmap.PixelWidth * 4, 0);
        Assert.True(actualPixels.SequenceEqual(expectedPixels));
        var game = new AchievementHubGame { Provider = "steam", Id = Road.GameId, Title = Road.GameTitle };
        var account = new AchievementHubStore.AccountData { Account = "alice", Games = new() { [Road.GameId] = new() { Rows = new() { row } } } };
        var accounts = new Dictionary<string, AchievementHubStore.AccountData> { ["steam"] = account };
        var now = DateTime.UtcNow;
        Assert.Empty(LibraryTimelineAchievement.Select(new[] { game }, accounts, DateTime.MinValue, DateTime.MaxValue, now.Ticks, TimeZoneInfo.Utc));
        row.ProgressKnown = row.Unlocked = true; row.UnlockUtcTicks = now.AddMinutes(-1).Ticks;
        var entry = Assert.Single(LibraryTimelineAchievement.Select(new[] { game }, accounts, DateTime.MinValue, DateTime.MaxValue, now.Ticks, TimeZoneInfo.Utc));
        Assert.Equal(Road.Id, entry.Row.CenturyDefinitionId);
        var tile = MainWindow.BuildLibraryTimelineAchievementTile(entry, 160, 256, "Today", () => { });
        Assert.Contains(Tree(tile).OfType<Image>(), i => i.Source is BitmapSource);
    });
}
