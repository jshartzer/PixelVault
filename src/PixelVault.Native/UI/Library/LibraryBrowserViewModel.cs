using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PixelVaultNative
{
    /// <summary>
    /// PV-PLN-UI-001 Step 14: owned library read-model. Promoted from the
    /// <c>MainWindow.LibraryBrowserViewModel.cs</c> partial so the projection logic, merge-key
    /// math, timeline assembly, and folder-view caches are reachable as a plain class — the same
    /// shape a future iOS view-model, a backend projection host, or focused unit tests can
    /// construct directly without dragging in WPF / MainWindow state.
    /// </summary>
    /// <remarks>
    /// Dependencies on MainWindow state (the current library root, the current grouping mode,
    /// and ~13 instance-method helpers that know how to normalize ids, clone DTOs, and read the
    /// metadata index) are expressed through <see cref="ILibraryBrowserViewModelHost"/>. The
    /// host is queried live on every call — never captured — so mid-session changes to the
    /// library root or grouping mode propagate correctly.
    ///
    /// Pure-static helpers (<c>TextAndPathHelpers</c>, <c>LibraryPlatformLabels</c>,
    /// <c>MainWindow.NormalizeConsoleLabel</c>, <c>SettingsService.NormalizeLibraryGroupingMode</c>)
    /// are called directly since they carry no host state and shouldn't inflate the host
    /// interface.
    ///
    /// The merged "All" projection cache (<see cref="LibraryBrowserProjectionCache"/>) lives on
    /// this instance — it moved off <c>MainWindow</c> as part of Step 14 because its lifetime
    /// matches the view-model, not the window.
    /// </remarks>
    internal sealed class LibraryBrowserViewModel
    {
        readonly ILibraryBrowserViewModelHost _host;
        readonly LibraryBrowserProjectionCache _projectionCache = new LibraryBrowserProjectionCache();

        public LibraryBrowserViewModel(ILibraryBrowserViewModelHost host)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
        }

        public LibraryBrowserFolderView CloneLibraryBrowserFolderView(LibraryBrowserFolderView view)
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

        public void PopulateLibraryBrowserFolderViewSearchBlob(LibraryBrowserFolderView view)
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

        public LibraryFolderInfo GetLibraryBrowserPrimaryFolder(LibraryBrowserFolderView view)
        {
            if (view == null) return null;
            if (view.IsTimelineProjection) return null;
            if (view.PrimaryFolder != null) return view.PrimaryFolder;
            return view.SourceFolders.FirstOrDefault(folder => folder != null);
        }

        public LibraryFolderInfo BuildLibraryBrowserDisplayFolder(LibraryBrowserFolderView view)
        {
            if (view == null) return null;
            var primary = GetLibraryBrowserPrimaryFolder(view);
            var folder = _host.CloneLibraryFolderInfo(primary) ?? new LibraryFolderInfo();
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
                ? (primary == null ? string.Empty : (primary.CollectionNotes ?? string.Empty))
                : view.CollectionNotes;
            return folder;
        }

        public bool SameLibraryBrowserSelection(LibraryBrowserFolderView left, LibraryBrowserFolderView right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null) return false;
            return string.Equals(left.ViewKey ?? string.Empty, right.ViewKey ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        public string NormalizeLibraryGroupingMode(string value) => SettingsService.NormalizeLibraryGroupingMode(value);

        public bool IsLibraryBrowserTimelineMode()
        {
            return string.Equals(NormalizeLibraryGroupingMode(_host.LibraryGroupingMode), "timeline", StringComparison.OrdinalIgnoreCase);
        }

        public bool IsLibraryBrowserTimelineView(LibraryBrowserFolderView view)
        {
            return view != null && view.IsTimelineProjection;
        }

        public Dictionary<string, LibraryTimelineCaptureContext> BuildLibraryTimelineCaptureContextMap(
            IEnumerable<string> files,
            Dictionary<string, LibraryMetadataIndexEntry> metadataIndex,
            IEnumerable<GameIndexEditorRow> savedGameRows,
            Dictionary<string, EmbeddedMetadataSnapshot> metadataSnapshots = null)
        {
            var libraryRoot = _host.LibraryRoot;
            var contexts = new Dictionary<string, LibraryTimelineCaptureContext>(StringComparer.OrdinalIgnoreCase);
            var rowsByGameId = (savedGameRows ?? Enumerable.Empty<GameIndexEditorRow>())
                .Where(row => row != null && !string.IsNullOrWhiteSpace(_host.NormalizeGameId(row.GameId)))
                .GroupBy(row => _host.NormalizeGameId(row.GameId), StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.First(), StringComparer.OrdinalIgnoreCase);
            foreach (var file in (files ?? Enumerable.Empty<string>()).Where(path => !string.IsNullOrWhiteSpace(path)))
            {
                var captureDate = _host.ResolveIndexedLibraryDate(libraryRoot, file, metadataIndex);
                var entry = _host.TryGetLibraryMetadataIndexEntry(libraryRoot, file, metadataIndex);
                EmbeddedMetadataSnapshot metadataSnapshot;
                if (metadataSnapshots == null || !metadataSnapshots.TryGetValue(file, out metadataSnapshot) || metadataSnapshot == null) metadataSnapshot = null;
                if (metadataSnapshot != null && metadataSnapshot.CaptureTime.HasValue)
                    captureDate = metadataSnapshot.CaptureTime.Value;
                var normalizedGameId = _host.NormalizeGameId(entry == null ? string.Empty : entry.GameId);
                GameIndexEditorRow savedRow;
                rowsByGameId.TryGetValue(normalizedGameId, out savedRow);
                var gameTitle = _host.NormalizeGameIndexName(savedRow == null ? string.Empty : savedRow.Name);
                if (string.IsNullOrWhiteSpace(gameTitle)) gameTitle = _host.NormalizeGameIndexName(_host.GuessGameIndexNameForFile(file));
                if (string.IsNullOrWhiteSpace(gameTitle)) gameTitle = "Unknown Game";
                var platformLabel = MainWindow.NormalizeConsoleLabel(savedRow == null ? (entry == null ? string.Empty : entry.ConsoleLabel) : savedRow.PlatformLabel);
                if (string.IsNullOrWhiteSpace(platformLabel) || string.Equals(platformLabel, "Other", StringComparison.OrdinalIgnoreCase))
                {
                    platformLabel = MainWindow.NormalizeConsoleLabel(entry == null ? string.Empty : entry.ConsoleLabel);
                }
                if (string.IsNullOrWhiteSpace(platformLabel) || string.Equals(platformLabel, "Other", StringComparison.OrdinalIgnoreCase))
                {
                    platformLabel = MainWindow.NormalizeConsoleLabel(_host.PrimaryPlatformLabel(file));
                }
                contexts[file] = new LibraryTimelineCaptureContext
                {
                    GameTitle = gameTitle,
                    PlatformLabel = platformLabel,
                    CaptureDate = captureDate,
                    Comment = metadataSnapshot == null ? string.Empty : TextAndPathHelpers.CleanComment(metadataSnapshot.Comment ?? string.Empty)
                };
            }
            return contexts;
        }

        public string BuildLibraryBrowserViewKey(string groupingMode, string gameId, string name, string folderPath, string platformLabel)
        {
            var normalizedGrouping = NormalizeLibraryGroupingMode(groupingMode);
            var normalizedGameId = _host.NormalizeGameId(gameId);
            var normalizedName = _host.NormalizeGameIndexName(name, folderPath);
            if (string.Equals(normalizedGrouping, "console", StringComparison.OrdinalIgnoreCase))
            {
                if (!string.IsNullOrWhiteSpace(normalizedGameId))
                {
                    return normalizedGrouping + "|" + normalizedGameId + "|" + MainWindow.NormalizeConsoleLabel(platformLabel) + "|" + (folderPath ?? string.Empty).Trim();
                }
                return normalizedGrouping + "|" + normalizedName + "|" + MainWindow.NormalizeConsoleLabel(platformLabel) + "|" + (folderPath ?? string.Empty).Trim();
            }
            if (!string.IsNullOrWhiteSpace(normalizedName)) return normalizedGrouping + "|name|" + normalizedName;
            if (!string.IsNullOrWhiteSpace(normalizedGameId)) return normalizedGrouping + "|id|" + normalizedGameId;
            return normalizedGrouping + "|folder|" + ((folderPath ?? string.Empty).Trim());
        }

        public string BuildLibraryBrowserPlatformSummary(IEnumerable<string> platformLabels)
        {
            var labels = (platformLabels ?? Enumerable.Empty<string>())
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .Select(label => MainWindow.NormalizeConsoleLabel(label))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(label => LibraryPlatformLabels.PlatformGroupOrder(label))
                .ThenBy(label => label, StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (labels.Count == 0) return "Other";
            if (labels.Count == 1) return labels[0];
            if (labels.Count == 2) return labels[0] + " + " + labels[1];
            return labels.Count + " platforms";
        }

        public string DetermineLibraryBrowserGroup(LibraryBrowserFolderView view)
        {
            return MainWindow.NormalizeConsoleLabel(view == null ? string.Empty : view.PrimaryPlatformLabel);
        }

        public string BuildLibraryBrowserAllMergeKey(LibraryFolderInfo folder)
        {
            if (folder == null) return string.Empty;
            if (folder.PendingGameAssignment)
                return "unassigned|" + ((folder.FolderPath ?? string.Empty).Trim());
            var normalizedName = _host.NormalizeGameIndexName(folder.Name, folder.FolderPath);
            if (!string.IsNullOrWhiteSpace(normalizedName)) return "name|" + normalizedName;
            var normalizedGameId = _host.NormalizeGameId(folder.GameId);
            if (!string.IsNullOrWhiteSpace(normalizedGameId)) return "id|" + normalizedGameId;
            return "folder|" + ((folder.FolderPath ?? string.Empty).Trim());
        }

        public int CountLibraryBrowserSourceFolders(LibraryBrowserFolderView view)
        {
            return (view == null ? Enumerable.Empty<LibraryFolderInfo>() : view.SourceFolders)
                .Select(folder => folder == null ? string.Empty : folder.FolderPath ?? string.Empty)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
        }

        public List<string> GetLibraryBrowserSourceFolderPaths(LibraryBrowserFolderView view)
        {
            return (view == null ? Enumerable.Empty<LibraryFolderInfo>() : view.SourceFolders)
                .Select(folder => folder == null ? string.Empty : folder.FolderPath ?? string.Empty)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public List<LibraryFolderInfo> GetLibraryBrowserActionFolders(LibraryBrowserFolderView view)
        {
            return (view == null ? Enumerable.Empty<LibraryFolderInfo>() : view.SourceFolders)
                .Where(folder => folder != null && !string.IsNullOrWhiteSpace(folder.FolderPath))
                .GroupBy(folder => folder.FolderPath ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .OrderBy(folder => folder.FolderPath ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public bool ShouldShowLibraryBrowserPlatformContext()
        {
            return string.Equals(NormalizeLibraryGroupingMode(_host.LibraryGroupingMode), "console", StringComparison.OrdinalIgnoreCase);
        }

        public string BuildLibraryBrowserFolderTileSubtitle(LibraryBrowserFolderView view)
        {
            var captureCount = view == null ? 0 : Math.Max(view.FileCount, 0);
            var captureText = captureCount + " capture" + (captureCount == 1 ? string.Empty : "s");
            string core;
            if (ShouldShowLibraryBrowserPlatformContext())
            {
                var platformText = view == null ? string.Empty : TextAndPathHelpers.CleanTag(view.PlatformSummaryText);
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

        public string BuildLibraryBrowserDetailMetaText(LibraryBrowserFolderView view, LibraryFolderInfo actionFolder)
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
                var platformText = view == null ? string.Empty : TextAndPathHelpers.CleanTag(view.PlatformSummaryText);
                var locationText = sourceFolderCount > 1 ? sourceFolderCount + " source folders" : folderPathText;
                if (string.IsNullOrWhiteSpace(platformText)) return itemText + " | " + locationText;
                return itemText + " | " + platformText + " | " + locationText;
            }

            if (sourceFolderCount > 1) return itemText + " | " + sourceFolderCount + " source folders";
            return string.IsNullOrWhiteSpace(folderPathText) ? itemText : itemText + " | " + folderPathText;
        }

        public string BuildLibraryBrowserScopeLabel(LibraryBrowserFolderView view)
        {
            if (view == null) return string.Empty;
            if (!ShouldShowLibraryBrowserPlatformContext()) return view.Name ?? string.Empty;
            var platformText = TextAndPathHelpers.CleanTag(view.PlatformSummaryText);
            return string.IsNullOrWhiteSpace(platformText)
                ? (view.Name ?? string.Empty)
                : ((view.Name ?? string.Empty) + " | " + platformText);
        }

        public string BuildLibraryBrowserActionScopeLabel(LibraryBrowserFolderView view)
        {
            if (view == null) return string.Empty;
            var sourceFolderCount = CountLibraryBrowserSourceFolders(view);
            if (!ShouldShowLibraryBrowserPlatformContext() && sourceFolderCount > 1)
            {
                return (view.Name ?? string.Empty) + " (" + sourceFolderCount + " folders)";
            }
            return BuildLibraryBrowserScopeLabel(view);
        }

        public string BuildLibraryBrowserOpenFoldersLabel(LibraryBrowserFolderView view)
        {
            if (IsLibraryBrowserTimelineView(view)) return "Open Source Folders";
            return CountLibraryBrowserSourceFolders(view) > 1 ? "Open Folders" : "Open Folder";
        }

        public string BuildLibraryBrowserTroubleshootingLabel(LibraryBrowserFolderView view)
        {
            if (view == null)
            {
                return "view=(none); grouping=" + NormalizeLibraryGroupingMode(_host.LibraryGroupingMode);
            }

            var platformText = string.Join(",",
                (view.PlatformLabels ?? new string[0])
                    .Where(label => !string.IsNullOrWhiteSpace(label))
                    .Select(label => MainWindow.NormalizeConsoleLabel(label))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(label => LibraryPlatformLabels.PlatformGroupOrder(label))
                    .ThenBy(label => label, StringComparer.OrdinalIgnoreCase));
            if (string.IsNullOrWhiteSpace(platformText))
            {
                platformText = MainWindow.NormalizeConsoleLabel(view.PrimaryPlatformLabel);
            }
            if (string.IsNullOrWhiteSpace(platformText))
            {
                platformText = "(none)";
            }

            return "viewKey=" + _host.FormatViewKeyForTroubleshooting(view.ViewKey ?? string.Empty)
                + "; name=" + (view.Name ?? string.Empty)
                + "; files=" + Math.Max(view.FileCount, 0)
                + "; sourceFolders=" + CountLibraryBrowserSourceFolders(view)
                + "; timeline=" + (view.IsTimelineProjection ? "1" : "0")
                + "; platforms=" + platformText
                + "; primaryFolder=" + _host.FormatPathForTroubleshooting(view.PrimaryFolderPath ?? string.Empty)
                + "; grouping=" + NormalizeLibraryGroupingMode(_host.LibraryGroupingMode);
        }

        public LibraryBrowserFolderView BuildLibraryBrowserTimelineView(IEnumerable<LibraryBrowserFolderView> visibleFolders)
        {
            var libraryRoot = _host.LibraryRoot;
            var sourceViews = (visibleFolders ?? Enumerable.Empty<LibraryBrowserFolderView>())
                .Where(view => view != null)
                .ToList();
            var orderedImagePaths = sourceViews
                .SelectMany(view => view.FilePaths ?? new string[0])
                .Where(path => !string.IsNullOrWhiteSpace(path) && TextAndPathHelpers.IsImage(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderByDescending(path => _host.ResolveIndexedLibraryDate(libraryRoot, path))
                .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var newest = orderedImagePaths.Length == 0 ? DateTime.MinValue : _host.ResolveIndexedLibraryDate(libraryRoot, orderedImagePaths[0]);
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

        public void ApplyRemovedFilesToLibraryBrowserState(MainWindow.LibraryBrowserWorkingSet ws, IEnumerable<string> removedFiles)
        {
            if (ws == null) return;
            var libraryRoot = _host.LibraryRoot;
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
                    .OrderByDescending(path => _host.ResolveIndexedLibraryDate(libraryRoot, path))
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
                    .Select(path => _host.ResolveIndexedLibraryDate(libraryRoot, path))
                    .DefaultIfEmpty(DateTime.MinValue)
                    .Max();
                folder.NewestCaptureUtcTicks = newest > DateTime.MinValue ? newest.ToUniversalTime().Ticks : 0;
                folder.NewestRecentSortUtcTicks = remainingPaths.Length == 0
                    ? 0
                    : remainingPaths.Max(path => _host.ResolveLibraryFileRecentSortUtcTicks(libraryRoot, path, null));

                var previewPath = folder.PreviewImagePath ?? string.Empty;
                if (removedSet.Contains(previewPath) || string.IsNullOrWhiteSpace(previewPath) || !File.Exists(previewPath))
                {
                    folder.PreviewImagePath = remainingPaths.FirstOrDefault(p => TextAndPathHelpers.IsImage(p)) ?? remainingPaths.FirstOrDefault() ?? string.Empty;
                }
            }

            ws.DetailFilesDisplayOrder.RemoveAll(path => removedSet.Contains(path) || string.IsNullOrWhiteSpace(path) || !File.Exists(path));
            foreach (var stale in ws.SelectedDetailFiles.Where(path => removedSet.Contains(path) || string.IsNullOrWhiteSpace(path) || !File.Exists(path)).ToList())
            {
                ws.SelectedDetailFiles.Remove(stale);
            }
        }

        public LibraryBrowserFolderView FindMatchingLibraryBrowserView(LibraryBrowserFolderView current, IList<LibraryBrowserFolderView> candidates)
        {
            if (current == null || candidates == null || candidates.Count == 0) return null;
            var exact = candidates.FirstOrDefault(candidate => SameLibraryBrowserSelection(current, candidate));
            if (exact != null) return exact;

            var currentPrimary = GetLibraryBrowserPrimaryFolder(current);
            if (currentPrimary != null)
            {
                var byPrimary = candidates.FirstOrDefault(candidate =>
                    candidate != null && candidate.SourceFolders.Any(source => _host.SameLibraryFolderSelection(source, currentPrimary)));
                if (byPrimary != null) return byPrimary;
            }

            var normalizedGameId = _host.NormalizeGameId(current.GameId);
            if (!string.IsNullOrWhiteSpace(normalizedGameId))
            {
                var byGameId = candidates.FirstOrDefault(candidate =>
                    candidate != null && string.Equals(_host.NormalizeGameId(candidate.GameId), normalizedGameId, StringComparison.OrdinalIgnoreCase));
                if (byGameId != null) return byGameId;
            }

            var normalizedName = _host.NormalizeGameIndexName(current.Name, current.PrimaryFolderPath);
            if (!string.IsNullOrWhiteSpace(normalizedName))
            {
                return candidates.FirstOrDefault(candidate =>
                    candidate != null && string.Equals(
                        _host.NormalizeGameIndexName(candidate.Name, candidate.PrimaryFolderPath),
                        normalizedName,
                        StringComparison.OrdinalIgnoreCase));
            }

            return null;
        }

        public LibraryBrowserFolderView FindLibraryBrowserViewByViewKey(IEnumerable<LibraryBrowserFolderView> candidates, string viewKey)
        {
            if (string.IsNullOrWhiteSpace(viewKey) || candidates == null) return null;
            return candidates.FirstOrDefault(c => c != null && string.Equals(c.ViewKey ?? string.Empty, viewKey, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Sort key for folder tiles: prefer precomputed UTC ticks on the view; avoid Alloc in OrderBy hot path.</summary>
        public DateTime GetLibraryBrowserFolderViewSortNewest(LibraryBrowserFolderView view)
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
            return _host.GetLibraryFolderNewestDate(BuildLibraryBrowserDisplayFolder(view));
        }

        /// <summary>Sort key for Recently Added: index date-added when known, else capture/file date (see <see cref="ILibraryBrowserViewModelHost.ResolveLibraryFileRecentSortUtcTicks"/>).</summary>
        public DateTime GetLibraryBrowserFolderViewSortRecentlyAdded(LibraryBrowserFolderView view)
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

        /// <summary>
        /// PV-PLN-UI-001 Step 13 Pass C: the merged "All" projection cache lives on this
        /// instance. Console mode is un-cached (per-folder projection is cheap); "All" mode
        /// hashes the folder list and reuses the last projection if the hash is stable.
        /// </summary>
        public List<LibraryBrowserFolderView> GetOrBuildLibraryBrowserFolderViews(IReadOnlyList<LibraryFolderInfo> folders, string groupingMode)
            => _projectionCache.GetOrBuild(
                folders,
                groupingMode,
                value => NormalizeLibraryGroupingMode(value),
                (rawFolders, mode) => BuildLibraryBrowserFolderViews(rawFolders, mode));

        public List<LibraryBrowserFolderView> BuildLibraryBrowserFolderViews(IEnumerable<LibraryFolderInfo> folders, string groupingMode)
        {
            var libraryRoot = _host.LibraryRoot;
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
                        GameId = _host.NormalizeGameId(folder.GameId),
                        Name = folder.Name ?? string.Empty,
                        PrimaryFolderPath = folder.FolderPath ?? string.Empty,
                        PrimaryFolder = folder,
                        PrimaryPlatformLabel = MainWindow.NormalizeConsoleLabel(folder.PlatformLabel),
                        PlatformLabels = new[] { MainWindow.NormalizeConsoleLabel(folder.PlatformLabel) },
                        PlatformSummaryText = MainWindow.NormalizeConsoleLabel(folder.PlatformLabel),
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
                .GroupBy(folder => BuildLibraryBrowserAllMergeKey(folder), StringComparer.OrdinalIgnoreCase)
                .Select(group =>
                {
                    var sourceFolders = group
                        .OrderByDescending(folder => folder.FileCount)
                        .ThenByDescending(folder => _host.GetLibraryFolderNewestDate(folder))
                        .ThenBy(folder => folder.Name ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    var primary = sourceFolders.FirstOrDefault();
                    var pathList = sourceFolders
                        .SelectMany(folder => folder.FilePaths ?? new string[0])
                        .Where(path => !string.IsNullOrWhiteSpace(path))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    var orderedPaths = pathList
                        .OrderByDescending(path => _host.ResolveIndexedLibraryDate(libraryRoot, path))
                        .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    var pathBackedCount = orderedPaths.Length;
                    var sumFolderCounts = sourceFolders.Sum(folder => folder == null ? 0 : Math.Max(folder.FileCount, 0));
                    var platformLabels = sourceFolders
                        .Select(folder => MainWindow.NormalizeConsoleLabel(folder.PlatformLabel))
                        .Where(label => !string.IsNullOrWhiteSpace(label))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .OrderBy(label => LibraryPlatformLabels.PlatformGroupOrder(label))
                        .ThenBy(label => label, StringComparer.OrdinalIgnoreCase)
                        .ToArray();
                    var distinctGameIds = sourceFolders
                        .Select(folder => _host.NormalizeGameId(folder.GameId))
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    var tickMax = sourceFolders.Max(folder => folder == null ? 0L : folder.NewestCaptureUtcTicks);
                    if (tickMax == 0 && orderedPaths.Length > 0)
                    {
                        var fromIndex = _host.ResolveIndexedLibraryDate(libraryRoot, orderedPaths[0]);
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
                        tickMaxRecent = orderedPaths.Max(path => _host.ResolveLibraryFileRecentSortUtcTicks(libraryRoot, path, null));
                    }
                    var previewImagePath = primary != null && !string.IsNullOrWhiteSpace(primary.PreviewImagePath)
                        ? primary.PreviewImagePath
                        : (orderedPaths.FirstOrDefault(p => TextAndPathHelpers.IsImage(p)) ?? orderedPaths.FirstOrDefault() ?? string.Empty);
                    var view = new LibraryBrowserFolderView
                    {
                        ViewKey = BuildLibraryBrowserViewKey("all", primary == null ? string.Empty : primary.GameId, primary == null ? string.Empty : primary.Name, primary == null ? string.Empty : primary.FolderPath, primary == null ? string.Empty : primary.PlatformLabel),
                        GameId = distinctGameIds.Count == 1 ? distinctGameIds[0] : string.Empty,
                        Name = primary == null ? string.Empty : (primary.Name ?? string.Empty),
                        PrimaryFolderPath = primary == null ? string.Empty : (primary.FolderPath ?? string.Empty),
                        PrimaryFolder = primary,
                        PrimaryPlatformLabel = platformLabels.FirstOrDefault() ?? MainWindow.NormalizeConsoleLabel(primary == null ? string.Empty : primary.PlatformLabel),
                        PlatformLabels = platformLabels,
                        PlatformSummaryText = BuildLibraryBrowserPlatformSummary(platformLabels),
                        FileCount = pathBackedCount > 0 ? pathBackedCount : sumFolderCounts,
                        PreviewImagePath = previewImagePath ?? string.Empty,
                        FilePaths = orderedPaths,
                        NewestCaptureUtcTicks = tickMax,
                        NewestRecentSortUtcTicks = tickMaxRecent,
                        SteamAppId = LibraryBrowserViewModelMath.MergeLibraryBrowserExternalIdsForCombinedView(sourceFolders, f => f.SteamAppId, MainWindow.NormalizeConsoleLabel),
                        NonSteamId = LibraryBrowserViewModelMath.MergeLibraryBrowserNonSteamIdForCombinedView(sourceFolders, MainWindow.NormalizeConsoleLabel),
                        SteamGridDbId = LibraryBrowserViewModelMath.MergeLibraryBrowserExternalIdsForCombinedView(sourceFolders, f => f.SteamGridDbId, MainWindow.NormalizeConsoleLabel),
                        RetroAchievementsGameId = LibraryBrowserViewModelMath.MergeLibraryBrowserRetroAchievementsGameIdForCombinedView(sourceFolders),
                        SuppressSteamAppIdAutoResolve = sourceFolders.All(folder => folder != null && folder.SuppressSteamAppIdAutoResolve),
                        SuppressSteamGridDbIdAutoResolve = sourceFolders.All(folder => folder != null && folder.SuppressSteamGridDbIdAutoResolve),
                        IsCompleted100Percent = sourceFolders.Any(folder => folder != null && folder.IsCompleted100Percent),
                        CompletedUtcTicks = sourceFolders.Select(folder => folder == null ? 0L : folder.CompletedUtcTicks).Where(ticks => ticks > 0).DefaultIfEmpty(0L).Max(),
                        IsMergedAcrossPlatforms = platformLabels.Length > 1,
                        PendingGameAssignment = sourceFolders.Any(f => f != null && f.PendingGameAssignment),
                        CollectionNotes = LibraryBrowserViewModelMath.MergeLibraryBrowserCollectionNotesForCombinedView(sourceFolders)
                    };
                    view.SourceFolders.AddRange(sourceFolders);
                    PopulateLibraryBrowserFolderViewSearchBlob(view);
                    return view;
                })
                .ToList();
        }
    }
}
