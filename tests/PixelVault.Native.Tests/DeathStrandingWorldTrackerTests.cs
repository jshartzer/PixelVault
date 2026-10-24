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

public sealed class DeathStrandingWorldTrackerTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "PV-WorldTrackers-" + Guid.NewGuid().ToString("N"));
    AchievementTrackingStore Store() => new(dir);
    public void Dispose()
    {
        var full = Path.GetFullPath(dir); var temp = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (!full.StartsWith(temp, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException();
        for (int i = 0; Directory.Exists(full); i++)
        { SqliteConnection.ClearAllPools(); try { Directory.Delete(full, true); } catch (IOException) when (i < 5) { Thread.Sleep(50); } }
    }
    static T Field<T>(object o, string name) => (T)o.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(o)!;
    static void Pump(Task task)
    {
        var frame = new DispatcherFrame(); var dispatcher = Dispatcher.CurrentDispatcher;
        _ = task.ContinueWith(_ => dispatcher.BeginInvoke(new Action(() => frame.Continue = false)), TaskScheduler.Default);
        if (!task.IsCompleted) Dispatcher.PushFrame(frame); task.GetAwaiter().GetResult();
    }
    [Theory]
    [InlineData(AchievementTrackingCatalog.DeathStrandingBuildId, "023", 13)]
    [InlineData(AchievementTrackingCatalog.DeathStrandingUpgradeId, "057", 10)]
    [InlineData(AchievementTrackingCatalog.DeathStrandingChipsId, "035", 56)]
    public void CataloguesHaveStableAchievementLinksAndUnknownInitialProgress(string id, string achievement, int count)
    {
        var store = Store(); var definition = store.Definitions().Single(d => d.Id == id); AchievementTrackingCatalog.Validate(definition);
        Assert.Equal("steam", definition.Provider); Assert.Equal("1850570", definition.GameId); Assert.Equal(achievement, definition.AchievementId);
        Assert.Equal(count, definition.Items.Count); Assert.Equal(count, definition.Items.Select(i => i.Id).Distinct().Count());
        Assert.All(definition.Items, item => { Assert.True(Guid.TryParse(item.Id, out _)); Assert.Equal(1, item.Target); });
        Assert.Contains("Manual checklist", definition.Description);
        Assert.All(store.Load(id, "player"), s => { Assert.Null(s.ReportedCurrent); Assert.Null(s.ManualCurrent); Assert.False(s.IsKnown); Assert.False(s.Complete); });
        Assert.Null(store.Source(id, "player")); Assert.Equal(TrackerAutomationMode.Manual, store.Automation(id, "player").Settings.Mode);
    }
    [Fact]
    public void StructuresSeparatePlacementFromMaximumUpgradeAndExcludeDebugAndRoadFacts()
    {
        var defs = Store().Definitions(); var build = defs.Single(d => d.Id == AchievementTrackingCatalog.DeathStrandingBuildId);
        var upgrade = defs.Single(d => d.Id == AchievementTrackingCatalog.DeathStrandingUpgradeId);
        Assert.All(build.Items, i => { Assert.StartsWith("ldfp_builded_", i.Identifiers["FactName"]); Assert.Equal("CompletedConstruction", i.Identifiers["Milestone"]); });
        Assert.All(upgrade.Items, i => { Assert.StartsWith("ldfp_lvmax_", i.Identifiers["FactName"]); Assert.Equal("MaximumUpgrade", i.Identifiers["Milestone"]); });
        Assert.DoesNotContain(build.Items.Concat(upgrade.Items), i => i.Identifiers["FactName"].EndsWith("_sample") || i.Identifiers["FactName"].EndsWith("_road"));
        Assert.Equal(3, build.Items.Count(i => i.Category == "Placed tools and signs"));
        Assert.Equal(3, upgrade.Items.Count(i => i.Category == "Director's Cut additions"));
        Assert.Empty(build.Items.Select(i => i.Id).Intersect(upgrade.Items.Select(i => i.Id)));
        Assert.Contains("foundation", build.Items.Single(i => i.Name == "Bridge").Detail);
        Assert.All(upgrade.Items, i => Assert.Contains("level 3", i.Detail));
    }
    [Fact]
    public void ChipNumbersCoverOneThrough56WithRestorationDistinctFromPickup()
    {
        var chips = Store().Definitions().Single(d => d.Id == AchievementTrackingCatalog.DeathStrandingChipsId);
        Assert.Equal(Enumerable.Range(1, 56), chips.Items.Select(i => int.Parse(i.Identifiers["MemoryChipNumber"])));
        Assert.Equal(56, chips.Items.Select(i => i.Identifiers["HashId"]).Distinct().Count());
        Assert.All(chips.Items, i => { Assert.Equal("Restored", i.Identifiers["Milestone"]); Assert.Contains("only after restoration", i.Detail); Assert.DoesNotContain('\ufffd', i.Name); });
        Assert.Equal("#32 — Honda NSR250R", chips.Items[31].Name); Assert.Equal("#45 — dawn/SILENT POETS", chips.Items[44].Name);
        Assert.Equal(6, chips.Items.Select(i => i.Category).Distinct().Count());
    }
    [Fact]
    public void ExistingFabricationManualProgressAndLinkOverridesSurviveNewBuiltins()
    {
        var store = Store(); var fabrication = AchievementTrackingCatalog.DeathStranding(); var ladder = fabrication.Items.Single(i => i.Name == "Ladder");
        store.SetManualProgress(fabrication.Id, "player", ladder.Id, 1); store.SaveNote(fabrication.Id, "player", ladder.Id, "Keep this"); store.SetAchievementLink(fabrication.Id, null!);
        var reopened = Store(); Assert.Equal(5, reopened.Definitions().Count); Assert.Null(reopened.Definitions().Single(d => d.Id == fabrication.Id).AchievementId);
        var state = reopened.Load(fabrication.Id, "player").Single(s => s.Item.Id == ladder.Id); Assert.True(state.Complete); Assert.Equal("Keep this", state.Note);
        Assert.All(reopened.Load(AchievementTrackingCatalog.DeathStrandingBuildId, "player"), s => Assert.Null(s.Current));
    }
    [Fact]
    public void ManualWorldProgressIsAccountScopedUndoableAndIndependentOfFabrication()
    {
        var store = Store(); var id = AchievementTrackingCatalog.DeathStrandingBuildId; var ladder = store.Definitions().Single(d => d.Id == id).Items.Single(i => i.Name == "Ladder");
        store.SetManualProgress(id, "player", ladder.Id, 1); store.SaveNote(id, "player", ladder.Id, "Placed near the station");
        Assert.True(Store().Load(id, "player").Single(s => s.Item.Id == ladder.Id).Complete);
        Assert.All(store.Load(id, "other"), s => Assert.Null(s.Current)); Assert.All(store.Load(AchievementTrackingCatalog.DeathStrandingId, "player"), s => Assert.Null(s.Current));
        store.Undo(id, "player", store.History(id, "player").Single(h => h.Kind == "manual").Id);
        var state = store.Load(id, "player").Single(s => s.Item.Id == ladder.Id); Assert.Null(state.Current); Assert.Equal("Placed near the station", state.Note);
    }
    [Fact]
    public void SaveDecoderOnlyConnectsValidatedMilestoneCatalogues()
    {
        var source = new DeathStrandingTrackerSource(Path.Combine(dir, "sources"));
        foreach (var definition in Store().Definitions().Where(d => d.Id != AchievementTrackingCatalog.DeathStrandingId))
        {
            var binding = new TrackerSourceBinding { AdapterId = source.Id, AdapterVersion = source.Version, TrackerId = definition.Id, Account = "76561197960265730", Location = dir, GameDirectory = dir };
            if (DeathStrandingWorldReader.Supports(definition.Id) || definition.Id == AchievementTrackingCatalog.DeathStrandingOrdersId || DeathStrandingChipReader.Supports(definition.Id)) source.Validate(binding, definition);
            else Assert.Throws<NotSupportedException>(() => source.Validate(binding, definition));
        }
    }
    [Theory]
    [InlineData("023", AchievementTrackingCatalog.DeathStrandingBuildId, "tracker-structures")]
    [InlineData("057", AchievementTrackingCatalog.DeathStrandingUpgradeId, "tracker-upgrades")]
    [InlineData("035", AchievementTrackingCatalog.DeathStrandingChipsId, "tracker-memory-chips")]
    public void AchievementEntrySelectsItsOwnCatalogueAndRenders(string achievement, string id, string renderName)
    {
        AchievementCollectionAccessTests.OnSta(() =>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var window = new AchievementTrackerWindow(null!, Store(), "player", "", provider: "steam", gameId: "1850570", achievementId: achievement);
            try
            {
                Pump(window.LoadDefinitionsAsync()); var choices = Field<ComboBox>(window, "trackers");
                Assert.Single(choices.Items); Assert.Equal(id, ((AchievementTrackerDefinition)choices.SelectedItem).Id);
                Assert.Contains("not checked", Field<TextBlock>(window, "source").Text); Assert.True(Field<Button>(window, "refreshSource").IsEnabled);
                Assert.Contains("No source connected",Field<TextBlock>(window,"refreshHint").Text);
                var first = (ListBoxItem)Field<ListBox>(window, "list").Items[0];
                var cell = Assert.IsType<Border>(first.Content); var row = Assert.IsType<Grid>(cell.Child);
                Assert.True(((TextBlock)((StackPanel)row.Children[0]).Children[0]).FontSize >= 18);
                Assert.Equal(1, Grid.GetColumn(row.Children[1]));
                var toggle = Assert.Single(((StackPanel)row.Children[1]).Children.OfType<ToggleButton>());
                Assert.Equal(25, toggle.MinHeight); Assert.False(toggle.IsChecked);
                Assert.Equal("Not Complete", ((TextBlock)((StackPanel)toggle.Content).Children[0]).Text);
                var output = Environment.GetEnvironmentVariable("PIXELVAULT_TRACKING_REVIEW_OUTPUT");
                if (string.IsNullOrWhiteSpace(output)) return;
                Directory.CreateDirectory(output); var root = (FrameworkElement)window.Content;
                root.Measure(new Size(1002, 742)); root.Arrange(new Rect(0, 0, 1002, 742)); root.UpdateLayout();
                var bitmap = new RenderTargetBitmap(1002, 742, 96, 96, PixelFormats.Pbgra32); bitmap.Render(root);
                var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
                using var file = File.Create(Path.Combine(output, renderName + ".png")); encoder.Save(file);
            }
            finally { window.Close(); }
        });
    }
}
