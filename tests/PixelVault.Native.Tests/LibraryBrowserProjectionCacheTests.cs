#nullable enable
using System;
using System.Collections.Generic;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests
{
    /// <summary>
    /// PV-PLN-UI-001 Step 13 Pass C: tests for the merged "All" projection cache extracted from
    /// the MainWindow.LibraryBrowserViewModel partial. We verify the key behaviors the cache
    /// must preserve relative to the prior in-MainWindow implementation:
    ///   1. cache hit on identical fingerprint
    ///   2. cache miss on any field that <c>ComputeLibraryBrowserFoldersMergeFingerprint</c> covers
    ///   3. console mode never caches (always rebuilds + clears)
    ///   4. <c>Reset</c> drops the cached projection
    /// </summary>
    public class LibraryBrowserProjectionCacheTests
    {
        static string NormalizeAllOrConsole(string? value) =>
            string.Equals(value, "console", StringComparison.OrdinalIgnoreCase) ? "console" : "all";

        static List<LibraryFolderInfo> MakeFolders() => new()
        {
            new LibraryFolderInfo
            {
                FolderPath = "C:/lib/A",
                Name = "Game A",
                GameId = "game-a",
                PlatformLabel = "Steam",
                FileCount = 5,
                NewestCaptureUtcTicks = 100,
                NewestRecentSortUtcTicks = 200,
                FilePaths = new[] { "C:/lib/A/1.png" }
            },
            new LibraryFolderInfo
            {
                FolderPath = "C:/lib/B",
                Name = "Game B",
                PlatformLabel = "PC",
                FileCount = 1,
                FilePaths = new[] { "C:/lib/B/x.png" }
            }
        };

        [Fact]
        public void GetOrBuild_CachesAcrossIdenticalCalls()
        {
            var cache = new LibraryBrowserProjectionCache();
            var folders = MakeFolders();
            var buildCount = 0;

            var first = cache.GetOrBuild(
                folders,
                "all",
                NormalizeAllOrConsole,
                (raw, mode) =>
                {
                    buildCount++;
                    return new List<LibraryBrowserFolderView> { new LibraryBrowserFolderView { ViewKey = "v1" } };
                });
            var second = cache.GetOrBuild(
                folders,
                "all",
                NormalizeAllOrConsole,
                (raw, mode) =>
                {
                    buildCount++;
                    return new List<LibraryBrowserFolderView> { new LibraryBrowserFolderView { ViewKey = "v2" } };
                });

            Assert.Equal(1, buildCount);
            Assert.Same(first, second);
            Assert.True(cache.HasCachedProjection);
        }

        [Fact]
        public void GetOrBuild_RebuildsWhenFingerprintChanges()
        {
            var cache = new LibraryBrowserProjectionCache();
            var folders = MakeFolders();
            var buildCount = 0;

            cache.GetOrBuild(folders, "all", NormalizeAllOrConsole, (raw, mode) => { buildCount++; return new List<LibraryBrowserFolderView>(); });
            folders[0].FileCount = 999;
            cache.GetOrBuild(folders, "all", NormalizeAllOrConsole, (raw, mode) => { buildCount++; return new List<LibraryBrowserFolderView>(); });

            Assert.Equal(2, buildCount);
        }

        [Fact]
        public void GetOrBuild_ConsoleGroupingNeverCachesAndClearsExistingCache()
        {
            var cache = new LibraryBrowserProjectionCache();
            var folders = MakeFolders();

            cache.GetOrBuild(folders, "all", NormalizeAllOrConsole, (raw, mode) => new List<LibraryBrowserFolderView> { new LibraryBrowserFolderView() });
            Assert.True(cache.HasCachedProjection);

            var consoleBuildCount = 0;
            cache.GetOrBuild(folders, "console", NormalizeAllOrConsole, (raw, mode) => { consoleBuildCount++; return new List<LibraryBrowserFolderView>(); });
            cache.GetOrBuild(folders, "console", NormalizeAllOrConsole, (raw, mode) => { consoleBuildCount++; return new List<LibraryBrowserFolderView>(); });
            Assert.Equal(2, consoleBuildCount);
            Assert.False(cache.HasCachedProjection);
            Assert.Equal(long.MinValue, cache.CachedFingerprint);
        }

        [Fact]
        public void GetOrBuild_ReturnsBuilderResultForIdenticalFingerprintAfterConsoleClear()
        {
            var cache = new LibraryBrowserProjectionCache();
            var folders = MakeFolders();
            var buildCount = 0;

            cache.GetOrBuild(folders, "all", NormalizeAllOrConsole, (raw, mode) => { buildCount++; return new List<LibraryBrowserFolderView>(); });
            cache.GetOrBuild(folders, "console", NormalizeAllOrConsole, (raw, mode) => { buildCount++; return new List<LibraryBrowserFolderView>(); });
            cache.GetOrBuild(folders, "all", NormalizeAllOrConsole, (raw, mode) => { buildCount++; return new List<LibraryBrowserFolderView>(); });
            cache.GetOrBuild(folders, "all", NormalizeAllOrConsole, (raw, mode) => { buildCount++; return new List<LibraryBrowserFolderView>(); });

            Assert.Equal(3, buildCount);
        }

        [Fact]
        public void Reset_DropsCachedProjection()
        {
            var cache = new LibraryBrowserProjectionCache();
            var folders = MakeFolders();

            cache.GetOrBuild(folders, "all", NormalizeAllOrConsole, (raw, mode) => new List<LibraryBrowserFolderView> { new LibraryBrowserFolderView() });
            Assert.True(cache.HasCachedProjection);

            cache.Reset();
            Assert.False(cache.HasCachedProjection);
            Assert.Equal(long.MinValue, cache.CachedFingerprint);
        }

        [Fact]
        public void GetOrBuild_NullFolders_FingerprintZeroStillCaches()
        {
            var cache = new LibraryBrowserProjectionCache();
            var buildCount = 0;
            cache.GetOrBuild(null, "all", NormalizeAllOrConsole, (raw, mode) => { buildCount++; return new List<LibraryBrowserFolderView>(); });
            cache.GetOrBuild(null, "all", NormalizeAllOrConsole, (raw, mode) => { buildCount++; return new List<LibraryBrowserFolderView>(); });
            Assert.Equal(1, buildCount);
        }

        [Fact]
        public void GetOrBuild_RejectsNullDelegates()
        {
            var cache = new LibraryBrowserProjectionCache();
            Assert.Throws<ArgumentNullException>(() => cache.GetOrBuild(null, "all", null!, (raw, mode) => new List<LibraryBrowserFolderView>()));
            Assert.Throws<ArgumentNullException>(() => cache.GetOrBuild(null, "all", NormalizeAllOrConsole, null!));
        }
    }
}
