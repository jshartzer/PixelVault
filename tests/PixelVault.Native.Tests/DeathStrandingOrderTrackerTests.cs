using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class DeathStrandingOrderTrackerTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "PV-OrderTracker-" + Guid.NewGuid().ToString("N"));
    const string Id = AchievementTrackingCatalog.DeathStrandingOrdersId;
    AchievementTrackingStore Store() => new(dir);
    static AchievementTrackerDefinition Definition() => AchievementTrackingCatalog.Builtins().Single(d => d.Id == Id);
    static T Field<T>(object o, string name) => (T)o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(o)!;
    static void Pump(Task task)
    {
        var frame = new DispatcherFrame(); var dispatcher = Dispatcher.CurrentDispatcher;
        _ = task.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)), TaskScheduler.Default);
        if (!task.IsCompleted) Dispatcher.PushFrame(frame); task.GetAwaiter().GetResult();
    }
    public void Dispose()
    {
        var full = Path.GetFullPath(dir); var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(temp, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException();
        for (int i = 0; Directory.Exists(full); i++)
        { SqliteConnection.ClearAllPools(); try { Directory.Delete(full, true); } catch (IOException) when (i < 5) { Thread.Sleep(50); } }
    }
    [Fact]
    public void CatalogueCovers540OrdersWithIndependentRanksAndExtractedCategories()
    {
        var d = Definition(); AchievementTrackingCatalog.Validate(d);
        Assert.Equal("019", d.AchievementId); Assert.Equal("1850570", d.GameId);
        Assert.Equal(new[] { "LL", "LLL" }, d.Milestones); Assert.Equal(1080, d.Items.Count);
        Assert.Equal(540, d.Items.Select(i => i.GroupId).Distinct().Count()); Assert.Equal(80, d.CategoryTargets.Values.Sum());
        Assert.All(d.Items.GroupBy(i => i.GroupId), g => { Assert.Equal(2, g.Count()); Assert.Equal(g.Key, g.First().Identifiers["MissionId"]); });
        Assert.Equal(223, d.Items.Count(i => i.Category == "Condition" && i.Milestone == "LL"));
        Assert.Equal(186, d.Items.Count(i => i.Category == "Quantity" && i.Milestone == "LL"));
        Assert.Equal(66, d.Items.Count(i => i.Category == "Time" && i.Milestone == "LL"));
        Assert.Equal(65, d.Items.Count(i => i.Category == "Miscellaneous" && i.Milestone == "LL"));
        Assert.All(d.Items.Where(i => i.GroupId == "212002"), i => { Assert.Equal("331", i.Identifiers["OrderNumber"]); Assert.Equal("Time", i.Category); Assert.Contains("Synthetic Cryptobiote", i.Name); });
        Assert.All(Store().Load(Id, "player"), s => Assert.Null(s.Current));
    }
    [Fact]
    public void LllImportLeavesLlUnknownAndManualBothRanksQualifyOnlyOnceWithUndo()
    {
        var store = Store(); var d = Definition(); var lll = d.Items.Single(i => i.Id == "212002:LLL");
        var snapshot = new AchievementTrackerSnapshot { TrackerId = Id, Provider = "steam", GameId = "1850570", Account = "player", Source = "Controlled LLL snapshot", ObservedUtcTicks = DateTime.UtcNow.Ticks,
            Items = new() { new() { ItemId = lll.Id, Current = 1 } } };
        var json = JsonSerializer.Serialize(snapshot); Assert.Equal(1, store.Import(json, "player")); Assert.Equal(0, store.Import(json, "player"));
        var rows = store.Load(Id, "player"); Assert.Null(rows.Single(s => s.Item.Id == "212002:LL").Current);
        Assert.Equal(1, AchievementTrackingCatalog.QualifyingRows(rows)["Time"]);
        store.SetManualProgress(Id, "player", "212002:LL", 1); store.SaveNote(Id, "player", "212002:LL", "Known LL example");
        Assert.Equal(1, AchievementTrackingCatalog.QualifyingRows(store.Load(Id, "player"))["Time"]);
        Assert.All(store.Load(Id, "other"), s => Assert.Null(s.Current));
        Assert.All(store.Load(AchievementTrackingCatalog.DeathStrandingId, "player"), s => Assert.Null(s.Current));
        store.Undo(Id, "player", store.History(Id, "player").Single(h => h.Kind == "manual").Id);
        var state = Store().Load(Id, "player").Single(s => s.Item.Id == "212002:LL"); Assert.Null(state.Current); Assert.Equal("Known LL example", state.Note);
        Assert.True(Store().Load(Id, "player").Single(s => s.Item.Id == lll.Id).Complete);
    }
    [Fact]
    public void InvalidMilestoneMatricesAndCategoryTargetsAreRejected()
    {
        var d = Definition(); d.Items.RemoveAt(0); Assert.Throws<InvalidDataException>(() => AchievementTrackingCatalog.Validate(d));
        d = Definition(); d.Items[0].Milestone = "invalid"; Assert.Throws<InvalidDataException>(() => AchievementTrackingCatalog.Validate(d));
        d = Definition(); d.CategoryTargets["Missing category"] = 20; Assert.Throws<InvalidDataException>(() => AchievementTrackingCatalog.Validate(d));
        d = Definition(); d.Milestones.Clear(); Assert.Throws<InvalidDataException>(() => AchievementTrackingCatalog.Validate(d));
    }
    [Theory]
    [InlineData(1002)]
    [InlineData(732)]
    public void AchievementEntryRendersPairedRanksSearchFiltersAndPersistsCheckOff(int width)
    {
        AchievementCollectionAccessTests.OnSta(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var store = Store(); var d = Definition(); var orders = new[] {176,181,182,184,199,210,211,212,220,230,248,250,253,331,388,391,392,405,406,408,409,410,416,422,424,432,435,439,447,453,468,469,473,479,490,491,493,499,508,605};
            foreach (var item in d.Items.Where(i => i.Milestone == "LLL" && orders.Contains(int.Parse(i.Identifiers["OrderNumber"])))) store.SetManualProgress(Id, "player", item.Id, 1);
            var window = new AchievementTrackerWindow(null!, store, "player", "", provider: "steam", gameId: "1850570", achievementId: "019");
            try
            {
                Pump(window.LoadDefinitionsAsync()); Assert.Single(Field<ComboBox>(window, "trackers").Items);
                Assert.Contains("LL: 0 recorded, 540 not checked", Field<TextBlock>(window, "source").Text);
                Assert.Contains("LLL: 40 recorded, 500 not checked", Field<TextBlock>(window, "source").Text);
                var list = Field<ListBox>(window, "list"); Assert.Equal(540, list.Items.Count);
                Field<ComboBox>(window, "filter").SelectedIndex = 3; Assert.Equal(40, list.Items.Count);
                Field<TextBox>(window, "search").Text = "#331 "; Assert.Single(list.Items);
                var cell = Assert.IsType<Border>(((ListBoxItem)list.Items[0]).Content);
                var row = Assert.IsType<Grid>(cell.Child); var text = Assert.IsType<StackPanel>(row.Children[0]); Assert.True(((TextBlock)text.Children[0]).FontSize >= 18);
                var indicators = Assert.IsType<StackPanel>(row.Children[1]); Assert.Equal(1, Grid.GetColumn(indicators));
                var checks = indicators.Children.OfType<ToggleButton>().ToList();
                Assert.Equal(2, checks.Count); Assert.False(checks[0].IsChecked); Assert.True(checks[1].IsChecked);
                Assert.All(checks, c => { Assert.Equal(25, c.MinHeight); Assert.Equal(108, c.MinWidth); });
                Assert.Equal("Not Complete", ((TextBlock)((StackPanel)checks[0].Content).Children[1]).Text);
                checks[0].RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump(window.PendingEdit);
                Assert.True(Store().Load(Id, "player").Single(s => s.Item.Id == "212002:LL").Complete);
                Assert.True(Store().Load(Id, "player").Single(s => s.Item.Id == "212002:LLL").Complete);
                var counts = AchievementTrackingCatalog.QualifyingRows(Store().Load(Id, "player")); Assert.Equal(40, counts.Values.Sum());
                var refreshedRow = (Grid)((Border)((ListBoxItem)list.Items[0]).Content).Child;
                var refreshedChecks = ((StackPanel)refreshedRow.Children[1]).Children.OfType<ToggleButton>().ToList();
                Assert.All(refreshedChecks, c => Assert.True(c.IsChecked));
                Field<Button>(window, "reset").RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent)); Pump(window.PendingEdit);
                Assert.Null(Store().Load(Id, "player").Single(s => s.Item.Id == "212002:LL").Current);
                Assert.True(Store().Load(Id, "player").Single(s => s.Item.Id == "212002:LLL").Complete);
                var output = Environment.GetEnvironmentVariable("PIXELVAULT_TRACKING_REVIEW_OUTPUT"); if (string.IsNullOrWhiteSpace(output)) return;
                Directory.CreateDirectory(output); var root = (FrameworkElement)window.Content;
                root.Measure(new Size(width, 742)); root.Arrange(new Rect(0, 0, width, 742)); root.UpdateLayout();
                var renderedCard = (Border)((ListBoxItem)list.Items[0]).Content; Assert.Equal(34,renderedCard.ActualHeight);
                var renderedRow = (Grid)renderedCard.Child; Assert.True(renderedRow.Children[1].TranslatePoint(new Point(), renderedRow).X > 0);
                var bitmap = new RenderTargetBitmap(width, 742, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(output, "tracker-orders-" + width + ".png")); encoder.Save(file);
            }
            finally { window.Close(); }
        });
    }
}
