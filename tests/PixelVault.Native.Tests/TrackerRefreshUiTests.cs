using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class TrackerRefreshUiTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "PV-RefreshUi-" + Guid.NewGuid().ToString("N"));
    readonly AchievementTrackingStore store;
    const string Id = "ui-checklist", Account = "player";
    public TrackerRefreshUiTests()
    {
        Directory.CreateDirectory(dir); store = new(dir);
        store.AddDefinition(new AchievementTrackerDefinition { Id = Id, Provider = "steam", GameId = "42", GameTitle = "A game", Title = "Building upgrades",
            Items = new() { new() { Id = "one", Name = "Postbox", Category = "Structures" }, new() { Id = "two", Name = "Bridge", Category = "Structures", Target = 3 } } });
        store.SetManualProgress(Id, Account, "one", 0); store.SaveNote(Id, Account, "one", "Check the mountain route.");
        var snapshot = new AchievementTrackerSnapshot { TrackerId = Id, Provider = "steam", GameId = "42", Account = Account, Source = "Test observations",
            ObservedUtcTicks = DateTime.UtcNow.AddMinutes(-1).Ticks, Items = new() { new() { ItemId = "one", Current = 1, Availability = TrackerAvailability.Available }, new() { ItemId = "two", Current = 2 } } };
        var file = Path.Combine(dir, "current.json"); File.WriteAllText(file, JsonSerializer.Serialize(snapshot));
        new TrackerRefreshService(store).Configure(Id, Account, JsonFileTrackerSource.AdapterId, "1", file, "Main profile");
    }
    public void Dispose() { SqliteConnection.ClearAllPools(); Directory.Delete(dir, true); }
    static T Field<T>(object o, string name) => (T)o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(o)!;
    static void Pump(Task task)
    {
        var frame = new DispatcherFrame(); var dispatcher = Dispatcher.CurrentDispatcher;
        _ = task.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)), TaskScheduler.Default);
        if (!task.IsCompleted) Dispatcher.PushFrame(frame); task.GetAwaiter().GetResult();
    }
    static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        yield return root;
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++) foreach (var child in Descendants(VisualTreeHelper.GetChild(root, i))) yield return child;
    }
    AchievementTrackerWindow Window() => new(null!, store, Account, "", new[] { new AchievementHubGame { Provider = "steam", Id = "42", Title = "A game" } }, "steam", "42");
    [Fact]
    public void RefreshRecoversTheSavedConnectionWhenItsUiCacheIsMissing()
    {
        AchievementCollectionAccessTests.OnSta(()=>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));var window=Window();
            try
            {
                Pump(window.LoadDefinitionsAsync());typeof(AchievementTrackerWindow).GetField("activeSource",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,null);
                var reviewed=false;window.ShowReconciliation=_=>{reviewed=true;return false;};Pump(window.RefreshAsync());
                Assert.True(reviewed);Assert.NotNull(Field<TrackerSourceBinding>(window,"activeSource"));Assert.True(Field<Button>(window,"refreshSource").IsEnabled);
                Assert.Null(store.Load(Id,Account).First().ReportedCurrent);
            }
            finally{window.Close();}
        });
    }
    [Fact]
    public void DisconnectedRefreshOpensSetupAndMissingAccountHasAnExplicitReason()
    {
        store.RemoveSource(Id,Account);
        AchievementCollectionAccessTests.OnSta(()=>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));var window=Window();
            try
            {
                Pump(window.LoadDefinitionsAsync());Assert.True(Field<Button>(window,"refreshSource").IsEnabled);Assert.Contains("No source connected",Field<TextBlock>(window,"refreshHint").Text);
                var setup=false;window.ShowSource=_=>{setup=true;return false;};Pump(window.RefreshAsync());Assert.True(setup);Assert.Null(store.Source(Id,Account));
            }
            finally{window.Close();}
            var offline=new AchievementTrackerWindow(null!,store,"","",provider:"steam",gameId:"42");
            try
            {
                Pump(offline.LoadDefinitionsAsync());Assert.Contains("account in Settings",Field<TextBlock>(offline,"refreshHint").Text);
                offline.ShowSource=_=>throw new Exception("Do not bind a source to an unknown account");Pump(offline.RefreshAsync());Assert.Contains("account in Settings",Field<TextBlock>(offline,"status").Text);
            }
            finally{offline.Close();}
        });
    }
    [Fact]
    public void OptionalLocalReview_ExistingNativeConnectionsEnableRefresh()
    {
        var data=Environment.GetEnvironmentVariable("PIXELVAULT_TRACKER_CONNECTION_REVIEW");if(string.IsNullOrWhiteSpace(data))return;
        var copy=Path.Combine(dir,"native-copy");Directory.CreateDirectory(Path.Combine(copy,"achievement-tracking"));
        using(var source=new SqliteConnection(new SqliteConnectionStringBuilder { DataSource=Path.Combine(data,"achievement-tracking","pixelvault-tracking.sqlite"),Mode=SqliteOpenMode.ReadOnly }.ToString()))
        using(var destination=new SqliteConnection("Data Source="+Path.Combine(copy,"achievement-tracking","pixelvault-tracking.sqlite")))
        {source.Open();destination.Open();source.BackupDatabase(destination);}
        var copied=new AchievementTrackingStore(copy);string account;
        using(var db=new SqliteConnection("Data Source="+copied.DatabasePath))
        {db.Open();using var cmd=db.CreateCommand();cmd.CommandText="SELECT account FROM tracker_source WHERE tracker_id=$t";cmd.Parameters.AddWithValue("$t",AchievementTrackingCatalog.DeathStrandingId);account=(string)cmd.ExecuteScalar()!;}
        AchievementCollectionAccessTests.OnSta(()=>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            foreach(var id in new[]{AchievementTrackingCatalog.DeathStrandingId,AchievementTrackingCatalog.DeathStrandingBuildId,AchievementTrackingCatalog.DeathStrandingUpgradeId})
            {
                var definition=copied.Definitions().Single(d=>d.Id==id);var window=new AchievementTrackerWindow(null!,copied,account,"",provider:definition.Provider,gameId:definition.GameId,achievementId:definition.AchievementId);
                try
                {
                    Pump(window.LoadDefinitionsAsync());Assert.Equal(id,Field<AchievementTrackerDefinition>(window,"selected").Id);
                    Assert.NotNull(Field<TrackerSourceBinding>(window,"activeSource"));
                    Assert.True(Field<Button>(window,"refreshSource").IsEnabled,Field<TextBlock>(window,"status").Text);
                    var worker=Environment.GetEnvironmentVariable("PIXELVAULT_TRACKER_REVIEW_WORKER");
                    if(!string.IsNullOrWhiteSpace(worker))
                    {
                        var native=new DeathStrandingTrackerSource(dir,worker);
                        typeof(AchievementTrackerWindow).GetField("refreshService",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(window,new TrackerRefreshService(copied,new[]{native}));
                        bool reviewed=false;window.ShowReconciliation=dialog=>
                        {
                            reviewed=true;var plan=Field<TrackerRefreshPlan>(dialog,"plan");Assert.Equal(definition.Items.Count,plan.Rows.Count);return false;
                        };
                        Pump(window.RefreshAsync());Assert.True(reviewed,Field<TextBlock>(window,"status").Text);
                    }
                }
                finally{window.Close();}
            }
        });
    }
    static void Render(Window window, string name, int width, int height)
    {
        var output = Environment.GetEnvironmentVariable("PIXELVAULT_TRACKING_REVIEW_OUTPUT"); if (string.IsNullOrWhiteSpace(output)) return;
        Directory.CreateDirectory(output); var root = (FrameworkElement)window.Content;
        root.Measure(new Size(width, height)); root.Arrange(new Rect(0, 0, width, height)); root.UpdateLayout();
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
        var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(Path.Combine(output, name + ".png")); encoder.Save(file);
    }

    [Fact]
    public void AutomaticSettingsPersistThroughTheTrackerUiAndCanBePaused()
    {
        AchievementCollectionAccessTests.OnSta(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var window = Window();
            try
            {
                Pump(window.LoadDefinitionsAsync()); Assert.True(Field<Button>(window, "automationButton").IsEnabled);
                window.ShowAutomation = dialog =>
                {
                    var root = (FrameworkElement)dialog.Content; root.Measure(new Size(536, 420)); root.Arrange(new Rect(0, 0, 536, 420)); root.UpdateLayout();
                    var modes = Descendants((DependencyObject)dialog.Content).OfType<ComboBox>().Single();
                    modes.SelectedIndex = 3;
                    typeof(TrackerAutomationWindow).GetProperty("Mode", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(dialog, TrackerAutomationMode.Periodic);
                    typeof(TrackerAutomationWindow).GetProperty("IntervalMinutes", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(dialog, 5);
                    Render(dialog, "tracker-automatic-settings", 536, 420); return true;
                };
                Pump(window.ConfigureAutomationAsync()); Assert.Equal(TrackerAutomationMode.Periodic, store.Automation(Id, Account).Settings.Mode);
                Assert.Equal(Visibility.Visible, Field<Button>(window, "pauseAutomatic").Visibility);
                Render(window, "tracker-automatic-780", 732, 690);
                var settings = store.Automation(Id, Account).Settings;
                store.SaveAutomation(Id, Account, settings.BindingId, settings.Mode, settings.IntervalMinutes, true); Pump(window.ReloadAsync());
                Assert.Equal("Resume automatic", Field<Button>(window, "pauseAutomatic").Content);
                Assert.Contains("Paused", Field<TextBlock>(window, "status").Text);
                Assert.Equal("Check the mountain route.", store.Load(Id, Account).Single(s => s.Item.Id == "one").Note);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void AutomaticConfigurationDoesNotDiscardAnUnsavedNote()
    {
        AchievementCollectionAccessTests.OnSta(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher)); var window = Window();
            try
            {
                Pump(window.LoadDefinitionsAsync()); Field<TextBox>(window, "notes").Text = "Unsaved draft";
                window.ShowAutomation = _ => throw new Exception("A draft must be saved first"); Pump(window.ConfigureAutomationAsync());
                Assert.Equal(TrackerAutomationMode.Manual, store.Automation(Id, Account).Settings.Mode);
                var binding = store.Source(Id, Account); store.SaveAutomation(Id, Account, binding.Id, TrackerAutomationMode.Periodic, 5, false);
                Pump(window.PollAutomaticStatusAsync());
                Assert.Equal("Unsaved draft", Field<TextBox>(window, "notes").Text); Assert.Equal(TrackerAutomationMode.Periodic, store.Automation(Id, Account).Settings.Mode);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void RefreshReviewConflictChoiceAndHistoryUndoWorkThroughTheSharedUi()
    {
        AchievementCollectionAccessTests.OnSta(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var window = Window();
            try
            {
                Pump(window.LoadDefinitionsAsync()); Assert.True(Field<Button>(window, "refreshSource").IsEnabled);
                window.ShowReconciliation = review =>
                {
                    var plan = Field<TrackerRefreshPlan>(review, "plan"); Assert.Single(plan.Rows, r => r.Conflict);
                    Render(review, "refresh-review", 886, 606);
                    plan.Rows.Single(r => r.ItemId == "one").UseSource = true; return true;
                };
                Pump(window.RefreshAsync());
                var state = store.Load(Id, Account).Single(s => s.Item.Id == "one"); Assert.True(state.Complete); Assert.False(state.IsManual); Assert.Equal("Check the mountain route.", state.Note);
                Render(window, "tracker-refresh-780", 732, 690);
                var history = new TrackerHistoryWindow(null!, store, Id, Account);
                try
                {
                    Render(history, "refresh-history", 716, 596);
                    var button = Descendants((DependencyObject)history.Content).OfType<Button>().Single(b => Equals(b.Content, "Undo selected change"));
                    button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump(history.PendingOperation);
                    state = store.Load(Id, Account).Single(s => s.Item.Id == "one"); Assert.Equal(0, state.ManualCurrent); Assert.Null(state.ReportedCurrent); Assert.Equal("Check the mountain route.", state.Note);
                }
                finally { history.Close(); }
                var source = new TrackerSourceWindow(null!, store.Source(Id, Account), store.SourceStatus(Id, Account));
                try { Render(source, "refresh-source", 636, 516); }
                finally { source.Close(); }
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void CancelledReviewAndUnsavedNoteDoNotChangeProgress()
    {
        AchievementCollectionAccessTests.OnSta(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var window = Window();
            try
            {
                Pump(window.LoadDefinitionsAsync()); bool reviewed = false; var savedNote = Field<TextBox>(window, "notes").Text;
                window.ShowReconciliation = _ => { reviewed = true; return false; };
                Field<TextBox>(window, "notes").Text = "An unsaved draft";
                Pump(window.RefreshAsync()); Assert.False(reviewed); Assert.Equal("An unsaved draft", Field<TextBox>(window, "notes").Text);
                Assert.Equal(0, store.SourceStatus(Id, Account).LastAttemptUtc);
                Field<TextBox>(window, "notes").Text = savedNote;
                Pump(window.RefreshAsync()); Assert.True(reviewed); Assert.Equal(TrackerRefreshOutcome.Cancelled, store.SourceStatus(Id, Account).Outcome);
                Assert.Null(store.Load(Id, Account).First().ReportedCurrent); Assert.True(Field<Button>(window, "refreshSource").IsEnabled);
            }
            finally { window.Close(); }
        });
    }
}
