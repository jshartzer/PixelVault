#nullable enable
using System;
using System.Collections.Generic;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests
{
    /// <summary>
    /// PV-PLN-UI-001 Step 13 Pass B: focused tests for the pure static helpers extracted from the
    /// MainWindow.LibraryBrowserViewModel partial. We exercise the timeline date math, the merged
    /// "All" id-picking tail, and the projection fingerprint (the part of the contract that the
    /// projection cache will lean on in Pass C).
    /// </summary>
    public class LibraryBrowserViewModelMathTests
    {
        [Fact]
        public void NormalizeLibraryTimelineDateRange_DefaultsToTodayAndSwapsInvertedRange()
        {
            DateTime start = DateTime.MinValue;
            DateTime end = DateTime.MinValue;
            LibraryBrowserViewModelMath.NormalizeLibraryTimelineDateRange(ref start, ref end);
            Assert.Equal(DateTime.Today, start);
            Assert.Equal(DateTime.Today, end);

            start = new DateTime(2025, 5, 10);
            end = new DateTime(2025, 5, 1);
            LibraryBrowserViewModelMath.NormalizeLibraryTimelineDateRange(ref start, ref end);
            Assert.Equal(new DateTime(2025, 5, 1), start);
            Assert.Equal(new DateTime(2025, 5, 10), end);
        }

        [Theory]
        [InlineData("today", 0, 0)]
        [InlineData("month", -10, 0)]
        [InlineData("30d", -29, 0)]
        [InlineData("last-30-days", -29, 0)]
        [InlineData("unknown", -29, 0)]
        public void BuildLibraryTimelinePresetDateRange_MatchesPresetWindows(string preset, int expectedStartOffsetDays, int expectedEndOffsetDays)
        {
            var reference = new DateTime(2025, 5, 11);
            LibraryBrowserViewModelMath.BuildLibraryTimelinePresetDateRange(preset, reference, out var start, out var end);
            if (preset == "month")
            {
                Assert.Equal(new DateTime(2025, 5, 1), start);
                Assert.Equal(reference.Date, end);
            }
            else
            {
                Assert.Equal(reference.AddDays(expectedStartOffsetDays).Date, start);
                Assert.Equal(reference.AddDays(expectedEndOffsetDays).Date, end);
            }
        }

        [Fact]
        public void DetectLibraryTimelinePresetKey_RoundTripsPresets()
        {
            var reference = new DateTime(2025, 6, 15);
            foreach (var key in new[] { "today", "month", "30d" })
            {
                LibraryBrowserViewModelMath.BuildLibraryTimelinePresetDateRange(key, reference, out var start, out var end);
                var detected = LibraryBrowserViewModelMath.DetectLibraryTimelinePresetKey(start, end, reference);
                Assert.Equal(key, detected);
            }

            var detectedCustom = LibraryBrowserViewModelMath.DetectLibraryTimelinePresetKey(
                new DateTime(2025, 1, 5),
                new DateTime(2025, 2, 9),
                reference);
            Assert.Equal("custom", detectedCustom);
        }

        [Fact]
        public void LibraryTimelineRangeContainsCapture_RespectsBoundaries()
        {
            var start = new DateTime(2025, 5, 1);
            var end = new DateTime(2025, 5, 31);
            Assert.True(LibraryBrowserViewModelMath.LibraryTimelineRangeContainsCapture(new DateTime(2025, 5, 15, 12, 0, 0), start, end));
            Assert.True(LibraryBrowserViewModelMath.LibraryTimelineRangeContainsCapture(start, start, end));
            Assert.True(LibraryBrowserViewModelMath.LibraryTimelineRangeContainsCapture(end, start, end));
            Assert.False(LibraryBrowserViewModelMath.LibraryTimelineRangeContainsCapture(start.AddDays(-1), start, end));
            Assert.False(LibraryBrowserViewModelMath.LibraryTimelineRangeContainsCapture(end.AddDays(1), start, end));
            Assert.False(LibraryBrowserViewModelMath.LibraryTimelineRangeContainsCapture(DateTime.MinValue, start, end));
        }

        [Fact]
        public void BuildLibraryTimelineSummaryText_AppendsCountsAndRange()
        {
            var oneDay = LibraryBrowserViewModelMath.BuildLibraryTimelineSummaryText(
                3, 2, 1,
                new DateTime(2025, 5, 1),
                new DateTime(2025, 5, 1));
            Assert.Equal("3 photos | 2 games | 1 platform | May 1, 2025", oneDay);

            var span = LibraryBrowserViewModelMath.BuildLibraryTimelineSummaryText(
                1, 0, 0,
                new DateTime(2025, 5, 31),
                new DateTime(2025, 5, 1));
            Assert.Equal("1 photo | May 1, 2025 - May 31, 2025", span);
        }

        [Fact]
        public void BuildLibraryTimelineDayCardTitle_LabelsTodayYesterdayElseFormatted()
        {
            var today = new DateTime(2025, 7, 4);
            Assert.Equal("Today", LibraryBrowserViewModelMath.BuildLibraryTimelineDayCardTitle(today, today));
            Assert.Equal("Yesterday", LibraryBrowserViewModelMath.BuildLibraryTimelineDayCardTitle(today.AddDays(-1), today));
            Assert.Equal(today.AddDays(-7).ToString("ddd, MMM d"), LibraryBrowserViewModelMath.BuildLibraryTimelineDayCardTitle(today.AddDays(-7), today));
            Assert.Equal(string.Empty, LibraryBrowserViewModelMath.BuildLibraryTimelineDayCardTitle(DateTime.MinValue, today));
        }

        [Fact]
        public void CalculateLibraryTimelinePackedTileSize_ClampsToScaledRange()
        {
            var smallScreen = LibraryBrowserViewModelMath.CalculateLibraryTimelinePackedTileSize(0, 480d);
            Assert.True(smallScreen >= (int)Math.Round(144 * 1.75));
            var bigScreen = LibraryBrowserViewModelMath.CalculateLibraryTimelinePackedTileSize(int.MaxValue, 1900d);
            Assert.Equal((int)Math.Round(280 * 1.75), bigScreen);
        }

        [Fact]
        public void BuildLibraryTimelinePackedRows_PacksByAvailableWidth()
        {
            var widths = new[] { 200d, 200d, 200d, 200d };
            var rows = LibraryBrowserViewModelMath.BuildLibraryTimelinePackedRows(widths, 460d, 8d);
            Assert.Equal(2, rows.Count);
            Assert.Equal(2, rows[0].Count);
            Assert.Equal(2, rows[1].Count);

            var emptyRows = LibraryBrowserViewModelMath.BuildLibraryTimelinePackedRows(null, 1000d, 8d);
            Assert.Empty(emptyRows);
        }

        [Fact]
        public void LibraryDetailFileLayoutHash_IsStableAcrossCase()
        {
            Assert.Equal(0, LibraryBrowserViewModelMath.LibraryDetailFileLayoutHash(null));
            Assert.Equal(0, LibraryBrowserViewModelMath.LibraryDetailFileLayoutHash(string.Empty));
            var lower = LibraryBrowserViewModelMath.LibraryDetailFileLayoutHash("c:/library/foo/bar.jpg");
            var upper = LibraryBrowserViewModelMath.LibraryDetailFileLayoutHash("C:/Library/FOO/BAR.JPG");
            Assert.Equal(lower, upper);
        }

        [Fact]
        public void ResolveLibraryVariableDetailTileWidth_ClampsAndRoundsToTwelve()
        {
            for (var i = 0; i < 24; i++)
            {
                var width = LibraryBrowserViewModelMath.ResolveLibraryVariableDetailTileWidth("file_" + i + ".png", 240, 96, 360);
                Assert.True(width >= 96 && width <= 360);
                Assert.Equal(0, width % 12);
            }
        }

        [Fact]
        public void PackLibraryDetailFilesIntoVariableRows_FlowsAcrossRows()
        {
            var files = new[] { "a.png", "b.png", "c.png", "d.png", "e.png" };
            var rows = LibraryBrowserViewModelMath.PackLibraryDetailFilesIntoVariableRows(files, 600d, 6, 240, 144, 320);
            Assert.NotEmpty(rows);
            var totalFiles = 0;
            foreach (var row in rows) totalFiles += row.Count;
            Assert.Equal(files.Length, totalFiles);
        }

        [Fact]
        public void ComputeLibraryBrowserFoldersMergeFingerprint_StableForUnchangedInput_ChangesOnEdit()
        {
            var folders = new List<LibraryFolderInfo>
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
                    PreviewImagePath = "C:/lib/A/cover.jpg",
                    SteamAppId = "1234",
                    FilePaths = new[] { "C:/lib/A/1.png", "C:/lib/A/2.png" }
                },
                new LibraryFolderInfo
                {
                    FolderPath = "C:/lib/B",
                    Name = "Game B",
                    PlatformLabel = "PC",
                    FileCount = 1,
                    NewestCaptureUtcTicks = 50,
                    FilePaths = new[] { "C:/lib/B/x.png" }
                }
            };

            var fp1 = LibraryBrowserViewModelMath.ComputeLibraryBrowserFoldersMergeFingerprint(folders);
            var fp2 = LibraryBrowserViewModelMath.ComputeLibraryBrowserFoldersMergeFingerprint(folders);
            Assert.Equal(fp1, fp2);

            folders[0].FileCount = 99;
            var fp3 = LibraryBrowserViewModelMath.ComputeLibraryBrowserFoldersMergeFingerprint(folders);
            Assert.NotEqual(fp1, fp3);

            Assert.Equal(0, LibraryBrowserViewModelMath.ComputeLibraryBrowserFoldersMergeFingerprint(null));
            Assert.Equal(0, LibraryBrowserViewModelMath.ComputeLibraryBrowserFoldersMergeFingerprint(new List<LibraryFolderInfo>()));
        }

        [Fact]
        public void MergeLibraryBrowserExternalIdsForCombinedView_PrefersSteamThenAgreement()
        {
            var folders = new[]
            {
                new LibraryFolderInfo { PlatformLabel = "Steam", SteamAppId = "1111" },
                new LibraryFolderInfo { PlatformLabel = "PC", SteamAppId = "9999" }
            };
            var picked = LibraryBrowserViewModelMath.MergeLibraryBrowserExternalIdsForCombinedView(
                folders,
                folder => folder.SteamAppId,
                static label => label ?? string.Empty);
            Assert.Equal("1111", picked);

            var foldersAgree = new[]
            {
                new LibraryFolderInfo { PlatformLabel = "PC", SteamGridDbId = "55" },
                new LibraryFolderInfo { PlatformLabel = "Xbox", SteamGridDbId = "55" }
            };
            var pickedAgree = LibraryBrowserViewModelMath.MergeLibraryBrowserExternalIdsForCombinedView(
                foldersAgree,
                folder => folder.SteamGridDbId,
                static label => label ?? string.Empty);
            Assert.Equal("55", pickedAgree);

            var foldersDisagree = new[]
            {
                new LibraryFolderInfo { PlatformLabel = "PC", SteamGridDbId = "55" },
                new LibraryFolderInfo { PlatformLabel = "Xbox", SteamGridDbId = "77" }
            };
            var pickedDisagree = LibraryBrowserViewModelMath.MergeLibraryBrowserExternalIdsForCombinedView(
                foldersDisagree,
                folder => folder.SteamGridDbId,
                static label => label ?? string.Empty);
            Assert.Equal(string.Empty, pickedDisagree);

            Assert.Equal(string.Empty, LibraryBrowserViewModelMath.MergeLibraryBrowserExternalIdsForCombinedView(null, folder => folder.SteamAppId, null));
        }

        [Fact]
        public void MergeLibraryBrowserNonSteamIdForCombinedView_PrefersEmulationThenAgreement()
        {
            var folders = new[]
            {
                new LibraryFolderInfo { PlatformLabel = "Emulation", NonSteamId = "abc-1" },
                new LibraryFolderInfo { PlatformLabel = "Steam", NonSteamId = "abc-2" }
            };
            var picked = LibraryBrowserViewModelMath.MergeLibraryBrowserNonSteamIdForCombinedView(
                folders,
                static label => label ?? string.Empty);
            Assert.Equal("abc-1", picked);
        }

        [Fact]
        public void MergeLibraryBrowserRetroAchievementsGameIdForCombinedView_OnlyWhenAllAgree()
        {
            var sameId = new[]
            {
                new LibraryFolderInfo { RetroAchievementsGameId = "10" },
                new LibraryFolderInfo { RetroAchievementsGameId = "10" }
            };
            Assert.Equal("10", LibraryBrowserViewModelMath.MergeLibraryBrowserRetroAchievementsGameIdForCombinedView(sameId));

            var diff = new[]
            {
                new LibraryFolderInfo { RetroAchievementsGameId = "10" },
                new LibraryFolderInfo { RetroAchievementsGameId = "11" }
            };
            Assert.Equal(string.Empty, LibraryBrowserViewModelMath.MergeLibraryBrowserRetroAchievementsGameIdForCombinedView(diff));
        }

        [Fact]
        public void MergeLibraryBrowserCollectionNotesForCombinedView_ReturnsFirstNonEmpty()
        {
            var folders = new[]
            {
                new LibraryFolderInfo { CollectionNotes = string.Empty },
                new LibraryFolderInfo { CollectionNotes = "  " },
                new LibraryFolderInfo { CollectionNotes = "favorites" },
                new LibraryFolderInfo { CollectionNotes = "second" }
            };
            Assert.Equal("favorites", LibraryBrowserViewModelMath.MergeLibraryBrowserCollectionNotesForCombinedView(folders));
            Assert.Equal(string.Empty, LibraryBrowserViewModelMath.MergeLibraryBrowserCollectionNotesForCombinedView(null));
            Assert.Equal(string.Empty, LibraryBrowserViewModelMath.MergeLibraryBrowserCollectionNotesForCombinedView(Array.Empty<LibraryFolderInfo>()));
        }
    }
}
