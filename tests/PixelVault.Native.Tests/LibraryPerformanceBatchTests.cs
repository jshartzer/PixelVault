using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class LibraryPerformanceBatchTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProfileSectionsStartOnce_BeforeOrAfterWindowLoaded(bool alreadyLoaded)
    {
        AchievementCollectionAccessTests.OnSta(() =>
        {
            var window = new System.Windows.Window { Width = 100, Height = 100, ShowInTaskbar = false };
            try
            {
                void Pump() => window.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                if (alreadyLoaded) { window.Show(); Pump(); }
                var calls = 0;
                MainWindow.ScheduleLibraryGameProfileInitialLoad(window, () => calls++, CancellationToken.None);
                if (!alreadyLoaded) window.Show();
                Pump(); Pump();
                Assert.Equal(1, calls);
                using var cancelled = new CancellationTokenSource();
                MainWindow.ScheduleLibraryGameProfileInitialLoad(window, () => calls++, cancelled.Token);
                cancelled.Cancel();
                Pump();
                Assert.Equal(1, calls);
            }
            finally { window.Close(); }
        });
    }

    [Fact]
    public void GroupingCache_TracksMiddlePathsFlagsNotesAndRoot_AndRefreshInvalidatesBothModes()
    {
        var host = new LibraryBrowserViewModelTests.StubHost();
        var vm = new LibraryBrowserViewModel(host);
        var folder = new LibraryFolderInfo { Name = "Game", FolderPath = "C:/lib/game", PlatformLabel = "Steam",
            FilePaths = new[] { "C:/lib/a.png", "C:/lib/b.png", "C:/lib/c.png" } };
        var folders = new[] { folder };
        var all = vm.GetOrBuildLibraryBrowserFolderViews(folders, "all");
        var console = vm.GetOrBuildLibraryBrowserFolderViews(folders, "console");
        Assert.Same(all, vm.GetOrBuildLibraryBrowserFolderViews(folders, "all"));
        Assert.Same(console, vm.GetOrBuildLibraryBrowserFolderViews(folders, "console"));
        folder.FilePaths[1] = "C:/lib/replacement.png";
        folder.IsFavorite = folder.IsShowcase = folder.IsCompleted100Percent = true;
        folder.CollectionNotes = "New searchable note";
        var changed = vm.GetOrBuildLibraryBrowserFolderViews(folders, "all");
        Assert.NotSame(all, changed);
        Assert.Contains("C:/lib/replacement.png", changed[0].FilePaths);
        Assert.Contains("new searchable note", changed[0].SearchBlob);
        var changedConsole = vm.GetOrBuildLibraryBrowserFolderViews(folders, "console");
        Assert.NotSame(console, changedConsole);
        Assert.True(changedConsole[0].IsFavorite);
        Assert.True(changedConsole[0].IsShowcase);
        Assert.True(changedConsole[0].IsCompleted100Percent);
        host.LibraryRoot = "D:/other";
        Assert.NotSame(changed, vm.GetOrBuildLibraryBrowserFolderViews(folders, "all"));
        vm.InvalidateFolderViews();
        Assert.NotSame(changedConsole, vm.GetOrBuildLibraryBrowserFolderViews(folders, "console"));
    }

    [Fact]
    public void ProfileSnapshot_ReadsIndexOnce_ResolvesEachUniqueDateOnce_AndOrdersStably()
    {
        var reads = 0;
        var dates = new List<string>();
        var index = new Dictionary<string, LibraryMetadataIndexEntry>();
        var newest = new DateTime(2026, 11, 5, 0, 0, 0, DateTimeKind.Utc);
        var entries = MainWindow.BuildLibraryGameProfileCaptureSnapshot(
            () => new() { "b.png", "a.png", "clip.mp4", "B.PNG", "" },
            files => { reads++; Assert.Equal(3, files.Count); return index; },
            (file, snapshot) => { Assert.Same(index, snapshot); dates.Add(file); return file == "clip.mp4" ? newest.AddDays(-1) : newest; },
            CancellationToken.None);
        Assert.Equal(1, reads);
        Assert.Equal(3, dates.Count);
        Assert.Equal(new[] { "a.png", "b.png", "clip.mp4" }, entries.Select(e => e.FilePath));
        Assert.True(entries[2].IsVideo);
    }

    [Fact]
    public void ProfileSnapshot_CancelledDuringDiscovery_DoesNotReadMetadata()
    {
        using var cancellation = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => MainWindow.BuildLibraryGameProfileCaptureSnapshot(
            () => { cancellation.Cancel(); return new() { "a.png" }; },
            _ => throw new Exception("Should not load a closed profile"),
            (_, _) => throw new Exception("Should not resolve dates"), cancellation.Token));
    }

    [Fact]
    public void ProfileSnapshot_CancelledDuringDateResolution_StopsRemainingWork()
    {
        using var cancellation = new CancellationTokenSource();
        var resolved = 0;
        Assert.Throws<OperationCanceledException>(() => MainWindow.BuildLibraryGameProfileCaptureSnapshot(
            () => new() { "a.png", "b.png" }, _ => new(),
            (_, _) => { resolved++; cancellation.Cancel(); return DateTime.MinValue; }, cancellation.Token));
        Assert.Equal(1, resolved);
    }
}
