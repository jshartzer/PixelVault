using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class TrackerDashboardSnapshotTests : IDisposable
{
    readonly string root = Path.Combine(Path.GetTempPath(), "pv-dashboard-snapshot-" + Guid.NewGuid().ToString("N"));
    const string Id = "snapshot-test", Account = "player";
    public void Dispose() { SqliteConnection.ClearAllPools(); Directory.Delete(root, true); }

    AchievementTrackingStore CreateStore()
    {
        var store = new AchievementTrackingStore(root);
        store.AddDefinition(new() { Id = Id, Provider = "steam", GameId = "42", GameTitle = "Game", Title = "Checklist",
            Items = new() { new() { Id = "a", Name = "First" }, new() { Id = "b", Name = "Second", Archived = true } } });
        store.SetManualProgress(Id, Account, "a", 1);
        store.SaveNote(Id, Account, "a", "Keep this note");
        var file = Path.Combine(root, "progress.json");
        File.WriteAllText(file, "{}");
        new TrackerRefreshService(store).Configure(Id, Account, JsonFileTrackerSource.AdapterId, "1", file);
        store.SaveAutomation(Id, Account, store.Source(Id, Account).Id, TrackerAutomationMode.Periodic, 5, true);
        return store;
    }

    static string Json<T>(T value) => JsonSerializer.Serialize(value, AchievementTrackingCatalog.Json);
    static Dictionary<string, string> Accounts(string account) => new() { ["steam"] = account };

    [Fact]
    public void SnapshotMatchesLegacyReadsIncludingArchivedItemsNotesSourceAndAutomation()
    {
        var store = CreateStore();
        var beforeHistory = store.History(Id, Account).Count;
        var read = Assert.Single(store.ReadDashboard(Accounts(Account), "steam", "42"));
        Assert.Equal(Json(store.Definitions().Single(d => d.Id == Id)), Json(read.Definition));
        Assert.Equal(Json(store.Load(Id, Account)), Json(read.States));
        Assert.Equal(Json(store.Source(Id, Account)), Json(read.Source));
        Assert.Equal(Json(store.Automation(Id, Account).Settings), Json(read.Automation));
        Assert.Equal(Json(store.Automation(Id, Account).State), Json(read.AutomationState));
        Assert.Equal(beforeHistory, store.History(Id, Account).Count);
        Assert.Empty(store.ReadDashboard(Accounts(Account), "retroachievements", "42"));
    }

    [Fact]
    public void SubsequentSnapshotsObserveAnotherWriterAndDoNotLeakAccounts()
    {
        var store = CreateStore();
        var first = Assert.Single(store.ReadDashboard(Accounts(Account), "steam", "42"));
        var writer = new AchievementTrackingStore(root);
        writer.SetManualProgress(Id, Account, "a", 0);
        writer.RenameTracker(Id, "Renamed");
        writer.RemoveSource(Id, Account);
        var next = Assert.Single(store.ReadDashboard(Accounts(Account), "steam", "42"));
        Assert.Equal(1, first.States[0].Current);
        Assert.Equal(0, next.States[0].Current);
        Assert.Equal("Renamed", next.Definition.Title);
        Assert.Null(next.Source);
        Assert.Equal(TrackerAutomationMode.Manual, next.Automation.Mode);
        foreach (var account in new[] { "other", "" })
        {
            var other = Assert.Single(store.ReadDashboard(Accounts(account), "steam", "42"));
            Assert.All(other.States, state => { Assert.Null(state.Current); Assert.True(string.IsNullOrEmpty(state.Note)); });
            Assert.Null(other.Source);
            Assert.Equal(TrackerAutomationMode.Manual, other.Automation.Mode);
        }
    }
}
