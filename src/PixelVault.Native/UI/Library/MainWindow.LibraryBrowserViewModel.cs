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
            internal string SteamAppId;
            internal string SteamGridDbId;
            internal bool SuppressSteamAppIdAutoResolve;
            internal bool SuppressSteamGridDbIdAutoResolve;
            internal bool IsMergedAcrossPlatforms;
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
                SteamAppId = view.SteamAppId,
                SteamGridDbId = view.SteamGridDbId,
                SuppressSteamAppIdAutoResolve = view.SuppressSteamAppIdAutoResolve,
                SuppressSteamGridDbIdAutoResolve = view.SuppressSteamGridDbIdAutoResolve,
                IsMergedAcrossPlatforms = view.IsMergedAcrossPlatforms,
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
            if (!string.IsNullOrWhiteSpace(view.SteamGridDbId)) parts.Add(view.SteamGridDbId.Trim());
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
            folder.SteamAppId = view.SteamAppId ?? string.Empty;
            folder.SteamGridDbId = view.SteamGridDbId ?? string.Empty;
            folder.SuppressSteamAppIdAutoResolve = view.SuppressSteamAppIdAutoResolve;
            folder.SuppressSteamGridDbIdAutoResolve = view.SuppressSteamGridDbIdAutoResolve;
            if (string.IsNullOrWhiteSpace(folder.PlatformLabel)) folder.PlatformLabel = view.PrimaryPlatformLabel ?? string.Empty;
            return folder;
        }

        bool SameLibraryBrowserSelection(LibraryBrowserFolderView left, LibraryBrowserFolderView right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null) return false;
            return string.Equals(left.ViewKey ?? string.Empty, right.ViewKey ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }

        string NormalizeLibraryGroupingMode(string value) => SettingsService.NormalizeLibraryGroupingMode(value);

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
                + "; platforms=" + platformText
                + "; primaryFolder=" + FormatPathForTroubleshooting(view.PrimaryFolderPath ?? string.Empty)
                + "; grouping=" + NormalizeLibraryGroupingMode(libraryGroupingMode);
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
                    h = h * 397 ^ (folder.PreviewImagePath ?? string.Empty).GetHashCode(StringComparison.OrdinalIgnoreCase);
                    h = h * 397 ^ (folder.SteamAppId ?? string.Empty).GetHashCode(StringComparison.OrdinalIgnoreCase);
                    h = h * 397 ^ (folder.SteamGridDbId ?? string.Empty).GetHashCode(StringComparison.OrdinalIgnoreCase);
                    h = h * 397 ^ (folder.SuppressSteamAppIdAutoResolve ? 1 : 0);
                    h = h * 397 ^ (folder.SuppressSteamGridDbIdAutoResolve ? 1 : 0);
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
                        SteamAppId = folder.SteamAppId ?? string.Empty,
                        SteamGridDbId = folder.SteamGridDbId ?? string.Empty,
                        SuppressSteamAppIdAutoResolve = folder.SuppressSteamAppIdAutoResolve,
                        SuppressSteamGridDbIdAutoResolve = folder.SuppressSteamGridDbIdAutoResolve,
                        IsMergedAcrossPlatforms = false
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
                    var distinctSteamAppIds = sourceFolders
                        .Select(folder => CleanTag(folder.SteamAppId))
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    var distinctGameIds = sourceFolders
                        .Select(folder => NormalizeGameId(folder.GameId))
                        .Where(value => !string.IsNullOrWhiteSpace(value))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    var distinctSteamGridDbIds = sourceFolders
                        .Select(folder => CleanTag(folder.SteamGridDbId))
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
                        SteamAppId = distinctSteamAppIds.Count == 1 ? distinctSteamAppIds[0] : string.Empty,
                        SteamGridDbId = distinctSteamGridDbIds.Count == 1 ? distinctSteamGridDbIds[0] : string.Empty,
                        SuppressSteamAppIdAutoResolve = sourceFolders.All(folder => folder != null && folder.SuppressSteamAppIdAutoResolve),
                        SuppressSteamGridDbIdAutoResolve = sourceFolders.All(folder => folder != null && folder.SuppressSteamGridDbIdAutoResolve),
                        IsMergedAcrossPlatforms = platformLabels.Length > 1
                    };
                    view.SourceFolders.AddRange(sourceFolders);
                    PopulateLibraryBrowserFolderViewSearchBlob(view);
                    return view;
                })
                .ToList();
        }
    }
}
