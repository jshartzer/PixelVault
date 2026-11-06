using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class LibraryCoverSummaryTests
{
    static readonly DateTime Older = new(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
    static readonly DateTime Newer = Older.AddDays(2);

    static LibraryFolderInfo Folder(string name, string platform, string[] files, long ticks) => new()
    {
        Name = name, FolderPath = "C:/lib/" + name + platform, PlatformLabel = platform,
        FilePaths = files, FileCount = files.Length, PreviewImagePath = files[0],
        NewestCaptureUtcTicks = ticks, NewestRecentSortUtcTicks = ticks + 100,
        SteamAppId = platform == "Steam" ? "42" : "", CollectionNotes = "Remember this run"
    };

    [Fact]
    public void CompleteSummaries_MergeWithoutMetadataOrCaptureDateProbes()
    {
        var host = new LibraryBrowserViewModelTests.StubHost();
        var vm = new LibraryBrowserViewModel(host);
        var steam = Folder("Game", "Steam", new[] { "old.png", "shared.png" }, Older.Ticks);
        var ps5 = Folder("Game", "PS5", new[] { "new.png", "shared.png" }, Newer.Ticks);
        steam.IsFavorite = true; ps5.IsShowcase = ps5.IsCompleted100Percent = true;
        ps5.CompletedUtcTicks = Newer.Ticks;
        var view = Assert.Single(vm.BuildLibraryBrowserFolderViews(new[] { steam, ps5 }, "all"));
        Assert.Equal(3, view.FileCount);
        Assert.Equal(new[] { "new.png", "old.png", "shared.png" }, view.FilePaths.OrderBy(p => p));
        Assert.Equal("new.png", view.PreviewImagePath);
        Assert.Equal(Newer.Ticks, view.NewestCaptureUtcTicks);
        Assert.Equal(Newer.Ticks + 100, view.NewestRecentSortUtcTicks);
        Assert.Equal("42", view.SteamAppId);
        Assert.True(view.IsMergedAcrossPlatforms && view.IsFavorite && view.IsShowcase && view.IsCompleted100Percent);
        Assert.Contains("remember this run", view.SearchBlob);
        Assert.Equal(0, host.LoadLibraryMetadataIndexCallCount);
        Assert.Equal(0, host.ResolveDateWithIndexCount + host.ResolveDateWithoutIndexCount);
        Assert.Equal(0, host.ResolveRecentWithIndexCount + host.ResolveRecentWithoutIndexCount);
    }

    [Fact]
    public void IncompleteSummary_ReadsOnlyFallbackPaths_AndKeepsDatePreviewFallbacks()
    {
        var host = new LibraryBrowserViewModelTests.StubHost();
        var complete = Folder("Complete", "Steam", new[] { "unrelated.png" }, Newer.Ticks);
        var legacy = Folder("Legacy", "PS5", new[] { "old.png", "new.png" }, 0);
        legacy.NewestRecentSortUtcTicks = 0;
        legacy.PreviewImagePath = "";
        host.MetadataIndex["old.png"] = new() { FilePath = "old.png", CaptureUtcTicks = Older.Ticks, IndexAddedUtcTicks = Older.Ticks };
        host.MetadataIndex["new.png"] = new() { FilePath = "new.png", CaptureUtcTicks = Newer.Ticks, IndexAddedUtcTicks = Newer.Ticks + 200 };
        var views = new LibraryBrowserViewModel(host).BuildLibraryBrowserFolderViews(new[] { complete, legacy }, "all");
        var fallback = views.Single(v => v.Name == "Legacy");
        Assert.Equal(new[] { "new.png", "old.png" }, fallback.FilePaths);
        Assert.Equal("new.png", fallback.PreviewImagePath);
        Assert.Equal(Newer.Ticks, fallback.NewestCaptureUtcTicks);
        Assert.Equal(Newer.Ticks + 200, fallback.NewestRecentSortUtcTicks);
        Assert.Equal(1, host.LoadLibraryMetadataIndexCallCount);
        Assert.Equal(0, host.FullMetadataIndexCallCount);
        Assert.Equal(new[] { "new.png", "old.png" }, host.RequestedMetadataPaths.OrderBy(p => p));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ChronologicalViews_SortSelectedMembershipOnEntry(bool sessions)
    {
        var host = new LibraryBrowserViewModelTests.StubHost();
        var vm = new LibraryBrowserViewModel(host);
        host.MetadataIndex["old.png"] = new() { FilePath = "old.png", CaptureUtcTicks = Older.Ticks };
        host.MetadataIndex["new.png"] = new() { FilePath = "new.png", CaptureUtcTicks = Newer.Ticks };
        var views = vm.BuildLibraryBrowserFolderViews(new[]
        {
            Folder("Shown", "Steam", new[] { "old.png", "new.png" }, Newer.Ticks),
            Folder("Hidden", "PS5", new[] { "hidden.png" }, Newer.Ticks)
        }, "all");
        Assert.Equal(0, host.LoadLibraryMetadataIndexCallCount);
        var visible = views.Where(v => v.Name == "Shown");
        var chronology = sessions ? vm.BuildLibraryBrowserSessionView(visible, 90) : vm.BuildLibraryBrowserTimelineView(visible);
        Assert.Equal(new[] { "new.png", "old.png" }, chronology.FilePaths);
        Assert.Equal(Newer.Ticks, chronology.NewestCaptureUtcTicks);
        Assert.DoesNotContain("hidden.png", chronology.FilePaths);
        Assert.Equal(1, host.FullMetadataIndexCallCount);
    }
}
