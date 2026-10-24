using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class AchievementTrackingTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "PixelVault-Tracking-" + Guid.NewGuid().ToString("N"));
    AchievementTrackingStore Store() => new(dir);
    public void Dispose() { SqliteConnection.ClearAllPools(); if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    static string Serialize(AchievementTrackerSnapshot snapshot) => JsonSerializer.Serialize(snapshot, AchievementTrackingCatalog.Json);
    static AchievementTrackerSnapshot Snapshot(int known = 3) => new()
    {
        TrackerId = AchievementTrackingCatalog.DeathStrandingId, Provider = "steam", GameId = "1850570", Account = "player-one",
        Source = "Test save snapshot", Evidence = "Test fixture", ObservedUtcTicks = DateTime.UtcNow.AddMinutes(-5).Ticks,
        Items = AchievementTrackingCatalog.DeathStranding().Items.Take(known).Select((item, index) => new AchievementTrackerProgress
        { ItemId = item.Id, Current = index == 0 ? 1 : 0, Availability = index < 2 ? TrackerAvailability.Available : TrackerAvailability.Locked }).ToList()
    };

    [Fact]
    public void Builtin_ContainsThe108ExtractedTargetsAndStableScopedIdentities()
    {
        var definition = AchievementTrackingCatalog.DeathStranding();
        AchievementTrackingCatalog.Validate(definition);
        Assert.Equal("1850570", definition.GameId); Assert.Equal("062", definition.AchievementId);
        Assert.Equal(108, definition.Items.Count);
        Assert.Equal(108, definition.Items.Select(i => i.Id).Distinct().Count());
        Assert.Equal(108, definition.Items.Select(i => i.Identifiers["ItemType"] + ":" + i.Identifiers["ItemID"]).Distinct().Count());
        Assert.Contains(definition.Items, i => i.Name == "Ladder" && i.Identifiers["ItemID"] == "216");
        Assert.Contains(definition.Items, i => i.Name == "Roadster: Long Range");
        Assert.All(definition.Items, i => Assert.Equal(1, i.Target));
    }

    [Fact]
    public void UnobservedProgress_IsUnknownAndNeverInventsZeroOrCompletion()
    {
        var states = Store().Load(AchievementTrackingCatalog.DeathStrandingId, "player-one");
        Assert.Equal(108, states.Count);
        Assert.All(states, state => { Assert.Null(state.Current); Assert.False(state.Complete); Assert.False(state.IsKnown); Assert.Equal("Not checked", state.Status); });
    }

    [Fact]
    public void Imports_PersistAndRemainAccountIsolated()
    {
        var store = Store(); var snapshot = Snapshot();
        Assert.Equal(3, store.Import(Serialize(snapshot), "player-one"));
        var states = Store().Load(snapshot.TrackerId, "player-one");
        Assert.Equal(3, states.Count(s => s.IsKnown)); Assert.Single(states, s => s.Complete);
        Assert.Single(states, s => s.Status == "Ready"); Assert.Single(states, s => s.Status == "Locked");
        Assert.All(Store().Load(snapshot.TrackerId, "another-player"), state => Assert.Null(state.Current));
        Assert.Equal("Test save snapshot", states.First(s => s.IsKnown).Source);
    }

    [Fact]
    public void NewImports_PreserveManualCountsAndNotesAndResetReturnsToImportedCount()
    {
        var store = Store(); var snapshot = Snapshot(); var id = snapshot.Items[1].ItemId;
        store.Import(Serialize(snapshot), "player-one"); store.SetManualProgress(snapshot.TrackerId, "player-one", id, 1);
        store.SaveNote(snapshot.TrackerId, "player-one", id, "Made this at Lake Knot.");
        snapshot.ObservedUtcTicks += TimeSpan.FromMinutes(1).Ticks;
        store.Import(Serialize(snapshot), "player-one");
        var state = Store().Load(snapshot.TrackerId, "player-one").Single(s => s.Item.Id == id);
        Assert.True(state.Complete); Assert.True(state.IsManual); Assert.Equal(0, state.ReportedCurrent); Assert.Equal("Made this at Lake Knot.", state.Note);
        store.SetManualProgress(snapshot.TrackerId, "player-one", id, null);
        state = Store().Load(snapshot.TrackerId, "player-one").Single(s => s.Item.Id == id);
        Assert.False(state.Complete); Assert.False(state.IsManual); Assert.Equal("Made this at Lake Knot.", state.Note);
    }

    [Fact]
    public void RepeatedAndOlderSnapshots_CannotRegressObservations()
    {
        var store = Store(); var snapshot = Snapshot();
        var json = Serialize(snapshot); Assert.Equal(3, store.Import(json, "player-one")); Assert.Equal(0, store.Import(json, "player-one"));
        snapshot.ObservedUtcTicks -= TimeSpan.FromDays(1).Ticks; snapshot.Items[0].Current = 0;
        Assert.Equal(0, store.Import(Serialize(snapshot), "player-one"));
        Assert.True(store.Load(snapshot.TrackerId, "player-one").First().Complete);
    }

    [Theory]
    [InlineData("account")][InlineData("game")][InlineData("provider")][InlineData("duplicate")][InlineData("unknown")]
    [InlineData("negative")][InlineData("future")][InlineData("version")][InlineData("checklist")]
    public void InvalidImports_AreRejectedWithoutPartialWrites(string change)
    {
        var store = Store(); var snapshot = Snapshot();
        switch (change)
        {
            case "account": snapshot.Account = "wrong"; break;
            case "game": snapshot.GameId = "1190460"; break;
            case "provider": snapshot.Provider = "retroachievements"; break;
            case "duplicate": snapshot.Items.Add(snapshot.Items[0]); break;
            case "unknown": snapshot.Items[2].ItemId = "unknown"; break;
            case "negative": snapshot.Items[2].Current = -1; break;
            case "future": snapshot.ObservedUtcTicks = DateTime.UtcNow.AddDays(1).Ticks; break;
            case "version": snapshot.Version = 2; break;
        }
        Assert.Throws<InvalidDataException>(() => store.Import(Serialize(snapshot), "player-one", change == "checklist" ? "different" : snapshot.TrackerId));
        Assert.All(store.Load(AchievementTrackingCatalog.DeathStrandingId, "player-one"), state => Assert.Null(state.Current));
    }

    [Fact]
    public void CustomChecklists_SupportOtherGamesCountsNotesAndRetroAccountNormalization()
    {
        var store = Store();
        var definition = new AchievementTrackerDefinition
        {
            Id = "other-game", Provider = "retroachievements", GameId = "12", GameTitle = "Another game", AchievementId = "456",
            Title = "Collect the keys", Items = new() { new() { Id = "keys", Name = "Keys", Target = 5 } }
        };
        store.AddDefinition(definition); store.SetManualProgress(definition.Id, "Player", "keys", 4);
        Assert.False(Store().Load(definition.Id, "PLAYER").Single().Complete);
        store.SetManualProgress(definition.Id, "player", "keys", 5);
        Assert.True(Store().Load(definition.Id, "Player").Single().Complete);
        Assert.Null(Store().Load(definition.Id, "SomeoneElse").Single().Current);
        Assert.Equal("456", Store().Definitions().Single(d => d.Id == definition.Id).AchievementId);
    }

    [Fact]
    public void EmptyAccounts_CannotWriteOrImportProgress()
    {
        var store = Store(); var snapshot = Snapshot();
        Assert.Throws<InvalidDataException>(() => store.SetManualProgress(snapshot.TrackerId, "", snapshot.Items[0].ItemId, 1));
        Assert.Throws<InvalidDataException>(() => store.Import(Serialize(snapshot), ""));
        Assert.Throws<InvalidDataException>(() => store.SaveNote(snapshot.TrackerId, "", snapshot.Items[0].ItemId, "x"));
    }

    [Fact]
    public void PendingSeed_ImportsOnlyForMatchingAccountAndOnlyOnce()
    {
        var store = Store(); var snapshot = Snapshot(); Directory.CreateDirectory(store.ImportDirectory);
        File.WriteAllText(Path.Combine(store.ImportDirectory, "seed.json"), Serialize(snapshot));
        Assert.Equal(0, store.ImportPending(snapshot.TrackerId, "another-player"));
        Assert.Equal(3, store.ImportPending(snapshot.TrackerId, "player-one"));
        store.SetManualProgress(snapshot.TrackerId, "player-one", snapshot.Items[0].ItemId, 0);
        Assert.Equal(0, store.ImportPending(snapshot.TrackerId, "player-one"));
        Assert.False(store.Load(snapshot.TrackerId, "player-one").First().Complete);
    }

    [Fact]
    public void CorruptPendingFile_DoesNotBlockValidImportsOrSavedManualProgress()
    {
        var store = Store(); var snapshot = Snapshot(); Directory.CreateDirectory(store.ImportDirectory);
        File.WriteAllText(Path.Combine(store.ImportDirectory, "a-broken.json"), "{unfinished");
        File.WriteAllText(Path.Combine(store.ImportDirectory, "b-valid.json"), Serialize(snapshot));
        var failures = new List<string>();
        Assert.Equal(3, store.ImportPending(snapshot.TrackerId, "player-one", failures.Add)); Assert.Single(failures);
        store.SetManualProgress(snapshot.TrackerId, "player-one", snapshot.Items[1].ItemId, 1);
        Assert.Equal(0, store.ImportPending(snapshot.TrackerId, "player-one", failures.Add));
        Assert.Equal(2, store.Load(snapshot.TrackerId, "player-one").Count(s => s.Complete));
    }

    [Fact]
    public void TrackerWindow_FiltersWithoutChangingTotalsAndRendersAtMinimumSize()
    {
        var store = Store(); store.Import(Serialize(Snapshot()), "player-one");
        AchievementCollectionAccessTests.OnSta(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var window = new AchievementTrackerWindow(null!, store, "player-one", "", provider: "steam", gameId: "1850570", achievementId: "062");
            try
            {
                Pump(window.LoadDefinitionsAsync());
                var list = Field<ListBox>(window, "list"); var summary = Field<TextBlock>(window, "summary"); var filter = Field<ComboBox>(window, "filter");
                Assert.Equal("1 / 108 fabricated", summary.Text); Assert.Single(list.Items.Cast<ListBoxItem>());
                filter.SelectedIndex = 0; Assert.Equal(108, list.Items.Count); Assert.Equal("1 / 108 fabricated", summary.Text);
                Field<TextBox>(window, "search").Text = "Ladder"; Assert.Single(list.Items.Cast<ListBoxItem>());
                var content = (FrameworkElement)window.Content;
                var height = window.MinHeight - 48;
                content.Measure(new Size(732, height)); content.Arrange(new Rect(0, 0, 732, height)); content.UpdateLayout();
                Assert.True(list.ActualWidth > 250); Assert.True(list.ActualHeight > 50);
                Assert.Contains(Descendants(content).OfType<Button>(), button => Equals(button.Content, "New checklist"));
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void TrackingEntryPoints_ArePresentInHubAndAchievementDetails()
    {
        var store = Store();
        AchievementCollectionAccessTests.OnSta(() =>
        {
            var hub = new AchievementHubWindow(null!, trackingStore: store);
            var game = new AchievementHubGame { Provider = "steam", Id = "1850570", Title = "Death Stranding" };
            var row = new GameAchievementsFetchService.AchievementRow { Provider = "steam", ProviderGameId = "1850570", ProviderAchievementId = "062", Title = "Homo Faber" };
            var detail = new AchievementDetailWindow(null!, game, row, new[] { row }, 0, null!, trackingStore: store, trackingAccount: "player-one");
            var listWindow = AchievementCollectionAccessTests.CreateWindow(() => Task.FromResult(new GameAchievementsFetchService.FetchResult()));
            try
            {
                Assert.Contains(Descendants(hub.Content).OfType<Button>(), button => Equals(button.Content, "Trackers"));
                Assert.Contains(Descendants(detail.Content).OfType<Button>(), button => Equals(button.Content, "Open tracker"));
                typeof(AchievementsInfoWindow).GetField("_trackingStore", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(listWindow, store);
                var card = new Border(); listWindow.AttachTrackingMenu(card, row);
                Assert.Contains(card.ContextMenu.Items.OfType<MenuItem>(), item => Equals(item.Header, "Track progress…"));
            }
            finally { hub.Close(); detail.Close(); listWindow.Close(); }
        });
    }

    [Fact]
    public void ManualUiCheckOffAndNotes_PersistWithoutChangingProviderProgress()
    {
        var store = Store(); var snapshot = Snapshot(); store.Import(Serialize(snapshot), "player-one");
        var itemId = snapshot.Items[1].ItemId;
        AchievementCollectionAccessTests.OnSta(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var window = new AchievementTrackerWindow(null!, store, "player-one", "");
            try
            {
                Pump(window.LoadDefinitionsAsync());
                var item = Field<ListBox>(window, "list").Items.OfType<ListBoxItem>().Single();
                Field<TextBox>(window, "notes").Text = "Fabricated today.";
                Field<Button>(window, "saveNote").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                Pump(window.PendingEdit);
                Pump(Until(() => store.Load(snapshot.TrackerId, "player-one").Single(s => s.Item.Id == itemId).Note == "Fabricated today."));
                Assert.IsType<Border>(item.Content); Assert.Empty(Descendants(item).OfType<CheckBox>());
                var check = Descendants(item).OfType<ToggleButton>().Single();
                Assert.Equal(25, check.MinHeight); Assert.Equal(108, check.MinWidth);
                check.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                Pump(window.PendingEdit);
                Pump(Until(() => Field<TextBlock>(window, "summary").Text == "2 / 108 fabricated"));
                var saved = Store().Load(snapshot.TrackerId, "player-one").Single(s => s.Item.Id == itemId);
                Assert.True(saved.Complete); Assert.Equal(0, saved.ReportedCurrent); Assert.Equal("Fabricated today.", saved.Note);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void NewChecklistPicker_DisplaysGameNamesAndPreservesTheSelectedGame()
    {
        AchievementCollectionAccessTests.OnSta(() =>
        {
            var games = new[] { new AchievementHubGame { Provider = "steam", Id = "42", Title = "Other Game" } };
            var dialog = new AchievementTrackerEditWindow(null!, games, null!, null!);
            try
            {
                var choice = Descendants(dialog.Content).OfType<ComboBox>().Single(c => AutomationProperties.GetName(c) == "Checklist game");
                var selected = Assert.IsType<ComboBoxItem>(choice.SelectedItem);
                Assert.Equal("Other Game", selected.Content); Assert.Equal("42", Assert.IsType<AchievementHubGame>(selected.Tag).Id);
            }
            finally { dialog.Close(); }
        });
    }

    [Fact]
    public void OptionalLocalReview_RenderRealSeedWithoutChangingLiveStore()
    {
        var source = Environment.GetEnvironmentVariable("PIXELVAULT_TRACKING_REVIEW_SEED");
        var output = Environment.GetEnvironmentVariable("PIXELVAULT_TRACKING_REVIEW_OUTPUT");
        if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(output)) return;
        var json = File.ReadAllText(source); var snapshot = JsonSerializer.Deserialize<AchievementTrackerSnapshot>(json, AchievementTrackingCatalog.Json)!;
        var store = Store(); store.Import(json, snapshot.Account);
        Assert.Equal(35, store.Load(snapshot.TrackerId, snapshot.Account).Count(s => s.Complete));
        AchievementCollectionAccessTests.OnSta(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var window = new AchievementTrackerWindow(null!, store, snapshot.Account, "");
            try
            {
                Pump(window.LoadDefinitionsAsync()); Directory.CreateDirectory(output);
                foreach (var width in new[] { 1050, 780 })
                {
                    var root = (FrameworkElement)window.Content;
                    root.Measure(new Size(width - 48, 690)); root.Arrange(new Rect(0, 0, width - 48, 690)); root.UpdateLayout();
                    var bitmap = new RenderTargetBitmap(width, 738, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
                    var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                    using var file = File.Create(Path.Combine(output, "tracker-" + width + ".png")); encoder.Save(file);
                }
            }
            finally { window.Close(); }
        });
    }

    static T Field<T>(object obj, string name) => (T)obj.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(obj)!;
    static async Task Until(Func<bool> complete)
    {
        var timeout = DateTime.UtcNow.AddSeconds(4);
        while (!complete())
        {
            if (DateTime.UtcNow > timeout) throw new TimeoutException("Checklist UI did not finish saving.");
            await Task.Delay(20);
        }
    }
    static void Pump(Task task)
    {
        if (!task.IsCompleted)
        {
            var frame = new DispatcherFrame();
            var dispatcher = Dispatcher.CurrentDispatcher;
            _ = task.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)), TaskScheduler.Default);
            Dispatcher.PushFrame(frame);
        }
        task.GetAwaiter().GetResult();
    }
    static IEnumerable<DependencyObject> Descendants(object? value)
    {
        if (value is not DependencyObject node) yield break;
        yield return node;
        foreach (var child in LogicalTreeHelper.GetChildren(node)) foreach (var item in Descendants(child)) yield return item;
    }
}
