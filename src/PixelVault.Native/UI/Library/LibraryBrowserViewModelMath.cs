#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace PixelVaultNative
{
    /// <summary>
    /// PV-PLN-UI-001 Step 13 Pass B: pure static helpers that previously lived as
    /// <c>internal static</c> members on the <c>MainWindow.LibraryBrowserViewModel</c> partial.
    /// None of these touch <c>MainWindow</c> instance state — timeline date math, packed-card
    /// layout estimates, variable-tile sizing, the merged-projection fingerprint, and the
    /// "All" group merge tail (id picking, RetroAchievements, collection notes).
    ///
    /// iOS alignment: contract-shaped. Inputs are plain values + small DTOs (no WPF types).
    /// <see cref="MainWindow"/> keeps thin forwarders so existing call sites
    /// (<c>MainWindow.LibraryBrowserShowOrchestration</c>, <c>LibraryTimelineModeTests</c>,
    /// <c>LibraryBrowserCombinedMergeTests</c>) keep resolving without rewires.
    /// </summary>
    internal static class LibraryBrowserViewModelMath
    {
        public static void NormalizeLibraryTimelineDateRange(ref DateTime startDate, ref DateTime endDate)
        {
            if (startDate <= DateTime.MinValue) startDate = DateTime.Today;
            if (endDate <= DateTime.MinValue) endDate = startDate;
            startDate = startDate.Date;
            endDate = endDate.Date;
            if (endDate < startDate)
            {
                var swap = startDate;
                startDate = endDate;
                endDate = swap;
            }
        }

        public static void BuildLibraryTimelinePresetDateRange(string? presetKey, DateTime referenceDate, out DateTime startDate, out DateTime endDate)
        {
            var today = referenceDate <= DateTime.MinValue ? DateTime.Today : referenceDate.Date;
            switch ((presetKey ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "today":
                    startDate = today;
                    endDate = today;
                    break;
                case "month":
                case "this-month":
                    startDate = new DateTime(today.Year, today.Month, 1);
                    endDate = today;
                    break;
                case "30d":
                case "30-days":
                case "last-30-days":
                default:
                    startDate = today.AddDays(-29);
                    endDate = today;
                    break;
            }
            NormalizeLibraryTimelineDateRange(ref startDate, ref endDate);
        }

        public static string DetectLibraryTimelinePresetKey(DateTime startDate, DateTime endDate, DateTime referenceDate)
        {
            NormalizeLibraryTimelineDateRange(ref startDate, ref endDate);
            var today = referenceDate <= DateTime.MinValue ? DateTime.Today : referenceDate.Date;
            BuildLibraryTimelinePresetDateRange("today", today, out var presetStart, out var presetEnd);
            if (startDate == presetStart && endDate == presetEnd) return "today";
            BuildLibraryTimelinePresetDateRange("month", today, out presetStart, out presetEnd);
            if (startDate == presetStart && endDate == presetEnd) return "month";
            BuildLibraryTimelinePresetDateRange("30d", today, out presetStart, out presetEnd);
            if (startDate == presetStart && endDate == presetEnd) return "30d";
            return "custom";
        }

        public static bool LibraryTimelineRangeContainsCapture(DateTime captureDate, DateTime startDate, DateTime endDate)
        {
            if (captureDate <= DateTime.MinValue) return false;
            NormalizeLibraryTimelineDateRange(ref startDate, ref endDate);
            var captureDay = captureDate.Date;
            return captureDay >= startDate && captureDay <= endDate;
        }

        /// <summary>
        /// Rolling presets (Today / This month / 30 days) are anchored to the calendar day when chosen.
        /// If the library stays open past midnight, <paramref name="ws"/> still holds yesterday’s end
        /// date and new captures disappear from the timeline until the user re-applies a preset.
        /// Recompute start/end from <see cref="DateTime.Today"/> when the preset is not custom.
        /// </summary>
        public static bool TryAlignLibraryTimelineRollingPresetToToday(MainWindow.LibraryBrowserWorkingSet? ws)
        {
            if (ws == null) return false;
            var key = (ws.TimelineDatePresetKey ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(key) || string.Equals(key, "custom", StringComparison.OrdinalIgnoreCase)) return false;
            string presetArg;
            if (string.Equals(key, "today", StringComparison.OrdinalIgnoreCase)) presetArg = "today";
            else if (string.Equals(key, "month", StringComparison.OrdinalIgnoreCase)) presetArg = "month";
            else if (string.Equals(key, "30d", StringComparison.OrdinalIgnoreCase)) presetArg = "30d";
            else return false;

            BuildLibraryTimelinePresetDateRange(presetArg, DateTime.Today, out var start, out var end);
            var curStart = ws.TimelineStartDate <= DateTime.MinValue ? DateTime.MinValue : ws.TimelineStartDate.Date;
            var curEnd = ws.TimelineEndDate <= DateTime.MinValue ? DateTime.MinValue : ws.TimelineEndDate.Date;
            if (curStart == start && curEnd == end) return false;
            ws.TimelineStartDate = start;
            ws.TimelineEndDate = end;
            return true;
        }

        public static string BuildLibraryTimelineSummaryText(int captureCount, int gameCount, int platformCount, DateTime newestCapture, DateTime oldestCapture)
        {
            var parts = new List<string>
            {
                captureCount + " photo" + (captureCount == 1 ? string.Empty : "s")
            };
            if (gameCount > 0) parts.Add(gameCount + " game" + (gameCount == 1 ? string.Empty : "s"));
            if (platformCount > 0) parts.Add(platformCount + " platform" + (platformCount == 1 ? string.Empty : "s"));
            if (newestCapture > DateTime.MinValue && oldestCapture > DateTime.MinValue)
            {
                var rangeText = newestCapture.Date == oldestCapture.Date
                    ? newestCapture.ToString("MMMM d, yyyy")
                    : oldestCapture.ToString("MMMM d, yyyy") + " - " + newestCapture.ToString("MMMM d, yyyy");
                parts.Add(rangeText);
            }
            return string.Join(" | ", parts.Where(part => !string.IsNullOrWhiteSpace(part)));
        }

        public static string BuildLibraryTimelineCaptureTimeLabel(DateTime captureDate)
        {
            return captureDate <= DateTime.MinValue ? string.Empty : captureDate.ToString("h:mm tt");
        }

        public static string BuildLibraryTimelineDayCardTitle(DateTime captureDate, DateTime referenceDate)
        {
            if (captureDate <= DateTime.MinValue) return string.Empty;
            var today = referenceDate <= DateTime.MinValue ? DateTime.Today : referenceDate.Date;
            var captureDay = captureDate.Date;
            if (captureDay == today) return "Today";
            if (captureDay == today.AddDays(-1)) return "Yesterday";
            if (captureDay.Year == today.Year) return captureDay.ToString("ddd, MMM d");
            return captureDay.ToString("ddd, MMM d, yyyy");
        }

        public static int CalculateLibraryTimelinePackedTileSize(int detailTileSize, double availableWidth)
        {
            const double scale = 1.75d;
            var width = availableWidth <= 0 ? 1280d : availableWidth;
            var minTile = (int)Math.Round((width < 540d ? 144 : (width < 860d ? 160 : 180)) * scale);
            var maxTile = (int)Math.Round((width >= 1800d ? 280 : (width >= 1300d ? 256 : (width >= 960d ? 228 : 196))) * scale);
            var proposed = detailTileSize <= 0 ? maxTile : Math.Min(detailTileSize, maxTile);
            return Math.Max(minTile, Math.Min(maxTile, proposed));
        }

        public static int CalculateLibraryTimelinePackedCardColumns(int captureCount, double availableWidth)
        {
            if (captureCount <= 1) return 1;
            var widthBasedMax = availableWidth >= 1680d ? 3 : (availableWidth >= 980d ? 2 : 1);
            if (captureCount >= 6 && widthBasedMax >= 3) return 3;
            if (captureCount >= 3 && widthBasedMax >= 2) return 2;
            return Math.Max(1, Math.Min(captureCount, widthBasedMax));
        }

        public static double EstimateLibraryTimelinePackedCardWidth(int captureCount, int tileSize, double availableWidth)
        {
            return EstimateLibraryTimelinePackedCardWidth(
                captureCount,
                tileSize,
                availableWidth,
                CalculateLibraryTimelinePackedCardColumns(captureCount, availableWidth));
        }

        public static double EstimateLibraryTimelinePackedCardWidth(int captureCount, int tileSize, double availableWidth, int cardColumns)
        {
            const double innerGap = 8d;
            const double horizontalPadding = 24d;
            var normalizedColumns = Math.Max(1, Math.Min(Math.Max(1, captureCount), cardColumns));
            return Math.Max(220d, (normalizedColumns * tileSize) + ((normalizedColumns - 1) * innerGap) + horizontalPadding);
        }

        public static double EstimateLibraryTimelinePackedCardHeight(int captureCount, int tileSize, double availableWidth)
        {
            return EstimateLibraryTimelinePackedCardHeight(
                captureCount,
                tileSize,
                CalculateLibraryTimelinePackedCardColumns(Math.Max(1, captureCount), availableWidth));
        }

        public static double EstimateLibraryTimelinePackedCardHeight(int captureCount, int tileSize, int cardColumns)
        {
            const double headerHeight = 40d;
            const double verticalPadding = 24d;
            const double innerGap = 8d;
            var safeCaptureCount = Math.Max(1, captureCount);
            var normalizedColumns = Math.Max(1, Math.Min(safeCaptureCount, cardColumns));
            var rowCount = Math.Max(1, (int)Math.Ceiling(safeCaptureCount / (double)normalizedColumns));
            var estimatedTileHeight = Math.Max(260d, tileSize / 0.28d);
            return headerHeight + verticalPadding + (rowCount * estimatedTileHeight) + ((rowCount - 1) * innerGap);
        }

        public static List<List<int>> BuildLibraryTimelinePackedRows(IReadOnlyList<double>? cardWidths, double availableWidth, double interCardGap)
        {
            var rows = new List<List<int>>();
            if (cardWidths == null || cardWidths.Count == 0) return rows;
            var rowLimit = Math.Max(220d, availableWidth);
            var currentRow = new List<int>();
            var currentWidth = 0d;
            for (var i = 0; i < cardWidths.Count; i++)
            {
                var width = Math.Max(180d, cardWidths[i]);
                var neededWidth = currentRow.Count == 0 ? width : width + Math.Max(0d, interCardGap);
                if (currentRow.Count > 0 && currentWidth + neededWidth > rowLimit)
                {
                    rows.Add(currentRow);
                    currentRow = new List<int>();
                    currentWidth = 0d;
                    neededWidth = width;
                }
                currentRow.Add(i);
                currentWidth += neededWidth;
            }
            if (currentRow.Count > 0) rows.Add(currentRow);
            return rows;
        }

        public static double EstimateLibraryPackedDayCardDesiredWidth(int captureCount, double availableWidth, bool timelineView)
        {
            return EstimateLibraryPackedDayCardDesiredWidth(captureCount, availableWidth, timelineView, 0);
        }

        public static double EstimateLibraryPackedDayCardDesiredWidth(int captureCount, double availableWidth, bool timelineView, int detailTileSize)
        {
            var safeCaptureCount = Math.Max(1, captureCount);
            var width = availableWidth <= 0d ? 1280d : availableWidth;
            var targetTileWidth = detailTileSize > 0
                ? detailTileSize
                : (timelineView
                    ? CalculateLibraryTimelinePackedTileSize(360, width)
                    : (width >= 1450d ? 420 : (width >= 1080d ? 340 : 280)));
            var minCardWidth = timelineView
                ? Math.Max(320d, targetTileWidth * 1.2d)
                : Math.Max(280d, targetTileWidth * 1.45d);
            var preferredColumns = 1;
            if (safeCaptureCount >= (timelineView ? 4 : 3) && width >= (timelineView ? 1250d : 920d))
                preferredColumns = 2;
            if (timelineView && safeCaptureCount >= 10 && width >= 2200d)
                preferredColumns = 3;
            const double innerGap = 8d;
            const double horizontalPadding = 24d;
            var desiredWidth = horizontalPadding + (preferredColumns * targetTileWidth) + ((preferredColumns - 1) * innerGap);
            if (safeCaptureCount >= 8) desiredWidth += targetTileWidth * (timelineView ? 0.18d : 0.12d);
            return Math.Max(minCardWidth, Math.Min(width, desiredWidth));
        }

        public static List<double> ExpandLibraryPackedRowWidths(IReadOnlyList<double>? desiredWidths, double availableWidth, double interCardGap)
        {
            var expanded = new List<double>();
            if (desiredWidths == null || desiredWidths.Count == 0) return expanded;
            var widths = desiredWidths.Select(width => Math.Max(180d, width)).ToList();
            var totalGap = Math.Max(0, widths.Count - 1) * Math.Max(0d, interCardGap);
            var slack = Math.Max(0d, Math.Max(220d, availableWidth) - totalGap - widths.Sum());
            var extraPerCard = slack <= 0d ? 0d : slack / widths.Count;
            for (var i = 0; i < widths.Count; i++)
                expanded.Add(widths[i] + extraPerCard);
            return expanded;
        }

        public static int LibraryDetailFileLayoutHash(string? path)
        {
            if (string.IsNullOrEmpty(path)) return 0;
            unchecked
            {
                return Math.Abs(StringComparer.OrdinalIgnoreCase.GetHashCode(path));
            }
        }

        public static int ResolveLibraryVariableDetailTileWidth(string? file, int baseWidth, int minWidth, int maxWidth)
        {
            var scales = new[] { 0.76, 0.88, 0.96, 1.06, 1.18, 1.32 };
            var h = LibraryDetailFileLayoutHash(file);
            var s = scales[h % scales.Length];
            var w = (int)Math.Round(baseWidth * s / 12.0) * 12;
            return Math.Max(minWidth, Math.Min(maxWidth, w));
        }

        public static List<List<(string File, int Width)>> PackLibraryDetailFilesIntoVariableRows(
            IReadOnlyList<string>? files,
            double availableWidth,
            int gapPx,
            int baseWidth,
            int minWidth,
            int maxWidth)
        {
            var rows = new List<List<(string, int)>>();
            if (files == null || files.Count == 0) return rows;
            var avail = Math.Max(120d, availableWidth);
            var row = new List<(string, int)>();
            var rowUsed = 0d;
            foreach (var file in files)
            {
                if (string.IsNullOrWhiteSpace(file)) continue;
                var w = ResolveLibraryVariableDetailTileWidth(file, baseWidth, minWidth, maxWidth);
                w = Math.Min(w, (int)Math.Floor(avail));
                var extra = row.Count > 0 ? gapPx : 0;
                if (row.Count > 0 && rowUsed + extra + w > avail + 1.0)
                {
                    rows.Add(row);
                    row = new List<(string, int)>();
                    rowUsed = 0;
                    extra = 0;
                }
                if (extra > 0) rowUsed += extra;
                row.Add((file, w));
                rowUsed += w;
            }
            if (row.Count > 0) rows.Add(row);
            return rows;
        }

        public static int EstimateLibraryVariableDetailRowHeight(IReadOnlyList<(string File, int Width)>? row, bool includeTimelineFooter)
        {
            if (row == null || row.Count == 0) return 260;
            var maxH = 0;
            foreach (var pair in row)
            {
                // Aspect ratio cache lives in MainWindow.LibraryPhotoMasonryLayout (internal static). It
                // is not pure (caches measurements) but it's already an internal-static helper, so
                // calling through MainWindow keeps behavior identical without expanding its surface.
                var aspectRatio = MainWindow.ResolveLibraryDetailAspectRatio(pair.File, null);
                var h = (int)Math.Ceiling(pair.Width / aspectRatio);
                if (h > maxH) maxH = h;
            }
            return Math.Max(1, maxH);
        }

        /// <summary>Stable fingerprint of folder list order + merge-relevant fields; used to skip rebuilding merged "All" projection.</summary>
        public static long ComputeLibraryBrowserFoldersMergeFingerprint(IReadOnlyList<LibraryFolderInfo>? folders)
        {
            unchecked
            {
                if (folders == null || folders.Count == 0) return 0;
                long h = folders.Count;
                for (var i = 0; i < folders.Count; i++)
                {
                    var folder = folders[i];
                    if (folder == null)
                    {
                        h = h * 397 ^ i;
                        continue;
                    }
                    h = h * 397 ^ i;
                    h = h * 397 ^ (folder.FolderPath ?? string.Empty).GetHashCode(StringComparison.OrdinalIgnoreCase);
                    h = h * 397 ^ (folder.Name ?? string.Empty).GetHashCode(StringComparison.OrdinalIgnoreCase);
                    h = h * 397 ^ (folder.GameId ?? string.Empty).GetHashCode(StringComparison.OrdinalIgnoreCase);
                    h = h * 397 ^ (folder.PlatformLabel ?? string.Empty).GetHashCode(StringComparison.OrdinalIgnoreCase);
                    h = h * 397 ^ folder.FileCount;
                    h = h * 397 ^ folder.NewestCaptureUtcTicks;
                    h = h * 397 ^ folder.NewestRecentSortUtcTicks;
                    h = h * 397 ^ (folder.PreviewImagePath ?? string.Empty).GetHashCode(StringComparison.OrdinalIgnoreCase);
                    h = h * 397 ^ (folder.SteamAppId ?? string.Empty).GetHashCode(StringComparison.OrdinalIgnoreCase);
                    h = h * 397 ^ (folder.NonSteamId ?? string.Empty).GetHashCode(StringComparison.OrdinalIgnoreCase);
                    h = h * 397 ^ (folder.SteamGridDbId ?? string.Empty).GetHashCode(StringComparison.OrdinalIgnoreCase);
                    h = h * 397 ^ (folder.RetroAchievementsGameId ?? string.Empty).GetHashCode(StringComparison.OrdinalIgnoreCase);
                    h = h * 397 ^ (folder.SuppressSteamAppIdAutoResolve ? 1 : 0);
                    h = h * 397 ^ (folder.SuppressSteamGridDbIdAutoResolve ? 1 : 0);
                    h = h * 397 ^ (folder.PendingGameAssignment ? 1 : 0);
                    var paths = folder.FilePaths;
                    var len = paths == null ? 0 : paths.Length;
                    h = h * 397 ^ len;
                    if (len > 0)
                    {
                        h = h * 397 ^ (paths![0] ?? string.Empty).GetHashCode(StringComparison.OrdinalIgnoreCase);
                        if (len > 1) h = h * 397 ^ (paths[len - 1] ?? string.Empty).GetHashCode(StringComparison.OrdinalIgnoreCase);
                    }
                }
                return h;
            }
        }

        /// <summary>
        /// Picks one external id (Steam App / SteamGrid DB) for merged "All" rows. Previously we required a single distinct id across
        /// every source folder, which left merged tiles blank when Steam and another copy disagreed even though the Steam install had a valid id.
        /// Prefer Steam-labeled folders first; otherwise use a single id only when all non-empty values agree.
        /// </summary>
        public static string MergeLibraryBrowserExternalIdsForCombinedView(
            IReadOnlyList<LibraryFolderInfo>? sourceFolders,
            Func<LibraryFolderInfo, string>? pickId,
            Func<string, string>? normalizeConsoleLabel)
        {
            if (sourceFolders == null || sourceFolders.Count == 0 || pickId == null) return string.Empty;
            var normConsole = normalizeConsoleLabel ?? (s => s ?? string.Empty);
            var normSteam = normConsole("Steam");

            string CleanPicked(LibraryFolderInfo folder)
            {
                if (folder == null) return string.Empty;
                return TextAndPathHelpers.CleanTag(pickId(folder) ?? string.Empty);
            }

            bool IsSteamPlatform(LibraryFolderInfo folder)
            {
                if (folder == null) return false;
                return string.Equals(normConsole(folder.PlatformLabel ?? string.Empty), normSteam, StringComparison.OrdinalIgnoreCase);
            }

            var steamScoped = sourceFolders
                .Where(IsSteamPlatform)
                .Select(CleanPicked)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (steamScoped.Count == 1) return steamScoped[0];

            var all = sourceFolders
                .Where(folder => folder != null)
                .Select(CleanPicked)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (all.Count == 1) return all[0];

            return string.Empty;
        }

        public static string MergeLibraryBrowserNonSteamIdForCombinedView(
            IReadOnlyList<LibraryFolderInfo>? sourceFolders,
            Func<string, string>? normalizeConsoleLabel)
        {
            if (sourceFolders == null || sourceFolders.Count == 0) return string.Empty;
            var normConsole = normalizeConsoleLabel ?? (s => s ?? string.Empty);
            var normEmulation = normConsole("Emulation");
            var emulationScoped = sourceFolders
                .Where(folder => folder != null && string.Equals(normConsole(folder.PlatformLabel ?? string.Empty), normEmulation, StringComparison.OrdinalIgnoreCase))
                .Select(folder => TextAndPathHelpers.CleanTag(folder.NonSteamId ?? string.Empty))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (emulationScoped.Count == 1) return emulationScoped[0];

            var all = sourceFolders
                .Where(folder => folder != null)
                .Select(folder => TextAndPathHelpers.CleanTag(folder.NonSteamId ?? string.Empty))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return all.Count == 1 ? all[0] : string.Empty;
        }

        /// <summary>Merged "All" folder row: show RetroAchievements game ID only when every non-empty source agrees.</summary>
        public static string MergeLibraryBrowserRetroAchievementsGameIdForCombinedView(IReadOnlyList<LibraryFolderInfo>? sourceFolders)
        {
            if (sourceFolders == null || sourceFolders.Count == 0) return string.Empty;
            var ids = sourceFolders
                .Where(folder => folder != null)
                .Select(folder => TextAndPathHelpers.CleanTag(folder.RetroAchievementsGameId ?? string.Empty))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return ids.Count == 1 ? ids[0] : string.Empty;
        }

        public static string MergeLibraryBrowserCollectionNotesForCombinedView(IEnumerable<LibraryFolderInfo>? folders)
        {
            if (folders == null) return string.Empty;
            foreach (var f in folders)
            {
                if (f == null) continue;
                var n = f.CollectionNotes;
                if (!string.IsNullOrWhiteSpace(n)) return n;
            }
            return string.Empty;
        }
    }
}
