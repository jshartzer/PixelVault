using System.Reflection;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using PixelVaultNative;
using Xunit;
using static PixelVaultNative.Tests.AchievementCollectionAccessTests;

namespace PixelVaultNative.Tests;

public sealed class LibraryTimelineAchievementTests
{
    static readonly DateTime Now = new(2026, 10, 18, 12, 0, 0, DateTimeKind.Utc);
    static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("America/Chicago");
    static AchievementHubGame Game(string id = "1") => new() { Provider = "steam", Id = id, Title = "Game " + id,
        Folder = new() { Name = "Game " + id, PlatformLabel = "Steam", SteamAppId = id } };
    static GameAchievementsFetchService.AchievementRow Row(string id, DateTime time) => new()
    {
        Provider = "steam", ProviderGameId = "1", ProviderAchievementId = id, Title = "Achievement " + id,
        ProgressKnown = true, Unlocked = true, UnlockUtcTicks = time.Ticks, Points = 40, AwardedPoints = 40
    };
    static LibraryTimelineAchievement Event(string id, DateTime local) => new()
    {
        Game = Game(), Account = "alice", Row = Row(id, TimeZoneInfo.ConvertTimeToUtc(local, Zone)), UnlockLocal = local, GameRows = new()
    };

    [Fact]
    public void Selection_UsesProviderUnlockTimeLocalDateRangeAndStableScopedIdentity()
    {
        var late = Row("A", new DateTime(2026, 10, 17, 4, 59, 59, DateTimeKind.Utc)); // Oct 16, 23:59 Chicago.
        var caseVariant = Row("a", new DateTime(2026, 10, 16, 12, 0, 0, DateTimeKind.Utc));
        var tomorrow = Row("tomorrow", new DateTime(2026, 10, 17, 5, 0, 0, DateTimeKind.Utc));
        var locked = Row("locked", lateDate()); locked.Unlocked = false;
        var unknown = Row("unknown", lateDate()); unknown.ProgressKnown = false;
        var undated = Row("undated", DateTime.MinValue);
        var future = Row("future", Now.AddMinutes(1));
        var wrongGame = Row("wrong-game", lateDate()); wrongGame.ProviderGameId = "2";
        var accounts = new Dictionary<string, AchievementHubStore.AccountData>
        {
            ["steam"] = new() { Account = "alice", Games = new()
            {
                ["1"] = new() { Rows = new() { late, late, caseVariant, tomorrow, locked, unknown, undated, future, wrongGame } },
                ["2"] = new() { Rows = new() { wrongGame } }
            } }
        };
        var selected = LibraryTimelineAchievement.Select(new[] { Game(), Game() }, accounts,
            new DateTime(2026, 10, 16), new DateTime(2026, 10, 16), Now.Ticks, Zone);
        Assert.Equal(new[] { "A", "a" }, selected.Select(e => e.Row.ProviderAchievementId));
        Assert.Equal(new DateTime(2026, 10, 16, 23, 59, 59), selected[0].UnlockLocal);
        Assert.All(selected, e => Assert.Equal("alice", e.Account));
        Assert.False(StringComparer.OrdinalIgnoreCase.Equals(selected[0].Key, selected[1].Key));
        static DateTime lateDate() => new(2026, 10, 16, 20, 0, 0, DateTimeKind.Utc);
    }

    [Fact]
    public void Mosaic_InterleavesEventsChronologicallyIncludingDaysWithoutPhotosWithoutMutatingCaptureLists()
    {
        var date = new DateTime(2026, 10, 16);
        var achievement = Event("between", date.AddHours(11));
        var onlyDay = Event("only-day", date.AddDays(-1).AddHours(9));
        var snapshot = new LibraryDetailRenderSnapshot { VisibleFiles = new() { "new.png", "old.mp4" },
            Groups = new() { new() { CaptureDate = date, Files = new() { "new.png", "old.mp4" } } },
            TimelineContextByFile = new() { ["new.png"] = new() { CaptureDate = date.AddHours(12) }, ["old.mp4"] = new() { CaptureDate = date.AddHours(10) } },
            Achievements = new() { achievement, achievement, onlyDay } };
        var mosaic = LibraryTimelineMosaicProjection.Build(snapshot);
        Assert.Equal(new[] { "new.png", achievement.Key, "old.mp4", onlyDay.Key }, mosaic.Groups.SelectMany(g => g.Files));
        Assert.Equal(2, mosaic.Groups.Count);
        Assert.Equal(new[] { "new.png", "old.mp4" }, snapshot.VisibleFiles);
        Assert.Equal(snapshot.VisibleFiles, snapshot.Groups.Single().Files);
        Assert.Empty(snapshot.MediaLayoutByFile);
        Assert.True(mosaic.Media[achievement.Key].UseSingleSquareCell);
        Assert.Equal(2, mosaic.Achievements.Count);
        var labels = MainWindow.BuildLibraryCaptureDateLabelMapForPlacements(mosaic.Groups, null!, date);
        Assert.Contains(onlyDay.Key, labels.Keys);
        Assert.DoesNotContain(achievement.Key, labels.Keys); // The newest photo already carries that day's stamp.
    }

    [Fact]
    public void MetadataRefinement_RepositionsEventsWhenPhotoTimeChangesWithinTheSameDay()
    {
        var day = new DateTime(2026, 10, 16);
        var achievement = Event("between", day.AddHours(11));
        LibraryDetailRenderSnapshot Snapshot(int hour) => new() { VisibleFiles = new() { "photo.png" },
            Groups = new() { new() { CaptureDate = day, Files = new() { "photo.png" } } }, Achievements = new() { achievement },
            TimelineContextByFile = new() { ["photo.png"] = new() { CaptureDate = day.AddHours(hour) } } };
        var before = Snapshot(10); var after = Snapshot(12);
        Assert.Equal(new[] { achievement.Key, "photo.png" }, LibraryTimelineMosaicProjection.Build(before).Groups.Single().Files);
        Assert.True(LibraryTimelineMosaicProjection.CaptureTimesChanged(before, after));
        Assert.Equal(new[] { "photo.png", achievement.Key }, LibraryTimelineMosaicProjection.Build(after).Groups.Single().Files);
        Assert.False(LibraryTimelineMosaicProjection.CaptureTimesChanged(after, Snapshot(12)));
    }

    [Fact]
    public void AchievementCells_StayAtTheSmallestSquareSizeAndFollowPhotoSizing()
    {
        var files = Enumerable.Range(0, 60).Select(i => "shot" + i + ".png").ToList();
        var events = Enumerable.Range(0, 20).Select(i => Event("E" + i, new DateTime(2026, 10, 16).AddMinutes(i))).ToList();
        var mixed = files.SelectMany((f, i) => i < events.Count ? new[] { f, events[i].Key } : new[] { f }).ToList();
        var media = files.ToDictionary(f => f, _ => new LibraryDetailMediaLayoutInfo { PixelWidth = 1920, PixelHeight = 1080 });
        foreach (var e in events) media[e.Key] = new() { PixelWidth = 1, PixelHeight = 1, UseSingleSquareCell = true };
        var sizes = new List<int>();
        foreach (var requested in new[] { 160, 260, 420 })
        {
            var chunks = MainWindow.BuildLibraryDetailMasonryChunks(mixed, 1280, 4, requested, 120, 800, true, media);
            var placements = chunks.SelectMany(c => c.Placements).ToList();
            Assert.Equal(mixed, placements.Select(p => p.File));
            var smallest = placements.Min(p => Math.Min(p.Width, p.Height));
            Assert.All(placements.Where(p => p.File.StartsWith("achievement:")), p =>
            { Assert.Equal(smallest, p.Width); Assert.Equal(p.Width, p.Height); });
            sizes.Add(smallest);
        }
        Assert.True(sizes[0] < sizes[1] && sizes[1] < sizes[2]);
    }

    [Fact]
    public void AchievementOnlyDays_KeepCompactTilesAndReportAchievementsWithoutInventingPhotos()
    {
        var achievement = Event("only", new DateTime(2026, 10, 16, 10, 0, 0));
        var snapshot = new LibraryDetailRenderSnapshot { Achievements = new() { achievement } };
        var mosaic = LibraryTimelineMosaicProjection.Build(snapshot);
        var chunk = Assert.Single(MainWindow.BuildLibraryDetailMasonryChunks(mosaic.Groups.Single().Files, 1280, 4, 260, 120, 600, true, mosaic.Media));
        var placement = Assert.Single(chunk.Placements);
        Assert.InRange(placement.Width, 120, 350);
        Assert.Equal(placement.Width, placement.Height);
        Assert.Equal("0 photos | 1 achievement | 1 game | 1 platform | October 16, 2026", MainWindow.BuildTimelineActivitySummary(snapshot));
    }

    [Fact]
    public void MixedVirtualRows_OnlyExposeRealCapturesToMetadataAndFileActions()
    {
        var date = new DateTime(2026, 10, 16);
        var achievement = Event("A", date.AddHours(11));
        var snapshot = new LibraryDetailRenderSnapshot { VisibleFiles = new() { "photo.png" },
            Groups = new() { new() { CaptureDate = date, Files = new() { "photo.png" } } },
            TimelineContextByFile = new() { ["photo.png"] = new() { CaptureDate = date.AddHours(12) } },
            Achievements = new() { achievement } };
        var mosaic = LibraryTimelineMosaicProjection.Build(snapshot);
        var host = (MainWindow)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(MainWindow));
        var method = typeof(MainWindow).GetMethod("BuildLibraryContinuousMosaicRowDefinitions", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var rows = (List<MainWindow.VirtualizedRowDefinition>)method.Invoke(host, new object?[]
        { null, null, mosaic.Groups, snapshot.TimelineContextByFile, mosaic.Media, null, 1000d, 220, 1d, true,
            null, null, null, null, null, null, mosaic.Achievements, false, 0d, true })!;
        Assert.Equal(new[] { "photo.png" }, rows.SelectMany(r => r.Files));
    }

    [Fact]
    public void Toggle_ControlsOnlyAchievementsAndPreservesCaptureFilterAndSelection()
    {
        OnSta(() =>
        {
            var panes = new MainWindow.LibraryBrowserPaneRefs { TimelineFilterPanel = new WrapPanel() };
            MainWindow.BuildLibraryCaptureControls(panes);
            var ws = new MainWindow.LibraryBrowserWorkingSet { CaptureMediaFilter = MainWindow.LibraryCaptureMediaFilter.Clips };
            ws.SelectedDetailFiles.Add("clip.mp4");
            var renders = 0;
            MainWindow.WireLibraryCaptureFilters(ws, panes, () => renders++);
            Assert.True(panes.TimelineAchievementsToggle.IsChecked);
            panes.TimelineAchievementsToggle.IsChecked = false;
            panes.TimelineAchievementsToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.False(ws.ShowTimelineAchievements);
            Assert.Equal(MainWindow.LibraryCaptureMediaFilter.Clips, ws.CaptureMediaFilter);
            Assert.Contains("clip.mp4", ws.SelectedDetailFiles);
            Assert.Equal(1, renders);
            panes.TimelineAchievementsToggle.IsChecked = true;
            panes.TimelineAchievementsToggle.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(ws.ShowTimelineAchievements); Assert.Equal(2, renders);
        });
    }

    [Fact]
    public void Tile_IsSquareAccessibleAndOpensAchievementDetailsInsteadOfPhotoSelection()
    {
        OnSta(() =>
        {
            var entry = Event("Champion", new DateTime(2026, 10, 16, 12, 0, 0));
            var opened = 0;
            var tile = MainWindow.BuildLibraryTimelineAchievementTile(entry, 180, 360, "Today", () => opened++);
            tile.Measure(new Size(180, 180)); tile.Arrange(new Rect(0, 0, 180, 180));
            Assert.Equal(180, tile.ActualWidth); Assert.Equal(tile.ActualWidth, tile.ActualHeight);
            Assert.Same(entry, tile.Tag);
            var button = Assert.IsType<Button>(tile.Child);
            Assert.Contains("Achievement Champion", AutomationProperties.GetName(button));
            Assert.Contains("40 PV pts", (string)button.ToolTip);
            button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.Equal(1, opened);
        });
    }
}
