#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests
{
    /// <summary>
    /// PV-PLN-UI-001 Step 14 Pass B: tests that construct <see cref="LibraryBrowserViewModel"/> directly
    /// against an in-memory <see cref="ILibraryBrowserViewModelHost"/> stub — no <see cref="MainWindow"/>,
    /// no WPF. This is the iOS-alignment payoff: the same read-model code can be exercised from a plain
    /// class with in-memory state, so a future iOS view-model or backend projection host can plug the
    /// same logic into its own shell.
    /// </summary>
    public class LibraryBrowserViewModelTests
    {
        /// <summary>
        /// Minimal host. Implements the interface with plain-data stand-ins for MainWindow helpers —
        /// enough to exercise the VM without any WPF / filesystem state. Live-read properties
        /// (<see cref="LibraryRoot"/>, <see cref="LibraryGroupingMode"/>) are plain settable fields so
        /// tests can flip the grouping mode mid-flight and verify the VM observes the change.
        /// </summary>
        internal sealed class StubHost : ILibraryBrowserViewModelHost
        {
            public string LibraryRoot { get; set; } = "C:/lib";
            public string LibraryGroupingMode { get; set; } = "all";

            public readonly Dictionary<string, DateTime> FileCaptureDates =
                new(StringComparer.OrdinalIgnoreCase);
            public readonly Dictionary<string, long> FileRecentSortTicks =
                new(StringComparer.OrdinalIgnoreCase);

            public LibraryFolderInfo CloneLibraryFolderInfo(LibraryFolderInfo folder)
            {
                if (folder == null) return null!;
                return new LibraryFolderInfo
                {
                    GameId = folder.GameId,
                    Name = folder.Name,
                    FolderPath = folder.FolderPath,
                    FileCount = folder.FileCount,
                    PreviewImagePath = folder.PreviewImagePath,
                    PlatformLabel = folder.PlatformLabel,
                    FilePaths = folder.FilePaths == null ? new string[0] : folder.FilePaths.ToArray(),
                    NewestCaptureUtcTicks = folder.NewestCaptureUtcTicks,
                    NewestRecentSortUtcTicks = folder.NewestRecentSortUtcTicks,
                    SteamAppId = folder.SteamAppId,
                    NonSteamId = folder.NonSteamId,
                    SteamGridDbId = folder.SteamGridDbId,
                    RetroAchievementsGameId = folder.RetroAchievementsGameId,
                    SuppressSteamAppIdAutoResolve = folder.SuppressSteamAppIdAutoResolve,
                    SuppressSteamGridDbIdAutoResolve = folder.SuppressSteamGridDbIdAutoResolve,
                    IsCompleted100Percent = folder.IsCompleted100Percent,
                    CompletedUtcTicks = folder.CompletedUtcTicks,
                    CollectionNotes = folder.CollectionNotes,
                    PendingGameAssignment = folder.PendingGameAssignment
                };
            }

            public bool SameLibraryFolderSelection(LibraryFolderInfo left, LibraryFolderInfo right)
                => ReferenceEquals(left, right) ||
                   (left != null && right != null &&
                    string.Equals(left.FolderPath ?? "", right.FolderPath ?? "", StringComparison.OrdinalIgnoreCase));

            public DateTime GetLibraryFolderNewestDate(LibraryFolderInfo folder)
            {
                if (folder == null || folder.NewestCaptureUtcTicks <= 0) return DateTime.MinValue;
                return new DateTime(folder.NewestCaptureUtcTicks, DateTimeKind.Utc).ToLocalTime();
            }

            public string NormalizeGameId(string value) =>
                (value ?? string.Empty).Trim().ToLowerInvariant();

            public string NormalizeGameIndexName(string name, string folderPath = null!) =>
                (name ?? string.Empty).Trim().ToLowerInvariant();

            public string GuessGameIndexNameForFile(string file) =>
                string.Empty;

            public string PrimaryPlatformLabel(string file) =>
                string.Empty;

            public DateTime ResolveIndexedLibraryDate(string libraryRoot, string file, Dictionary<string, LibraryMetadataIndexEntry> index = null!)
            {
                if (string.IsNullOrEmpty(file)) return DateTime.MinValue;
                return FileCaptureDates.TryGetValue(file, out var dt) ? dt : DateTime.MinValue;
            }

            public LibraryMetadataIndexEntry TryGetLibraryMetadataIndexEntry(string libraryRoot, string file, Dictionary<string, LibraryMetadataIndexEntry> index) => null!;

            public long ResolveLibraryFileRecentSortUtcTicks(string libraryRoot, string file, Dictionary<string, LibraryMetadataIndexEntry> index = null!)
            {
                if (string.IsNullOrEmpty(file)) return 0;
                return FileRecentSortTicks.TryGetValue(file, out var t) ? t : 0;
            }

            public string FormatViewKeyForTroubleshooting(string viewKey) => viewKey ?? string.Empty;
            public string FormatPathForTroubleshooting(string path) => path ?? string.Empty;
        }

        static LibraryFolderInfo MakeFolder(string name, string platform, string folderPath, int fileCount = 1, string gameId = "", string[]? files = null, long newestTicks = 0)
        {
            return new LibraryFolderInfo
            {
                GameId = gameId,
                Name = name,
                FolderPath = folderPath,
                PlatformLabel = platform,
                FileCount = fileCount,
                FilePaths = files ?? new[] { folderPath + "/preview.png" },
                PreviewImagePath = (files != null && files.Length > 0) ? files[0] : (folderPath + "/preview.png"),
                NewestCaptureUtcTicks = newestTicks,
                NewestRecentSortUtcTicks = newestTicks
            };
        }

        [Fact]
        public void ConsoleGrouping_ProducesOneViewPerFolder()
        {
            var host = new StubHost { LibraryGroupingMode = "console" };
            var vm = new LibraryBrowserViewModel(host);
            var folders = new List<LibraryFolderInfo>
            {
                MakeFolder("Game A", "Steam", "C:/lib/A"),
                MakeFolder("Game A", "PS5", "C:/lib/A-ps5"),
                MakeFolder("Game B", "PC", "C:/lib/B")
            };

            var views = vm.BuildLibraryBrowserFolderViews(folders, "console");

            Assert.Equal(3, views.Count);
            Assert.All(views, v => Assert.False(v.IsMergedAcrossPlatforms, "console views must not be cross-platform merged"));
            Assert.Equal(new[] { "Steam", "PS5", "PC" }, views.Select(v => v.PrimaryPlatformLabel).ToArray());
        }

        [Fact]
        public void AllGrouping_MergesFoldersByNameAcrossPlatforms()
        {
            var host = new StubHost { LibraryGroupingMode = "all" };
            var vm = new LibraryBrowserViewModel(host);
            var folders = new List<LibraryFolderInfo>
            {
                MakeFolder("Hades", "Steam", "C:/lib/hades-steam", fileCount: 5),
                MakeFolder("Hades", "PS5",   "C:/lib/hades-ps5",   fileCount: 3),
                MakeFolder("Celeste", "PC",  "C:/lib/celeste",     fileCount: 2)
            };

            var views = vm.BuildLibraryBrowserFolderViews(folders, "all");

            Assert.Equal(2, views.Count);
            var hades = views.Single(v => string.Equals(v.Name, "Hades", StringComparison.OrdinalIgnoreCase));
            Assert.True(hades.IsMergedAcrossPlatforms);
            Assert.Equal(new[] { "Steam", "PS5" }.OrderBy(s => s), hades.PlatformLabels.OrderBy(s => s));
            Assert.Equal(2, hades.SourceFolders.Count);

            var celeste = views.Single(v => string.Equals(v.Name, "Celeste", StringComparison.OrdinalIgnoreCase));
            Assert.False(celeste.IsMergedAcrossPlatforms);
            Assert.Single(celeste.SourceFolders);
        }

        [Fact]
        public void BuildViewKey_DiffersBetweenConsoleAndAllGroupings()
        {
            var vm = new LibraryBrowserViewModel(new StubHost());
            var consoleKey = vm.BuildLibraryBrowserViewKey("console", "id-1", "Hades", "C:/lib/hades", "Steam");
            var allKey = vm.BuildLibraryBrowserViewKey("all", "id-1", "Hades", "C:/lib/hades", "Steam");
            Assert.NotEqual(consoleKey, allKey);
            Assert.StartsWith("console|", consoleKey);
            Assert.StartsWith("all|", allKey);
        }

        [Fact]
        public void PlatformSummary_ReflectsMultiplicity()
        {
            var vm = new LibraryBrowserViewModel(new StubHost());
            Assert.Equal("Other", vm.BuildLibraryBrowserPlatformSummary(new string[0]));
            Assert.Equal("Steam", vm.BuildLibraryBrowserPlatformSummary(new[] { "Steam" }));
            // two-platform case: format is "X + Y" -- order is platform-group-order dependent, so just assert shape.
            var two = vm.BuildLibraryBrowserPlatformSummary(new[] { "Steam", "PS5" });
            Assert.Contains(" + ", two);
            Assert.Equal("3 platforms", vm.BuildLibraryBrowserPlatformSummary(new[] { "Steam", "PS5", "Xbox" }));
        }

        [Fact]
        public void LiveHostRead_GroupingModeFlipIsObserved()
        {
            var host = new StubHost { LibraryGroupingMode = "all" };
            var vm = new LibraryBrowserViewModel(host);
            Assert.False(vm.ShouldShowLibraryBrowserPlatformContext());
            host.LibraryGroupingMode = "console";
            Assert.True(vm.ShouldShowLibraryBrowserPlatformContext());
            Assert.False(vm.IsLibraryBrowserTimelineMode());
            host.LibraryGroupingMode = "timeline";
            Assert.True(vm.IsLibraryBrowserTimelineMode());
        }

        [Fact]
        public void TimelineView_FlattensImagesSortedByCaptureDateDescending()
        {
            var host = new StubHost();
            var older = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var newer = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc);
            host.FileCaptureDates["C:/lib/a/old.png"] = older;
            host.FileCaptureDates["C:/lib/b/new.png"] = newer;

            var vm = new LibraryBrowserViewModel(host);

            var folderA = MakeFolder("Game A", "Steam", "C:/lib/a", files: new[] { "C:/lib/a/old.png" });
            var folderB = MakeFolder("Game B", "PS5",   "C:/lib/b", files: new[] { "C:/lib/b/new.png" });
            var baseViews = vm.BuildLibraryBrowserFolderViews(new[] { folderA, folderB }, "all");

            var timeline = vm.BuildLibraryBrowserTimelineView(baseViews);

            Assert.True(timeline.IsTimelineProjection);
            Assert.Equal(2, timeline.FileCount);
            Assert.Equal(new[] { "C:/lib/b/new.png", "C:/lib/a/old.png" }, timeline.FilePaths);
            Assert.Equal("C:/lib/b/new.png", timeline.PreviewImagePath);
        }

        [Fact]
        public void SearchBlob_PopulatedFromSignificantFields_LowercaseAndJoined()
        {
            var vm = new LibraryBrowserViewModel(new StubHost());
            var view = new LibraryBrowserFolderView
            {
                Name = "Hades II",
                PlatformSummaryText = "Steam",
                PrimaryFolderPath = "C:/lib/Hades-II",
                GameId = "hades-ii",
                SteamAppId = "1145350"
            };

            vm.PopulateLibraryBrowserFolderViewSearchBlob(view);

            Assert.NotNull(view.SearchBlob);
            Assert.Equal(view.SearchBlob, view.SearchBlob!.ToLowerInvariant());
            Assert.Contains("hades ii", view.SearchBlob);
            Assert.Contains("1145350", view.SearchBlob);
            Assert.Contains("c:/lib/hades-ii", view.SearchBlob);
        }

        [Fact]
        public void FindMatchingLibraryBrowserView_FallsBackToGameIdThenName()
        {
            var host = new StubHost();
            var vm = new LibraryBrowserViewModel(host);
            var folderA1 = MakeFolder("Hades", "Steam", "C:/lib/hades-steam", gameId: "hades");
            var folderA2 = MakeFolder("Hades", "PS5",   "C:/lib/hades-ps5",   gameId: "hades");
            var folderB  = MakeFolder("Celeste", "PC",  "C:/lib/celeste",     gameId: "celeste");

            var prior = vm.BuildLibraryBrowserFolderViews(new[] { folderA1, folderB }, "console");
            var current = prior.First(v => v.GameId == "hades");
            var next = vm.BuildLibraryBrowserFolderViews(new[] { folderA2, folderB }, "console");

            var match = vm.FindMatchingLibraryBrowserView(current, next);

            Assert.NotNull(match);
            Assert.Equal("hades", match!.GameId);
            Assert.Equal("C:/lib/hades-ps5", match.PrimaryFolderPath);
        }

        [Fact]
        public void ProjectionCache_ReusesViewsAcrossIdenticalCalls()
        {
            var host = new StubHost { LibraryGroupingMode = "all" };
            var vm = new LibraryBrowserViewModel(host);
            var folders = new List<LibraryFolderInfo>
            {
                MakeFolder("Hades", "Steam", "C:/lib/hades", fileCount: 5, newestTicks: 100),
                MakeFolder("Celeste", "PC",  "C:/lib/celeste", fileCount: 2, newestTicks: 200)
            };

            var first = vm.GetOrBuildLibraryBrowserFolderViews(folders, "all");
            var second = vm.GetOrBuildLibraryBrowserFolderViews(folders, "all");

            Assert.Same(first, second);
        }

        [Fact]
        public void ProjectionCache_RebuildsWhenFolderFingerprintChanges()
        {
            var host = new StubHost { LibraryGroupingMode = "all" };
            var vm = new LibraryBrowserViewModel(host);
            var folders = new List<LibraryFolderInfo>
            {
                MakeFolder("Hades", "Steam", "C:/lib/hades", fileCount: 5, newestTicks: 100)
            };

            var first = vm.GetOrBuildLibraryBrowserFolderViews(folders, "all");
            folders[0].FileCount = 999;
            var second = vm.GetOrBuildLibraryBrowserFolderViews(folders, "all");

            Assert.NotSame(first, second);
        }

        [Fact]
        public void ScopeLabel_IncludesPlatformInConsoleMode_Only()
        {
            var host = new StubHost();
            var vm = new LibraryBrowserViewModel(host);
            var view = new LibraryBrowserFolderView { Name = "Hades", PlatformSummaryText = "Steam" };

            host.LibraryGroupingMode = "all";
            Assert.Equal("Hades", vm.BuildLibraryBrowserScopeLabel(view));

            host.LibraryGroupingMode = "console";
            Assert.Equal("Hades | Steam", vm.BuildLibraryBrowserScopeLabel(view));
        }

        [Fact]
        public void OpenFoldersLabel_SingularVersusPluralVersusTimeline()
        {
            var vm = new LibraryBrowserViewModel(new StubHost());
            var single = new LibraryBrowserFolderView { Name = "Hades" };
            single.SourceFolders.Add(new LibraryFolderInfo { FolderPath = "C:/lib/hades" });
            Assert.Equal("Open Folder", vm.BuildLibraryBrowserOpenFoldersLabel(single));

            var multi = new LibraryBrowserFolderView { Name = "Hades" };
            multi.SourceFolders.Add(new LibraryFolderInfo { FolderPath = "C:/lib/hades-steam" });
            multi.SourceFolders.Add(new LibraryFolderInfo { FolderPath = "C:/lib/hades-ps5" });
            Assert.Equal("Open Folders", vm.BuildLibraryBrowserOpenFoldersLabel(multi));

            var timeline = new LibraryBrowserFolderView { Name = "Timeline", IsTimelineProjection = true };
            Assert.Equal("Open Source Folders", vm.BuildLibraryBrowserOpenFoldersLabel(timeline));
        }

        [Fact]
        public void Ctor_RejectsNullHost()
        {
            Assert.Throws<ArgumentNullException>(() => new LibraryBrowserViewModel(null!));
        }
    }
}
