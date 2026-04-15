using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        long _libraryBrowserAllMergeProjectionFingerprint = long.MinValue;
        List<LibraryBrowserFolderView> _libraryBrowserAllMergeProjection;

        internal sealed class LibraryBrowserFolderView
        {
            internal string ViewKey;
            internal string GameId;
            internal string Name;
            internal string PrimaryFolderPath;
            internal LibraryFolderInfo PrimaryFolder;
            internal readonly List<LibraryFolderInfo> SourceFolders = new List<LibraryFolderInfo>();
            internal string PrimaryPlatformLabel;
            internal string[] PlatformLabels = new string[0];
            internal string PlatformSummaryText;
            internal int FileCount;
            internal string PreviewImagePath;
            internal string[] FilePaths = new string[0];
            internal long NewestCaptureUtcTicks;
            internal long NewestRecentSortUtcTicks;
            internal string SteamAppId;
            internal string NonSteamId;
            internal string SteamGridDbId;
            internal string RetroAchievementsGameId;
            internal string CollectionNotes;
            internal bool SuppressSteamAppIdAutoResolve;
            internal bool SuppressSteamGridDbIdAutoResolve;
            internal bool IsCompleted100Percent;
            internal long CompletedUtcTicks;
            internal bool IsMergedAcrossPlatforms;
            internal bool IsTimelineProjection;
            internal bool PendingGameAssignment;
            /// <summary>Lowercase, newline-separated tokens for library search (name, paths, ids, platforms).</summary>
            internal string SearchBlob;
        }

        LibraryBrowserFolderView CloneLibraryBrowserFolderView(LibraryBrowserFolderView view)
        {
            if (view == null) return null;
            var clone = new LibraryBrowserFolderView
            {
                ViewKey = view.ViewKey,
                GameId = view.GameId,
                Name = view.Name,
                PrimaryFolderPath = view.PrimaryFolderPath,
                PrimaryFolder = view.PrimaryFolder,
                PrimaryPlatformLabel = view.PrimaryPlatformLabel,
                PlatformLabels = view.PlatformLabels == null ? new string[0] : view.PlatformLabels.ToArray(),
                PlatformSummaryText = view.PlatformSummaryText,
                FileCount = view.FileCount,
                PreviewImagePath = view.PreviewImagePath,
                FilePaths = view.FilePaths == null ? new string[0] : view.FilePaths.ToArray(),
                NewestCaptureUtcTicks = view.NewestCaptureUtcTicks,
                NewestRecentSortUtcTicks = view.NewestRecentSortUtcTicks,
                SteamAppId = view.SteamAppId,
                NonSteamId = view.NonSteamId,
                SteamGridDbId = view.SteamGridDbId,
                RetroAchievementsGameId = view.RetroAchievementsGameId,
                CollectionNotes = view.CollectionNotes,
                SuppressSteamAppIdAutoResolve = view.SuppressSteamAppIdAutoResolve,
                SuppressSteamGridDbIdAutoResolve = view.SuppressSteamGridDbIdAutoResolve,
                IsCompleted100Percent = view.IsCompleted100Percent,
                CompletedUtcTicks = view.CompletedUtcTicks,
                IsMergedAcrossPlatforms = view.IsMergedAcrossPlatforms,
                IsTimelineProjection = view.IsTimelineProjection,
                PendingGameAssignment = view.PendingGameAssignment,
                SearchBlob = view.SearchBlob
            };
            clone.SourceFolders.AddRange(view.SourceFolders.Where(folder => folder != null));
            return clone;
        }

        void PopulateLibraryBrowserFolderViewSearchBlob(LibraryBrowserFolderView view)
        {
            if (view == null) return;
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(view.Name)) parts.Add(view.Name.Trim());
            if (!string.IsNullOrWhiteSpace(view.PlatformSummaryText)) parts.Add(view.PlatformSummaryText.Trim());
            if (!string.IsNullOrWhiteSpace(view.PrimaryFolderPath)) parts.Add(view.PrimaryFolderPath.Trim());
            if (!string.IsNullOrWhiteSpace(view.GameId)) parts.Add(view.GameId.Trim());
            if (!string.IsNullOrWhiteSpace(view.SteamAppId)) parts.Add(view.SteamAppId.Trim());
            if (!string.IsNullOrWhiteSpace(view.NonSteamId)) parts.Add(view.NonSteamId.Trim());
            if (!string.IsNullOrWhiteSpace(view.SteamGridDbId)) parts.Add(view.SteamGridDbId.Trim());
            if (!string.IsNullOrWhiteSpace(view.RetroAchievementsGameId)) parts.Add(view.RetroAchievementsGameId.Trim());
            if (!string.IsNullOrWhiteSpace(view.CollectionNotes)) parts.Add(view.CollectionNotes.Trim());
            foreach (var source in view.SourceFolders)
            {
                if (source == null) continue;
                var p = source.FolderPath;
                if (!string.IsNullOrWhiteSpace(p)) parts.Add(p.Trim());
            }
            view.SearchBlob = string.Join("\n", parts).ToLowerInvariant();
        }

        LibraryFolderInfo GetLibraryBrowserPrimaryFolder(LibraryBrowserFolderView view)
        {
            if (view == null) return null;
            if (view.IsTimelineProjection) return null;
            if (view.PrimaryFolder != null) return view.PrimaryFolder;
            return view.SourceFolders.FirstOrDefault(folder => folder != null);
        }

        LibraryFolderInfo BuildLibraryBrowserDisplayFolder(LibraryBrowserFolderView view)
        {
            if (view == null) return null;
            var primary = GetLibraryBrowserPrimaryFolder(view);
            var folder = CloneLibraryFolderInfo(primary) ?? new LibraryFolderInfo();
            folder.GameId = view.GameId ?? string.Empty;
            folder.Name = view.Name ?? string.Empty;
            folder.FolderPath = string.IsNullOrWhiteSpace(view.PrimaryFolderPath) ? (primary == null ? string.Empty : primary.FolderPath ?? string.Empty) : view.PrimaryFolderPath;
            folder.FileCount = view.FileCount;
            folder.PreviewImagePath = string.IsNullOrWhiteSpace(view.PreviewImagePath) ? (primary == null ? string.Empty : primary.PreviewImagePath ?? string.Empty) : view.PreviewImagePath;
            folder.FilePaths = view.FilePaths == null ? new string[0] : view.FilePaths.ToArray();
            folder.NewestCaptureUtcTicks = view.NewestCaptureUtcTicks;
            folder.NewestRecentSortUtcTicks = view.NewestRecentSortUtcTicks;
            folder.SteamAppId = view.SteamAppId ?? string.Empty;
            folder.NonSteamId = view.NonSteamId ?? string.Empty;
            folder.SteamGridDbId = view.SteamGridDbId ?? string.Empty;
            folder.RetroAchievementsGameId = view.RetroAchievementsGameId ?? string.Empty;
            folder.SuppressSteamAppIdAutoResolve = view.SuppressSteamAppIdAutoResolve;
            folder.SuppressSteamGridDbIdAutoResolve = view.SuppressSteamGridDbIdAutoResolve;
            folder.IsCompleted100Percent = view.IsCompleted100Percent;
            folder.CompletedUtcTicks = view.CompletedUtcTicks;
            if (string.IsNullOrWhiteSpace(folder.PlatformLabel)) folder.PlatformLabel = view.PrimaryPlatformLabel ?? string.Empty;
            folder.PendingGameAssignment = view.PendingGameAssignment
                || (primary != null && primary.PendingGameAssignment);
            folder.CollectionNotes = string.IsNullOrWhiteSpace(view.CollectionNotes)
                ? (primary?.CollectionNotes ?? string.Empty)
                : view.CollectionNotes;
            return folder;
        }

        bool SameLibraryBrowserSelection(LibraryBrowserFolderView left, LibraryBrowserFolderView right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null) return false;
            return string.Equals(left.ViewKey ?? string.Empty, right.ViewKey ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        string NormalizeLibraryGroupingMode(string value) => SettingsService.NormalizeLibraryGroupingMode(value);

        bool IsLibraryBrowserTimelineMode()
        {
            return string.Equals(NormalizeLibraryGroupingMode(libraryGroupingMode), "timeline", StringComparison.OrdinalIgnoreCase);
        }

        bool IsLibraryBrowserTimelineView(LibraryBrowserFolderView view)
        {
            return view != null && view.IsTimelineProjection;
        }

        internal static void NormalizeLibraryTimelineDateRange(ref DateTime startDate, ref DateTime endDate)
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

        internal static void BuildLibraryTimelinePresetDateRange(string presetKey, DateTime referenceDate, out DateTime startDate, out DateTime endDate)
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

        internal static string DetectLibraryTimelinePresetKey(DateTime startDate, DateTime endDate, DateTime referenceDate)
        {
            NormalizeLibraryTimelineDateRange(ref startDate, ref endDate);
            var today = referenceDate <= DateTime.MinValue ? DateTime.Today : referenceDate.Date;
            DateTime presetStart;
            DateTime presetEnd;
            BuildLibraryTimelinePresetDateRange("today", today, out presetStart, out presetEnd);
            if (startDate == presetStart && endDate == presetEnd) return "today";
            BuildLibraryTimelinePresetDateRange("month", today, out presetStart, out presetEnd);
            if (startDate == presetStart && endDate == presetEnd) return "month";
            BuildLibraryTimelinePresetDateRange("30d", today, out presetStart, out presetEnd);
            if (startDate == presetStart && endDate == presetEnd) return "30d";
            return "custom";
        }

        internal static bool LibraryTimelineRangeContainsCapture(DateTime captureDate, DateTime startDate, DateTime endDate)
        {
            if (captureDate <= DateTime.MinValue) return false;
            NormalizeLibraryTimelineDateRange(ref startDate, ref endDate);
            var captureDay = captureDate.Date;
            return captureDay >= startDate && captureDay <= endDate;
        }

        /// <summary>
        /// Rolling presets (Today / This month / 30 days) are anchored to the calendar day when chosen.
        /// If the library stays open past midnight, <paramref name="ws"/> still holds yesterday’s end date and new captures disappear from the timeline until the user re-applies a preset.
        /// Recompute start/end from <see cref="DateTime.Today"/> when the preset is not custom.
        /// </summary>
        internal static bool TryAlignLibraryTimelineRollingPresetToToday(LibraryBrowserWorkingSet ws)
        {
            if (ws == null) return false;
            var key = (ws.TimelineDatePresetKey ?? string.Empty).Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(key) || string.Equals(key, "custom", StringComparison.OrdinalIgnoreCase)) return false;
            string presetArg;
            if (string.Equals(key, "today", StringComparison.OrdinalIgnoreCase)) presetArg = "today";
            else if (string.Equals(key, "month", StringComparison.OrdinalIgnoreCase)) presetArg = "month";
            else if (string.Equals(key, "30d", StringComparison.OrdinalIgnoreCase)) presetArg = "30d";
            else return false;

            DateTime start;
            DateTime end;
            BuildLibraryTimelinePresetDateRange(presetArg, DateTime.Today, out start, out end);
            var curStart = ws.TimelineStartDate <= DateTime.MinValue ? DateTime.MinValue : ws.TimelineStartDate.Date;
            var curEnd = ws.TimelineEndDate <= DateTime.MinValue ? DateTime.MinValue : ws.TimelineEndDate.Date;
            if (curStart == start && curEnd == end) return false;
            ws.TimelineStartDate = start;
            ws.TimelineEndDate = end;
            return true;
        }

        internal static string BuildLibraryTimelineSummaryText(int captureCount, int gameCount, int platformCount, DateTime newestCapture, DateTime oldestCapture)
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

        internal static string BuildLibraryTimelineCaptureTimeLabel(DateTime captureDate)
        {
            return captureDate <= DateTime.MinValue ? string.Empty : captureDate.ToString("h:mm tt");
        }

        internal static string BuildLibraryTimelineDayCardTitle(DateTime captureDate, DateTime referenceDate)
        {
            if (captureDate <= DateTime.MinValue) return string.Empty;
            var today = referenceDate <= DateTime.MinValue ? DateTime.Today : referenceDate.Date;
            var captureDay = captureDate.Date;
            if (captureDay == today) return "Today";
            if (captureDay == today.AddDays(-1)) return "Yesterday";
            if (captureDay.Year == today.Year) return captureDay.ToString("ddd, MMM d");
            return captureDay.ToString("ddd, MMM d, yyyy");
        }

        internal static int CalculateLibraryTimelinePackedTileSize(int detailTileSize, double availableWidth)
        {
            const double scale = 1.75d;
            var width = availableWidth <= 0 ? 1280d : availableWidth;
            var minTile = (int)Math.Round((width < 540d ? 144 : (width < 860d ? 160 : 180)) * scale);
            var maxTile = (int)Math.Round((width >= 1800d ? 280 : (width >= 1300d ? 256 : (width >= 960d ? 228 : 196))) * scale);
            var proposed = detailTileSize <= 0 ? maxTile : Math.Min(detailTileSize, maxTile);
            return Math.Max(minTile, Math.Min(maxTile, proposed));
        }

        internal static int CalculateLibraryTimelinePackedCardColumns(int captureCount, double availableWidth)
        {
            if (captureCount <= 1) return 1;
            var widthBasedMax = availableWidth >= 1680d ? 3 : (availableWidth >= 980d ? 2 : 1);
            if (captureCount >= 6 && widthBasedMax >= 3) return 3;
            if (captureCount >= 3 && widthBasedMax >= 2) return 2;
            return Math.Max(1, Math.Min(captureCount, widthBasedMax));
        }

        internal static double EstimateLibraryTimelinePackedCardWidth(int captureCount, int tileSize, double availableWidth)
        {
            return EstimateLibraryTimelinePackedCardWidth(
                captureCount,
                tileSize,
                availableWidth,
                CalculateLibraryTimelinePackedCardColumns(captureCount, availableWidth));
        }

        internal static double EstimateLibraryTimelinePackedCardWidth(int captureCount, int tileSize, double availableWidth, int cardColumns)
        {
            const double innerGap = 8d;
            const double horizontalPadding = 24d;
            var normalizedColumns = Math.Max(1, Math.Min(Math.Max(1, captureCount), cardColumns));
            return Math.Max(220d, (normalizedColumns * tileSize) + ((normalizedColumns - 1) * innerGap) + horizontalPadding);
        }

        internal static double EstimateLibraryTimelinePackedCardHeight(int captureCount, int tileSize, double availableWidth)
        {
            return EstimateLibraryTimelinePackedCardHeight(
                captureCount,
                tileSize,
                CalculateLibraryTimelinePackedCardColumns(Math.Max(1, captureCount), availableWidth));
        }

        internal static double EstimateLibraryTimelinePackedCardHeight(int captureCount, int tileSize, int cardColumns)
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

        internal static List<List<int>> BuildLibraryTimelinePackedRows(IReadOnlyList<double> cardWidths, double availableWidth, double interCardGap)
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

        internal static double EstimateLibraryPackedDayCardDesiredWidth(int captureCount, double availableWidth, bool timelineView)
        {
            return EstimateLibraryPackedDayCardDesiredWidth(captureCount, availableWidth, timelineView, 0);
        }

        internal static double EstimateLibraryPackedDayCardDesiredWidth(int captureCount, double availableWidth, bool timelineView, int detailTileSize)
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

        internal static List<double> ExpandLibraryPackedRowWidths(IReadOnlyList<double> desiredWidths, double availableWidth, double interCardGap)
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

        internal static int LibraryDetailFileLayoutHash(string path)
        {
            if (string.IsNullOrEmpty(path)) return 0;
            unchecked
            {
                return Math.Abs(StringComparer.OrdinalIgnoreCase.GetHashCode(path));
            }
        }

        internal static int ResolveLibraryVariableDetailTileWidth(string file, int baseWidth, int minWidth, int maxWidth)
        {
            var scales = new[] { 0.76, 0.88, 0.96, 1.06, 1.18, 1.32 };
            var h = LibraryDetailFileLayoutHash(file);
            var s = scales[h % scales.Length];
            var w = (int)Math.Round(baseWidth * s / 12.0) * 12;
            return Math.Max(minWidth, Math.Min(maxWidth, w));
        }

        internal static List<List<(string File, int Width)>> PackLibraryDetailFilesIntoVariableRows(
            IReadOnlyList<string> files,
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

        internal static int EstimateLibraryVariableDetailRowHeight(IReadOnlyList<(string File, int Width)> row, bool includeTimelineFooter)
        {
            if (row == null || row.Count == 0) return 260;
            var maxH = 0;
            foreach (var pair in row)
            {
                var aspectRatio = ResolveLibraryDetailAspectRatio(pair.File, null);
                var h = (int)Math.Ceiling(pair.Width / aspectRatio);
                if (h > maxH) maxH = h;
            }
            return Math.Max(1, maxH);
        }

        Dictionary<string, LibraryTimelineCaptureContext> BuildLibraryTimelineCaptureContextMap(
            IEnumerable<string> files,
            Dictionary<string, LibraryMetadataIndexEntry> metadataIndex,
            IEnumerable<GameIndexEditorRow> savedGameRows,
            Dictionary<string, EmbeddedMetadataSnapshot> metadataSnapshots = null)
        {
            var contexts = new Dictionary<string, LibraryTimelineCaptureContext>(StringComparer.OrdinalIgnoreCase);
            var rowsByGameId = (savedGameRows ?? Enumerable.Empty<GameIndexEditorRow>())
                .Where(row => row != null && !string.IsNullOrWhiteSpace(NormalizeGameId(row.GameId)))
                .GroupBy(row => NormalizeGameId(row.GameId), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            foreach (var file in (files ?? Enumerable.Empty<string>()).Where(path => !string.IsNullOrWhiteSpace(path)))
            {
                var captureDate = ResolveIndexedLibraryDate(libraryRoot, file, metadataIndex);
                var entry = TryGetLibraryMetadataIndexEntry(libraryRoot, file, metadataIndex);
                EmbeddedMetadataSnapshot metadataSnapshot;
                if (metadataSnapshots == null || !metadataSnapshots.TryGetValue(file, out metadataSnapshot) || metadataSnapshot == null) metadataSnapshot = null;
                var normalizedGameId = NormalizeGameId(entry == null ? string.Empty : entry.GameId);
                GameIndexEditorRow savedRow;
                rowsByGameId.TryGetValue(normalizedGameId, out savedRow);
                var gameTitle = NormalizeGameIndexName(savedRow == null ? string.Empty : savedRow.Name);
                if (string.IsNullOrWhiteSpace(gameTitle)) gameTitle = NormalizeGameIndexName(GuessGameIndexNameForFile(file));
                if (string.IsNullOrWhiteSpace(gameTitle)) gameTitle = "Unknown Game";
                var platformLabel = NormalizeConsoleLabel(savedRow == null ? (entry == null ? string.Empty : entry.ConsoleLabel) : savedRow.PlatformLabel);
                if (string.IsNullOrWhiteSpace(platformLabel) || string.Equals(platformLabel, "Other", StringComparison.OrdinalIgnoreCase))
                {
                    platformLabel = NormalizeConsoleLabel(entry == null ? string.Empty : entry.ConsoleLabel);
                }
                if (string.IsNullOrWhiteSpace(platformLabel) || string.Equals(platformLabel, "Other", StringComparison.OrdinalIgnoreCase))
                {
                    platformLabel = NormalizeConsoleLabel(PrimaryPlatformLabel(file));
                }
                contexts[file] = new LibraryTimelineCaptureContext
                {
                    GameTitle = gameTitle,
                    PlatformLabel = platformLabel,
                    CaptureDate = captureDate,
                    Comment = metadataSnapshot == null ? string.Empty : CleanComment(metadataSnapshot.Comment ?? string.Empty)
                };
            }
            return contexts;
        }

        string BuildLibraryBrowserViewKey(string groupingMode, string gameId, string name, string folderPath, string platformLabel)
        {
            var normalizedGrouping = NormalizeLibraryGroupingMode(groupingMode);
            var normalizedGameId = NormalizeGameId(gameId);
            var normalizedName = NormalizeGameIndexName(name, folderPath);
            if (string.Equals(normalizedGrouping, "console", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(normalizedGameId))
                {
                    return normalizedGrouping + "|" + normalizedGameId + "|" + NormalizeConsoleLabel(platformLabel) + "|" + (folderPath ?? string.Empty).Trim();
                }
                return normalizedGrouping + "|" + normalizedName + "|" + NormalizeConsoleLabel(platformLabel) + "|" + (folderPath ?? string.Empty).Trim();
            }
            if (!string.IsNullOrWhiteSpace(normalizedName)) return normalizedGrouping + "|name|" + normalizedName;
            if (!string.IsNullOrWhiteSpace(normalizedGameId)) return normalizedGrouping + "|id|" + normalizedGameId;
            return normalizedGrouping + "|folder|" + ((folderPath ?? string.Empty).Trim());
        }

        string BuildLibraryBrowserPlatformSummary(IEnumerable<string> platformLabels)
        {
            var labels = (platformLabels ?? Enumerable.Empty<string>())
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .Select(NormalizeConsoleLabel)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(label => PlatformGroupOrder(label))
                .ThenBy(label => label, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (labels.Count == 0) return "Other";
            if (labels.Count == 1) return labels[0];
            if (labels.Count == 2) return labels[0] + " + " + labels[1];
            return labels.Count + " platforms";
        }

        string DetermineLibraryBrowserGroup(LibraryBrowserFolderView view)
        {
            return NormalizeConsoleLabel(view == null ? string.Empty : view.PrimaryPlatformLabel);
        }

        string BuildLibraryBrowserAllMergeKey(LibraryFolderInfo folder)
        {
            if (folder == null) return string.Empty;
            if (folder.PendingGameAssignment)
                return "unassigned|" + ((folder.FolderPath ?? string.Empty).Trim());
            var normalizedName = NormalizeGameIndexName(folder.Name, folder.FolderPath);
            if (!string.IsNullOrWhiteSpace(normalizedName)) return "name|" + normalizedName;
            var normalizedGameId = NormalizeGameId(folder.GameId);
            if (!string.IsNullOrWhiteSpace(normalizedGameId)) return "id|" + normalizedGameId;
            return "folder|" + ((folder.FolderPath ?? string.Empty).Trim());
        }

        int CountLibraryBrowserSourceFolders(LibraryBrowserFolderView view)
        {
            return (view == null ? Enumerable.Empty<LibraryFolderInfo>() : view.SourceFolders)
                .Select(folder => folder == null ? string.Empty : folder.FolderPath ?? string.Empty)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
        }

        List<string> GetLibraryBrowserSourceFolderPaths(LibraryBrowserFolderView view)
        {
            return (view == null ? Enumerable.Empty<LibraryFolderInfo>() : view.SourceFolders)
                .Select(folder => folder == null ? string.Empty : folder.FolderPath ?? string.Empty)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        List<LibraryFolderInfo> GetLibraryBrowserActionFolders(LibraryBrowserFolderView view)
        {
            return (view == null ? Enumerable.Empty<LibraryFolderInfo>() : view.SourceFolders)
                .Where(folder => folder != null && !string.IsNullOrWhiteSpace(folder.FolderPath))
                .GroupBy(folder => folder.FolderPath ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(folder => folder.FolderPath ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        bool ShouldShowLibraryBrowserPlatformContext()
        {
            return string.Equals(NormalizeLibraryGroupingMode(libraryGroupingMode), "console", StringComparison.OrdinalIgnoreCase);
        }

        string BuildLibraryBrowserFolderTileSubtitle(LibraryBrowserFolderView view)
        {
            var captureCount = view == null ? 0 : Math.Max(view.FileCount, 0);
            var captureText = captureCount + " capture" + (captureCount == 1 ? string.Empty : "s");
            string core;
            if (ShouldShowLibraryBrowserPlatformContext())
            {
                var platformText = view == null ? string.Empty : CleanTag(view.PlatformSummaryText);
                core = string.IsNullOrWhiteSpace(platformText) ? captureText : platformText + " | " + captureText;
            }
            else
            {
                var sourceFolderCount = CountLibraryBrowserSourceFolders(view);
                core = sourceFolderCount > 1
                    ? captureText + " | " + sourceFolderCount + " folders"
                    : captureText;
            }
            return core;
        }

        string BuildLibraryBrowserDetailMetaText(LibraryBrowserFolderView view, LibraryFolderInfo actionFolder)
        {
            var itemCount = view == null ? 0 : Math.Max(view.FileCount, 0);
            var itemText = itemCount + " item" + (itemCount == 1 ? string.Empty : "s");
            if (IsLibraryBrowserTimelineView(view))
            {
                return itemText + " | photo timeline";
            }
            var sourceFolderCount = CountLibraryBrowserSourceFolders(view);
            var folderPathText = actionFolder == null ? string.Empty : actionFolder.FolderPath ?? string.Empty;
            if (ShouldShowLibraryBrowserPlatformContext())
            {
                var platformText = view == null ? string.Empty : CleanTag(view.PlatformSummaryText);
                var locationText = sourceFolderCount > 1 ? sourceFolderCount + " source folders" : folderPathText;
                if (string.IsNullOrWhiteSpace(platformText)) return itemText + " | " + locationText;
                return itemText + " | " + platformText + " | " + locationText;
            }

            if (sourceFolderCount > 1) return itemText + " | " + sourceFolderCount + " source folders";
            return string.IsNullOrWhiteSpace(folderPathText) ? itemText : itemText + " | " + folderPathText;
        }

        string BuildLibraryBrowserScopeLabel(LibraryBrowserFolderView view)
        {
            if (view == null) return string.Empty;
            if (!ShouldShowLibraryBrowserPlatformContext()) return view.Name ?? string.Empty;
            var platformText = CleanTag(view.PlatformSummaryText);
            return string.IsNullOrWhiteSpace(platformText)
                ? (view.Name ?? string.Empty)
                : ((view.Name ?? string.Empty) + " | " + platformText);
        }

        string BuildLibraryBrowserActionScopeLabel(LibraryBrowserFolderView view)
        {
            if (view == null) return string.Empty;
            var sourceFolderCount = CountLibraryBrowserSourceFolders(view);
            if (!ShouldShowLibraryBrowserPlatformContext() && sourceFolderCount > 1)
            {
                return (view.Name ?? string.Empty) + " (" + sourceFolderCount + " folders)";
            }
            return BuildLibraryBrowserScopeLabel(view);
        }

        string BuildLibraryBrowserOpenFoldersLabel(LibraryBrowserFolderView view)
        {
            if (IsLibraryBrowserTimelineView(view)) return "Open Source Folders";
            return CountLibraryBrowserSourceFolders(view) > 1 ? "Open Folders" : "Open Folder";
        }

        string BuildLibraryBrowserTroubleshootingLabel(LibraryBrowserFolderView view)
        {
            if (view == null)
            {
                return "view=(none); grouping=" + NormalizeLibraryGroupingMode(libraryGroupingMode);
            }

            var platformText = string.Join(",",
                (view.PlatformLabels ?? new string[0])
                    .Where(label => !string.IsNullOrWhiteSpace(label))
                    .Select(NormalizeConsoleLabel)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(label => PlatformGroupOrder(label))
                    .ThenBy(label => label, StringComparer.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(platformText))
            {
                platformText = NormalizeConsoleLabel(view.PrimaryPlatformLabel);
            }
            if (string.IsNullOrWhiteSpace(platformText))
            {
                platformText = "(none)";
            }

            return "viewKey=" + FormatViewKeyForTroubleshooting(view.ViewKey ?? string.Empty)
                + "; name=" + (view.Name ?? string.Empty)
                + "; files=" + Math.Max(view.FileCount, 0)
                + "; sourceFolders=" + CountLibraryBrowserSourceFolders(view)
                + "; timeline=" + (view.IsTimelineProjection ? "1" : "0")
                + "; platforms=" + platformText
                + "; primaryFolder=" + FormatPathForTroubleshooting(view.PrimaryFolderPath ?? string.Empty)
                + "; grouping=" + NormalizeLibraryGroupingMode(libraryGroupingMode);
        }

        LibraryBrowserFolderView BuildLibraryBrowserTimelineView(IEnumerable<LibraryBrowserFolderView> visibleFolders)
        {
            var sourceViews = (visibleFolders ?? Enumerable.Empty<LibraryBrowserFolderView>())
                .Where(view => view != null)
                .ToList();
            var orderedImagePaths = sourceViews
                .SelectMany(view => view.FilePaths ?? new string[0])
                .Where(path => !string.IsNullOrWhiteSpace(path) && IsImage(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(path => ResolveIndexedLibraryDate(libraryRoot, path))
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var newest = orderedImagePaths.Length == 0 ? DateTime.MinValue : ResolveIndexedLibraryDate(libraryRoot, orderedImagePaths[0]);
            var timelineView = new LibraryBrowserFolderView
            {
                ViewKey = "timeline|capture-feed",
                Name = "Timeline",
                PrimaryFolderPath = string.Empty,
                PrimaryFolder = null,
                PrimaryPlatformLabel = string.Empty,
                PlatformLabels = new string[0],
                PlatformSummaryText = "Photo timeline",
                FileCount = orderedImagePaths.Length,
                PreviewImagePath = orderedImagePaths.FirstOrDefault() ?? string.Empty,
                FilePaths = orderedImagePaths,
                NewestCaptureUtcTicks = newest <= DateTime.MinValue ? 0 : newest.ToUniversalTime().Ticks,
                NewestRecentSortUtcTicks = newest <= DateTime.MinValue ? 0 : newest.ToUniversalTime().Ticks,
                SteamAppId = string.Empty,
                NonSteamId = string.Empty,
                SteamGridDbId = string.Empty,
                RetroAchievementsGameId = string.Empty,
                SuppressSteamAppIdAutoResolve = true,
                SuppressSteamGridDbIdAutoResolve = true,
                IsMergedAcrossPlatforms = true,
                IsTimelineProjection = true
            };
            timelineView.SourceFolders.AddRange(sourceViews
                .SelectMany(view => view.SourceFolders)
                .Where(folder => folder != null)
                .GroupBy(folder => folder.FolderPath ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(folder => folder.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase));
            PopulateLibraryBrowserFolderViewSearchBlob(timelineView);
            return timelineView;
        }

        void ApplyRemovedFilesToLibraryBrowserState(LibraryBrowserWorkingSet ws, IEnumerable<string> removedFiles)
        {
            if (ws == null) return;
            var removedSet = new HashSet<string>((removedFiles ?? Enumerable.Empty<string>())
                .Where(path => !string.IsNullOrWhiteSpace(path)), StringComparer.OrdinalIgnoreCase);
            if (removedSet.Count == 0) return;

            foreach (var folder in ws.Folders.ToList())
            {
                if (folder == null) continue;
                var existingPaths = (folder.FilePaths ?? new string[0])
                    .Where(path => !string.IsNullOrWhiteSpace(path))
                    .ToArray();
                if (existingPaths.Length == 0) continue;
                if (!existingPaths.Any(path => removedSet.Contains(path))) continue;

                var remainingPaths = existingPaths
                    .Where(path => !removedSet.Contains(path) && File.Exists(path))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderByDescending(path => ResolveIndexedLibraryDate(libraryRoot, path))
                    .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (remainingPaths.Length == 0)
                {
                    ws.Folders.Remove(folder);
                    continue;
                }

                folder.FilePaths = remainingPaths;
                folder.FileCount = remainingPaths.Length;
                var newest = remainingPaths
                    .Select(path => ResolveIndexedLibraryDate(libraryRoot, path))
                    .DefaultIfEmpty(DateTime.MinValue)
                    .Max();
                folder.NewestCaptureUtcTicks = newest > DateTime.MinValue ? newest.ToUniversalTime().Ticks : 0;
                folder.NewestRecentSortUtcTicks = remainingPaths.Length == 0
                    ? 0
                    : remainingPaths.Max(path => ResolveLibraryFileRecentSortUtcTicks(libraryRoot, path, null));

                var previewPath = folder.PreviewImagePath ?? string.Empty;
                if (removedSet.Contains(previewPath) || string.IsNullOrWhiteSpace(previewPath) || !File.Exists(previewPath))
                {
                    folder.PreviewImagePath = remainingPaths.FirstOrDefault(IsImage) ?? remainingPaths.FirstOrDefault() ?? string.Empty;
                }
            }

            ws.DetailFilesDisplayOrder.RemoveAll(path => removedSet.Contains(path) || string.IsNullOrWhiteSpace(path) || !File.Exists(path));
            foreach (var stale in ws.SelectedDetailFiles.Where(path => removedSet.Contains(path) || string.IsNullOrWhiteSpace(path) || !File.Exists(path)).ToList())
            {
                ws.SelectedDetailFiles.Remove(stale);
            }
        }

        LibraryBrowserFolderView FindMatchingLibraryBrowserView(LibraryBrowserFolderView current, IList<LibraryBrowserFolderView> candidates)
        {
            if (current == null || candidates == null || candidates.Count == 0) return null;
            var exact = candidates.FirstOrDefault(candidate => SameLibraryBrowserSelection(current, candidate));
            if (exact != null) return exact;

            var currentPrimary = GetLibraryBrowserPrimaryFolder(current);
            if (currentPrimary != null)
            {
                var byPrimary = candidates.FirstOrDefault(candidate =>
                    candidate != null && candidate.SourceFolders.Any(source => SameLibraryFolderSelection(source, currentPrimary)));
                if (byPrimary != null) return byPrimary;
            }

            var normalizedGameId = NormalizeGameId(current.GameId);
            if (!string.IsNullOrWhiteSpace(normalizedGameId))
            {
                var byGameId = candidates.FirstOrDefault(candidate =>
                    candidate != null && string.Equals(NormalizeGameId(candidate.GameId), normalizedGameId, StringComparison.OrdinalIgnoreCase));
                if (byGameId != null) return byGameId;
            }

            var normalizedName = NormalizeGameIndexName(current.Name, current.PrimaryFolderPath);
            if (!string.IsNullOrWhiteSpace(normalizedName))
            {
                return candidates.FirstOrDefault(candidate =>
                    candidate != null && string.Equals(
                        NormalizeGameIndexName(candidate.Name, candidate.PrimaryFolderPath),
                        normalizedName,
                        StringComparison.OrdinalIgnoreCase));
            }

            return null;
        }

        LibraryBrowserFolderView FindLibraryBrowserViewByViewKey(IEnumerable<LibraryBrowserFolderView> candidates, string viewKey)
        {
            if (string.IsNullOrWhiteSpace(viewKey) || candidates == null) return null;
            return candidates.FirstOrDefault(c => c != null && string.Equals(c.ViewKey ?? string.Empty, viewKey, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Stable fingerprint of folder list order + merge-relevant fields; used to skip rebuilding merged "All" projection.</summary>
        static long ComputeLibraryBrowserFoldersMergeFingerprint(IReadOnlyList<LibraryFolderInfo> folders)
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
                        h = h * 397 ^ (paths[0] ?? string.Empty).GetHashCode(StringComparison.OrdinalIgnoreCase);
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
        internal static string MergeLibraryBrowserExternalIdsForCombinedView(
            IReadOnlyList<LibraryFolderInfo> sourceFolders,
            Func<LibraryFolderInfo, string> pickId,
            Func<string, string> normalizeConsoleLabel)
        {
            if (sourceFolders == null || sourceFolders.Count == 0 || pickId == null) return string.Empty;
            var normConsole = normalizeConsoleLabel ?? (s => s ?? string.Empty);
            var normSteam = normConsole("Steam");

            string CleanPicked(LibraryFolderInfo folder)
            {
                if (folder == null) return string.Empty;
                return CleanTag(pickId(folder) ?? string.Empty);
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

        internal static string MergeLibraryBrowserNonSteamIdForCombinedView(
            IReadOnlyList<LibraryFolderInfo> sourceFolders,
            Func<string, string> normalizeConsoleLabel)
        {
            if (sourceFolders == null || sourceFolders.Count == 0) return string.Empty;
            var normConsole = normalizeConsoleLabel ?? (s => s ?? string.Empty);
            var normEmulation = normConsole("Emulation");
            var emulationScoped = sourceFolders
                .Where(folder => folder != null && string.Equals(normConsole(folder.PlatformLabel ?? string.Empty), normEmulation, StringComparison.OrdinalIgnoreCase))
                .Select(folder => CleanTag(folder.NonSteamId ?? string.Empty))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (emulationScoped.Count == 1) return emulationScoped[0];

            var all = sourceFolders
                .Where(folder => folder != null)
                .Select(folder => CleanTag(folder.NonSteamId ?? string.Empty))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return all.Count == 1 ? all[0] : string.Empty;
        }

        /// <summary>Merged "All" folder row: show RetroAchievements game ID only when every non-empty source agrees.</summary>
        internal static string MergeLibraryBrowserRetroAchievementsGameIdForCombinedView(IReadOnlyList<LibraryFolderInfo> sourceFolders)
        {
            if (sourceFolders == null || sourceFolders.Count == 0) return string.Empty;
            var ids = sourceFolders
                .Where(folder => folder != null)
                .Select(folder => CleanTag(folder.RetroAchievementsGameId ?? string.Empty))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            return ids.Count == 1 ? ids[0] : string.Empty;
        }

        /// <summary>Returns cached merged rows for "All" grouping when folder data unchanged; console mode is always rebuilt and clears the cache.</summary>
        List<LibraryBrowserFolderView> GetOrBuildLibraryBrowserFolderViews(IReadOnlyList<LibraryFolderInfo> folders, string groupingMode)
        {
            var normalizedGrouping = NormalizeLibraryGroupingMode(groupingMode);
            if (string.Equals(normalizedGrouping, "console", StringComparison.OrdinalIgnoreCase))
            {
                _libraryBrowserAllMergeProjection = null;
                _libraryBrowserAllMergeProjectionFingerprint = long.MinValue;
                return BuildLibraryBrowserFolderViews(folders, groupingMode);
            }

            var fp = ComputeLibraryBrowserFoldersMergeFingerprint(folders);
            if (_libraryBrowserAllMergeProjection != null && fp == _libraryBrowserAllMergeProjectionFingerprint)
            {
                return _libraryBrowserAllMergeProjection;
            }

            var built = BuildLibraryBrowserFolderViews(folders, groupingMode);
            _libraryBrowserAllMergeProjection = built;
            _libraryBrowserAllMergeProjectionFingerprint = fp;
            return built;
        }

        /// <summary>Sort key for folder tiles: prefer precomputed UTC ticks on the view; avoid Alloc in OrderBy hot path.</summary>
        DateTime GetLibraryBrowserFolderViewSortNewest(LibraryBrowserFolderView view)
        {
            if (view == null) return DateTime.MinValue;
            if (view.NewestCaptureUtcTicks > 0)
            {
                try
                {
                    return new DateTime(view.NewestCaptureUtcTicks, DateTimeKind.Utc).ToLocalTime();
                }
                catch
                {
                }
            }
            return GetLibraryFolderNewestDate(BuildLibraryBrowserDisplayFolder(view));
        }

        /// <summary>Sort key for Recently Added: index date-added when known, else capture/file date (see <see cref="ResolveLibraryFileRecentSortUtcTicks"/>).</summary>
        DateTime GetLibraryBrowserFolderViewSortRecentlyAdded(LibraryBrowserFolderView view)
        {
            if (view == null) return DateTime.MinValue;
            if (view.NewestRecentSortUtcTicks > 0)
            {
                try
                {
                    return new DateTime(view.NewestRecentSortUtcTicks, DateTimeKind.Utc).ToLocalTime();
                }
                catch
                {
                }
            }
            return GetLibraryBrowserFolderViewSortNewest(view);
        }

        static string MergeLibraryBrowserCollectionNotesForCombinedView(IEnumerable<LibraryFolderInfo> folders)
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

        List<LibraryBrowserFolderView> BuildLibraryBrowserFolderViews(IEnumerable<LibraryFolderInfo> folders, string groupingMode)
        {
            var rawFolders = (folders ?? Enumerable.Empty<LibraryFolderInfo>())
                .Where(folder => folder != null)
                .ToList();
            var normalizedGrouping = NormalizeLibraryGroupingMode(groupingMode);
            if (string.Equals(normalizedGrouping, "console", StringComparison.OrdinalIgnoreCase))
            {
                return rawFolders.Select(folder =>
                {
                    var view = new LibraryBrowserFolderView
                    {
                        ViewKey = BuildLibraryBrowserViewKey("console", folder.GameId, folder.Name, folder.FolderPath, folder.PlatformLabel),
                        GameId = NormalizeGameId(folder.GameId),
                        Name = folder.Name ?? string.Empty,
                        PrimaryFolderPath = folder.FolderPath ?? string.Empty,
                        PrimaryFolder = folder,
                        PrimaryPlatformLabel = NormalizeConsoleLabel(folder.PlatformLabel),
                        PlatformLabels = new[] { NormalizeConsoleLabel(folder.PlatformLabel) },
                        PlatformSummaryText = NormalizeConsoleLabel(folder.PlatformLabel),
                        FileCount = folder.FileCount,
                        PreviewImagePath = folder.PreviewImagePath ?? string.Empty,
                        FilePaths = folder.FilePaths == null ? new string[0] : folder.FilePaths.ToArray(),
                        NewestCaptureUtcTicks = folder.NewestCaptureUtcTicks,
                        NewestRecentSortUtcTicks = folder.NewestRecentSortUtcTicks,
                        SteamAppId = folder.SteamAppId ?? string.Empty,
                        NonSteamId = folder.NonSteamId ?? string.Empty,
                        SteamGridDbId = folder.SteamGridDbId ?? string.Empty,
                        RetroAchievementsGameId = folder.RetroAchievementsGameId ?? string.Empty,
                        SuppressSteamAppIdAutoResolve = folder.SuppressSteamAppIdAutoResolve,
                        SuppressSteamGridDbIdAutoResolve = folder.SuppressSteamGridDbIdAutoResolve,
                        IsCompleted100Percent = folder.IsCompleted100Percent,
                        CompletedUtcTicks = folder.CompletedUtcTicks,
                        IsMergedAcrossPlatforms = false,
                        PendingGameAssignment = folder.PendingGameAssignment,
                        CollectionNotes = folder.CollectionNotes ?? string.Empty
                    };
                    view.SourceFolders.Add(folder);
                    PopulateLibraryBrowserFolderViewSearchBlob(view);
                    return view;
                }).ToList();
            }

            return rawFolders
                .GroupBy(BuildLibraryBrowserAllMergeKey, StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var sourceFolders = group
                        .OrderByDescending(folder => folder.FileCount)
                        .ThenByDescending(GetLibraryFolderNewestDate)
                        .ThenBy(folder => folder.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    var primary = sourceFolders.FirstOrDefault();
                    var pathList = sourceFolders
                        .SelectMany(folder => folder.FilePaths ?? new string[0])
                        .Where(path => !string.IsNullOrWhiteSpace(path))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    var orderedPaths = pathList
                        .OrderByDescending(path => ResolveIndexedLibraryDate(libraryRoot, path))
                        .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    var pathBackedCount = orderedPaths.Length;
                    var sumFolderCounts = sourceFolders.Sum(folder => folder == null ? 0 : Math.Max(folder.FileCount, 0));
                    var platformLabels = sourceFolders
                        .Select(folder => NormalizeConsoleLabel(folder.PlatformLabel))
                        .Where(label => !string.IsNullOrWhiteSpace(label))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(label => PlatformGroupOrder(label))
                        .ThenBy(label => label, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    var distinctGameIds = sourceFolders
                        .Select(folder => NormalizeGameId(folder.GameId))
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    var tickMax = sourceFolders.Max(folder => folder == null ? 0L : folder.NewestCaptureUtcTicks);
                    if (tickMax == 0 && orderedPaths.Length > 0)
                    {
                        var fromIndex = ResolveIndexedLibraryDate(libraryRoot, orderedPaths[0]);
                        if (fromIndex > DateTime.MinValue)
                        {
                            try
                            {
                                tickMax = fromIndex.ToUniversalTime().Ticks;
                            }
                            catch
                            {
                            }
                        }
                    }
                    var tickMaxRecent = sourceFolders.Max(folder => folder == null ? 0L : folder.NewestRecentSortUtcTicks);
                    if (tickMaxRecent == 0 && orderedPaths.Length > 0)
                    {
                        tickMaxRecent = orderedPaths.Max(path => ResolveLibraryFileRecentSortUtcTicks(libraryRoot, path, null));
                    }
                    var previewImagePath = primary != null && !string.IsNullOrWhiteSpace(primary.PreviewImagePath)
                        ? primary.PreviewImagePath
                        : (orderedPaths.FirstOrDefault(IsImage) ?? orderedPaths.FirstOrDefault() ?? string.Empty);
                    var view = new LibraryBrowserFolderView
                    {
                        ViewKey = BuildLibraryBrowserViewKey("all", primary == null ? string.Empty : primary.GameId, primary == null ? string.Empty : primary.Name, primary == null ? string.Empty : primary.FolderPath, primary == null ? string.Empty : primary.PlatformLabel),
                        GameId = distinctGameIds.Count == 1 ? distinctGameIds[0] : string.Empty,
                        Name = primary == null ? string.Empty : (primary.Name ?? string.Empty),
                        PrimaryFolderPath = primary == null ? string.Empty : (primary.FolderPath ?? string.Empty),
                        PrimaryFolder = primary,
                        PrimaryPlatformLabel = platformLabels.FirstOrDefault() ?? NormalizeConsoleLabel(primary == null ? string.Empty : primary.PlatformLabel),
                        PlatformLabels = platformLabels,
                        PlatformSummaryText = BuildLibraryBrowserPlatformSummary(platformLabels),
                        FileCount = pathBackedCount > 0 ? pathBackedCount : sumFolderCounts,
                        PreviewImagePath = previewImagePath ?? string.Empty,
                        FilePaths = orderedPaths,
                        NewestCaptureUtcTicks = tickMax,
                        NewestRecentSortUtcTicks = tickMaxRecent,
                        SteamAppId = MergeLibraryBrowserExternalIdsForCombinedView(sourceFolders, f => f.SteamAppId, NormalizeConsoleLabel),
                        NonSteamId = MergeLibraryBrowserNonSteamIdForCombinedView(sourceFolders, NormalizeConsoleLabel),
                        SteamGridDbId = MergeLibraryBrowserExternalIdsForCombinedView(sourceFolders, f => f.SteamGridDbId, NormalizeConsoleLabel),
                        RetroAchievementsGameId = MergeLibraryBrowserRetroAchievementsGameIdForCombinedView(sourceFolders),
                        SuppressSteamAppIdAutoResolve = sourceFolders.All(folder => folder != null && folder.SuppressSteamAppIdAutoResolve),
                        SuppressSteamGridDbIdAutoResolve = sourceFolders.All(folder => folder != null && folder.SuppressSteamGridDbIdAutoResolve),
                        IsCompleted100Percent = sourceFolders.Any(folder => folder != null && folder.IsCompleted100Percent),
                        CompletedUtcTicks = sourceFolders.Select(folder => folder == null ? 0L : folder.CompletedUtcTicks).Where(ticks => ticks > 0).DefaultIfEmpty(0L).Max(),
                        IsMergedAcrossPlatforms = platformLabels.Length > 1,
                        PendingGameAssignment = sourceFolders.Any(f => f != null && f.PendingGameAssignment),
                        CollectionNotes = MergeLibraryBrowserCollectionNotesForCombinedView(sourceFolders)
                    };
                    view.SourceFolders.AddRange(sourceFolders);
                    PopulateLibraryBrowserFolderViewSearchBlob(view);
                    return view;
                })
                .ToList();
        }
    }
}
