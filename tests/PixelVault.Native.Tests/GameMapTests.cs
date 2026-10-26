using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Data.Sqlite;
using PixelVaultNative;
using Xunit;
using static PixelVaultNative.Tests.AchievementCollectionAccessTests;

namespace PixelVaultNative.Tests;

public sealed class GameMapTests : IDisposable
{
    readonly string dir = Path.Combine(Path.GetTempPath(), "PV-Maps-" + Guid.NewGuid().ToString("N"));
    string ImageFile()
    {
        Directory.CreateDirectory(dir);var path = Path.Combine(dir, "source.png");
        var encoder = new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(BitmapSource.Create(120, 80, 96, 96, PixelFormats.Bgr24, null, new byte[120 * 80 * 3], 120 * 3)));
        using var output = File.Create(path);encoder.Save(output);return path;
    }
    static GameMapDefinition Definition(string id = "map-one") => new() { Id = id, Provider = "steam", GameId = "123", GameTitle = "Fixture", Title = "A map", Region = "A region", AchievementIds = new() { "a" }, TrackerIds = new() { "t" }, Markers = new() { new() { Id = "m1", Label = "1", X = 0.25, Y = 0.75 } } };
    [Fact] public void ImportOwnsPrivateImageAndSurvivesSourceDeletion() => OnSta(() =>
    {
        var store = new GameMapStore(dir);var source = ImageFile();var map = store.Import(Definition(), source);File.Delete(source);
        var reopened = new GameMapStore(dir).List("steam", "123").Single();
        Assert.True(File.Exists(store.ImagePath(reopened)));Assert.Equal(120, reopened.PixelWidth);Assert.Equal(80, reopened.PixelHeight);Assert.Equal(1, reopened.Revision);
        Assert.Equal(0.75, reopened.Markers[0].Y);Assert.EndsWith(".png", map.ImageFile);
    });
    [Fact] public void LinksStayInsideGameAndProviderAndCanBeEditedWithoutImageLoss() => OnSta(() =>
    {
        var store = new GameMapStore(dir);var map = store.Import(Definition(), ImageFile());var image = map.ImageFile;
        Assert.Single(store.List("steam", "123", "a"));Assert.Single(store.List("steam", "123", tracker: "t"));
        Assert.Empty(store.List("steam", "123", "b"));Assert.Empty(store.List("steam", "456"));Assert.Empty(store.List("retroachievements", "123"));
        map.AchievementIds.Clear();map.TrackerIds.Clear();map.Title = "Renamed";store.Save(map);
        Assert.Empty(store.List("steam", "123", "a"));var saved = store.List("steam", "123").Single();Assert.Equal(image, saved.ImageFile);Assert.Equal("Renamed", saved.Title);Assert.Equal(2, saved.Revision);
    });
    [Fact] public void ConcurrentEditsCannotOverwriteNewerPinsOrLinks() => OnSta(() =>
    {
        var store = new GameMapStore(dir);store.Import(Definition(), ImageFile());var a = store.List("steam", "123").Single();var b = store.List("steam", "123").Single();
        a.Markers.Add(new() { Id = "m2", Label = "2", X = 0.5, Y = 0.5 });store.Save(a);b.AchievementIds.Clear();
        Assert.Throws<InvalidOperationException>(() => store.Save(b));Assert.Equal(2, store.List("steam", "123").Single().Markers.Count);Assert.Single(store.List("steam", "123", "a"));
    });
    [Fact] public void DuplicateMapCannotOverwriteExistingDefinition() => OnSta(() =>
    {
        var store = new GameMapStore(dir);var file = ImageFile();store.Import(Definition(), file);var duplicate = Definition();duplicate.Markers.Clear();
        Assert.Throws<SqliteException>(() => store.Import(duplicate, file));Assert.Single(store.List("steam", "123").Single().Markers);
    });
    [Fact] public void InvalidImagesDoNotCreateMapRecords() => OnSta(() =>
    {
        Directory.CreateDirectory(dir);var source = Path.Combine(dir, "bad.png");File.WriteAllText(source, "not an image");var store = new GameMapStore(dir);
        Assert.ThrowsAny<Exception>(() => store.Import(Definition(), source));Assert.Empty(store.List("steam", "123"));Assert.Empty(Directory.GetFiles(Path.Combine(store.Root, "images"), "*.tmp"));
    });
    [Theory] [InlineData(-0.1, 0)] [InlineData(1.1, 0)] [InlineData(0, double.NaN)] [InlineData(double.PositiveInfinity, 0)]
    public void InvalidCoordinatesAreRejected(double x, double y) { var map = Definition();map.Markers[0].X = x;map.Markers[0].Y = y;Assert.Throws<InvalidDataException>(() => GameMapStore.Validate(map)); }
    [Fact] public void TraversalAndDuplicateMarkerIdsAreRejected()
    {
        var store = new GameMapStore(dir);var map = Definition();map.ImageFile = "../outside.png";Assert.Throws<InvalidDataException>(() => store.ImagePath(map));
        map.ImageFile = "C:\\outside.png";Assert.Throws<InvalidDataException>(() => store.ImagePath(map));
        map.Markers.Add(new() { Id = "m1", Label = "Duplicate", X = 0, Y = 0 });Assert.Throws<InvalidDataException>(() => GameMapStore.Validate(map));
    }
    [Fact] public void WorldProjectionPreservesNorthUpAndCentreAtEveryImageSize()
    {
        Assert.Equal((0d, 0d), GameMapStore.WorldToImage(-3584, 3584, -3584, 3584, -3584, 3584));
        Assert.Equal((1d, 1d), GameMapStore.WorldToImage(3584, -3584, -3584, 3584, -3584, 3584));
        Assert.Equal((0.5, 0.5), GameMapStore.WorldToImage(0, 0, -1792, 1792, -1792, 1792));
        Assert.Throws<ArgumentOutOfRangeException>(() => GameMapStore.WorldToImage(0, 0, 1, 1, 0, 10));
    }
    [Fact] public void MapUiDoesNotModifyTrackerProgressOrAutomation() => OnSta(() =>
    {
        var tracking = new AchievementTrackingStore(dir);var map = Definition();tracking.Maps.Import(map, ImageFile());
        tracking.AddDefinition(new() { Id = "t", Provider = "steam", GameId = "123", GameTitle = "Fixture", Title = "Progress fixture", Items = new() { new() { Id = "m1", Name = "One" } } });
        tracking.SetManualProgress("t", "76561197960265729", "m1", 1);tracking.SaveNote("t", "76561197960265729", "m1", "Keep my note");
        var original = tracking.Definitions().Select(d => d.Id).ToArray();var history = tracking.History("t", "76561197960265729").Count;
        var links = new GameMapLinks();links.Bind(tracking, "steam", "123", "Fixture", null!, null!, "a");
        Assert.Contains("1", ((Button)links.Children[0]).Content.ToString());
        var window = new GameMapsWindow(null!, tracking, new() { Provider = "steam", Id = "123", Title = "Fixture" }, null!, null!, "a");
        window.Reload();Assert.Equal(original, tracking.Definitions().Select(d => d.Id));window.Close();
        var state = tracking.Load("t", "76561197960265729").Single();Assert.True(state.Complete);Assert.Equal("Keep my note", state.Note);Assert.Equal(history, tracking.History("t", "76561197960265729").Count);
    });
    [Fact] public void GenericPinsAndMapsNeedNoAchievementOrTracker() => OnSta(() =>
    {
        var map = Definition();map.Provider = "library";map.GameId = "local-game";map.AchievementIds.Clear();map.TrackerIds.Clear();
        var store = new GameMapStore(dir);store.Import(map, ImageFile());Assert.Single(store.List("library", "local-game"));
    });
    [Theory] [InlineData("123", "76561197960265729", 0)] [InlineData("456", "76561197960265729", 1)] [InlineData("123", "76561197960265730", 1)]
    public void RemainingFilterUsesOnlyCurrentGameAndAccount(string trackerGame, string account, int remainingCount) => OnSta(() =>
    {
        var tracking = new AchievementTrackingStore(dir);
        tracking.AddDefinition(new() { Id = "t", Provider = "steam", GameId = trackerGame, GameTitle = "Fixture", Title = "A tracker", Items = new() { new() { Id = "m1", Name = "One" } } });
        tracking.SetManualProgress("t", "76561197960265729", "m1", 1);
        var map = Definition();map.Markers[0].TrackerId = "t";map.Markers[0].ItemId = "m1";tracking.Maps.Import(map, ImageFile());
        var window = new GameMapsWindow(null!, tracking, new() { Provider = "steam", Id = "123", Title = "Fixture" }, account, null!);
        window.Reload();Field<CheckBox>(window, "remaining").IsChecked = true;
        Assert.Equal(remainingCount, Field<ListBox>(window, "pins").Items.Count);window.Close();
    });
    [Fact] public void SeedHasExactly56UniqueTrackerLinkedPinsAcrossThreeCalibratedRegions()
    {
        using var stream = typeof(GameMapStore).Assembly.GetManifestResourceStream("PixelVault.Maps.DeathStranding.json");Assert.NotNull(stream);
        var maps = JsonSerializer.Deserialize<List<GameMapDefinition>>(stream!)!;Assert.Equal(3, maps.Count);Assert.Equal(56, maps.Sum(m => m.Markers.Count));
        var chips = AchievementTrackingCatalog.Builtins().Single(t => t.Id == AchievementTrackingCatalog.DeathStrandingChipsId);
        Assert.Equal(chips.Items.Select(i => i.Id).Order(), maps.SelectMany(m => m.Markers).Select(p => p.ItemId).Order());
        Assert.Equal(new[] { 9, 44, 3 }, maps.Select(m => m.Markers.Count));
        foreach (var map in maps)
        {
            GameMapStore.Validate(map);Assert.Contains(chips.AchievementId, map.AchievementIds);Assert.Contains(chips.Id, map.TrackerIds);
            var half = map.Region == "Western Region" ? 1792 : 3584;
            foreach (var pin in map.Markers)
            {
                var expected = GameMapStore.WorldToImage(pin.WorldX!.Value, pin.WorldY!.Value, -half, half, -half, half);
                Assert.Equal(expected.X, pin.X, 10);Assert.Equal(expected.Y, pin.Y, 10);Assert.Equal(chips.Id, pin.TrackerId);
            }
        }
    }
    [Fact] public void PinEditorDistinguishesGenericMilestonesAndKeepsUnavailableLinks() => OnSta(() =>
    {
        var tracking = new AchievementTrackingStore(dir);var definition = tracking.Definitions().Single(d => d.Id == AchievementTrackingCatalog.DeathStrandingOrdersId);
        var marker = new GameMapMarker { Label = "An order", X = 0.5, Y = 0.5, TrackerId = definition.Id, ItemId = definition.Items[0].Id };
        var map = new GameMapDefinition { Provider = definition.Provider, GameId = definition.GameId, Title = "A map", Markers = new() { marker } };
        var editor = new GameMapPinWindow(null!, tracking, map, marker);
        var fields = ((StackPanel)((DockPanel)editor.Content).Children.OfType<ScrollViewer>().Single().Content).Children.OfType<ComboBox>().ToList();
        var labels = fields[1].Items.Cast<object>().Select(i => i.ToString()!).ToList();
        Assert.Contains(labels, s => s.EndsWith(" · LL"));Assert.Contains(labels, s => s.EndsWith(" · LLL"));Assert.NotNull(fields[1].SelectedItem);editor.Close();
        marker.TrackerId = "unavailable";marker.ItemId = "old-id";
        editor = new GameMapPinWindow(null!, tracking, map, marker);fields = ((StackPanel)((DockPanel)editor.Content).Children.OfType<ScrollViewer>().Single().Content).Children.OfType<ComboBox>().ToList();
        Assert.NotNull(fields[1].SelectedItem);Assert.Equal("old-id", editor.Result.ItemId);editor.Close();
    });
    public void Dispose() { SqliteConnection.ClearAllPools();if (Directory.Exists(dir)) Directory.Delete(dir, true); }
}
