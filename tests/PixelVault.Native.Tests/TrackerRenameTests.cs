using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using PixelVaultNative;
using Xunit;
using static PixelVaultNative.Tests.AchievementCollectionAccessTests;

namespace PixelVaultNative.Tests;

public sealed class TrackerRenameTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "PV-Rename-" + Guid.NewGuid().ToString("N"));
    AchievementTrackingStore Store() => new(dir);
    static AchievementTrackerDefinition Custom() => new() { Id = "rename.test", Provider = "steam", GameId = "42", GameTitle = "Fixture", Title = "Original title", AchievementId = "A", Items = new() { new() { Id = "one", Name = "Collect ten", Target = 10 } } };
    static void Pump(Task task)
    {
        var frame = new DispatcherFrame();
        var dispatcher = Dispatcher.CurrentDispatcher;_ = task.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)), TaskScheduler.Default);
        if (!task.IsCompleted) Dispatcher.PushFrame(frame);task.GetAwaiter().GetResult();
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void RenamingCustomAndBundledTrackersPreservesEverythingExceptTheDisplayName(bool bundled)
    {
        var store = Store();var d = bundled ? AchievementTrackingCatalog.DeathStranding() : Custom();if (!bundled) store.AddDefinition(d);
        var account = "player";var item = d.Items[0].Id;store.SetManualProgress(d.Id, account, item, 1);store.SaveNote(d.Id, account, item, "Keep this note");
        var service = new TrackerRefreshService(store);service.Configure(d.Id, account, "pixelvault.json-file", "1", Path.Combine(dir, "source.json"));
        store.SaveAutomation(d.Id, account, store.Source(d.Id, account).Id, TrackerAutomationMode.SaveChanges, 5, false);
        store.SetAchievementLink(d.Id, new GameAchievementsFetchService.AchievementRow { Provider = d.Provider, ProviderGameId = d.GameId, ProviderAchievementId = "LINKED", Title = "Linked achievement" });
        string Data()
        {
            using var c = new SqliteConnection("Data Source=" + store.DatabasePath);c.Open();var result = new List<string>();
            foreach (var table in new[] { "tracker_item_state", "tracker_history", "tracker_source", "tracker_automation", "tracker_link" })
            {
                using var q = c.CreateCommand();q.CommandText = "SELECT * FROM " + table + " ORDER BY 1,2";using var reader = q.ExecuteReader();
                while (reader.Read()) { var row = new object[reader.FieldCount];reader.GetValues(row);result.Add(JsonSerializer.Serialize(row)); }
            }
            return JsonSerializer.Serialize(result);
        }
        var before = Data();var original = store.Definitions().Single(x => x.Id == d.Id);var frozen = JsonSerializer.Serialize(original.Items);
        store.RenameTracker(d.Id, "  A clearer name  ", original.Title);
        var changed = Store().Definitions().Single(x => x.Id == d.Id);Assert.Equal("A clearer name", changed.Title);Assert.Equal("LINKED", changed.AchievementId);Assert.Equal(frozen, JsonSerializer.Serialize(changed.Items));Assert.Equal(before, Data());
        if (bundled) Assert.Equal(d.Title, AchievementTrackingCatalog.Builtins().Single(x => x.Id == d.Id).Title);
        else
        {
            using var c = new SqliteConnection("Data Source=" + store.DatabasePath);c.Open();using var q = c.CreateCommand();q.CommandText = "SELECT definition FROM tracker_definition WHERE id='rename.test'";
            Assert.Equal("A clearer name", JsonSerializer.Deserialize<AchievementTrackerDefinition>((string)q.ExecuteScalar()!)!.Title);
        }
    }
    [Theory] [InlineData("")] [InlineData("  ")] [InlineData("Line\nbreak")] [InlineData("Tab\tname")]
    public void InvalidNamesCannotChangeTheExistingTracker(string title)
    {
        var store = Store();store.AddDefinition(Custom());Assert.Throws<InvalidDataException>(() => store.RenameTracker("rename.test", title));Assert.Equal("Original title", store.Definitions().Single(x => x.Id == "rename.test").Title);
    }
    [Fact] public void OversizedMissingAndConcurrentRenamesAreRejected()
    {
        var store = Store();store.AddDefinition(Custom());Assert.Throws<InvalidDataException>(() => store.RenameTracker("rename.test", new string('a', 201)));
        Assert.Throws<InvalidDataException>(() => store.RenameTracker("missing", "New title"));store.RenameTracker("rename.test", "Other window", "Original title");
        Assert.Throws<InvalidOperationException>(() => store.RenameTracker("rename.test", "Stale edit", "Original title"));Assert.Equal("Other window", store.Definitions().Single(x => x.Id == "rename.test").Title);
    }
    [Theory] [InlineData(false)] [InlineData(true)]
    public void RenameMenuPreservesSelectionAndUnsavedDraftsAndSupportsCancellation(bool cancel) => OnSta(() =>
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));var store = Store();var d = Custom();store.AddDefinition(d);store.SetManualProgress(d.Id, "player", "one", 3);
        var w = new AchievementTrackerWindow(null!, store, "player", "", provider: "steam", gameId: "42", trackerId: d.Id);
        try
        {
            Pump(w.LoadDefinitionsAsync());var selector = Field<ComboBox>(w, "trackers");Field<ListBox>(w, "list").SelectedIndex = 0;
            var notes = Field<TextBox>(w, "notes");var count = Field<TextBox>(w, "current");notes.Text = "Unsaved note";count.Text = "7";
            w.ShowRename = dialog => { Assert.Equal("Original title", dialog.TrackerTitle);Field<TextBox>(dialog, "name").Text = "Renamed tracker";return !cancel; };
            var buttons = Tree((DependencyObject)w.Content).OfType<Button>();var manage = buttons.Single(b => Equals(b.Content, "Manage tracker…"));
            var rename = manage.ContextMenu!.Items.OfType<MenuItem>().Single(m => Equals(m.Header, "Rename tracker…"));rename.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Pump(w.PendingRename);
            Assert.Equal(cancel ? "Original title" : "Renamed tracker", store.Definitions().Single(x => x.Id == d.Id).Title);Assert.Equal(d.Id, ((AchievementTrackerDefinition)selector.SelectedItem).Id);
            Assert.Equal("Unsaved note", notes.Text);Assert.Equal("7", count.Text);Assert.Equal(3, store.Load(d.Id, "player").Single().Current);Assert.Equal("", store.Load(d.Id, "player").Single().Note);
        }
        finally { w.Close(); }
    });
    static IEnumerable<DependencyObject> Tree(DependencyObject root)
    {
        yield return root;if (root is Panel panel) foreach (UIElement child in panel.Children) foreach (var item in Tree(child)) yield return item;
    }
    public void Dispose() { SqliteConnection.ClearAllPools();if (Directory.Exists(dir)) Directory.Delete(dir, true); }
}
