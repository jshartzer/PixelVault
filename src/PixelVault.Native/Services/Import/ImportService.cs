using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace PixelVaultNative
{
    internal interface IImportService
    {
        List<UndoImportEntry> LoadUndoManifest();

        void SaveUndoManifest(List<UndoImportEntry> entries);

        MoveStepResult MoveFilesToLibraryDestination(
            IEnumerable<string> files,
            string summaryLabel,
            Action<int, int, string> progress = null,
            CancellationToken cancellationToken = default);

        SortStepResult SortDestinationRootIntoGameFolders(string destinationRoot, string libraryRoot, CancellationToken cancellationToken = default);

        UndoImportExecutionResult ExecuteUndoImportMoves(IEnumerable<UndoImportEntry> entries);

        /// <summary>Top-level and optional recursive media file lists from configured source roots.</summary>
        SourceInventory BuildSourceInventory(bool recurseRename);

        /// <summary>Deletes each path that exists (e.g. import “delete before processing”). Skips missing files.</summary>
        DeleteStepResult DeleteSourceFiles(IEnumerable<string> filePaths, Action<int, int, string> progress = null, CancellationToken cancellationToken = default);

        /// <summary>Steam AppID-based renames in the upload folder (canonical store title + timestamp suffix rules).</summary>
        RenameStepResult RunSteamRename(IEnumerable<string> sourceFiles, Action<int, int, string> progress = null, CancellationToken cancellationToken = default);

        /// <summary>Prefix filenames with sanitized game title for manual metadata / library edit flows (mutates item paths).</summary>
        RenameStepResult RunManualRename(List<ManualMetadataItem> items, Action<int, int, string> progress = null, CancellationToken cancellationToken = default);
    }

    /// <summary>Outcome of moving files back to source folders during undo (no UI).</summary>
    internal sealed class UndoImportExecutionResult
    {
        public int Moved;
        public int Skipped;
        public List<UndoImportEntry> RemainingEntries = new List<UndoImportEntry>();
        public List<string> RemovedFromLibraryPaths = new List<string>();
    }

    internal sealed class ImportServiceDependencies
    {
        public Func<string> UndoManifestPath;
        public Func<string> GetDestinationRoot;
        public Func<string> GetLibraryRoot;
        public Func<string> GetConflictMode;
        public Func<string, string> UniquePath;
        public Action<string, string> MoveMetadataSidecarIfPresent;
        public Action<string, string, List<UndoImportEntry>> AddSidecarUndoEntryIfPresent;
        public Action<string> Log;
        public Func<string, bool> IsMedia;
        public Func<string, string> GetSafeGameFolderName;
        public Func<string, string> GetGameNameFromFileName;
        public Action<string, string> EnsureDirectoryExists;
        public Func<ILibraryScanner> GetLibraryScanner;

        /// <summary>Enumerate files under all configured source roots (deduped, full paths).</summary>
        public Func<SearchOption, Func<string, bool>, IEnumerable<string>> EnumerateSourceMediaFiles;

        /// <summary>Parse filename for intake (Steam AppID, title hint).</summary>
        public Func<string, FilenameParseResult> ParseFilenameForImport;

        /// <summary>Resolve Steam store title for an AppID (sync; may call network).</summary>
        public Func<string, string> ResolveSteamStoreTitle;

        /// <summary>Record Steam AppID on the saved game index when first seen during rename.</summary>
        public Action<string, string, string> EnsureSteamAppIdInGameIndex;

        /// <summary>Sanitize user-entered game title for use in filenames.</summary>
        public Func<string, string> SanitizeManualRenameGameTitle;

        /// <summary>Normalize title for “already game-prefixed?” checks during manual rename.</summary>
        public Func<string, string> NormalizeTitleForManualRename;
    }

    internal sealed class ImportService : IImportService
    {
        readonly ImportServiceDependencies d;

        public ImportService(ImportServiceDependencies dependencies)
        {
            d = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        }

        string UndoPath => d.UndoManifestPath == null ? string.Empty : (d.UndoManifestPath() ?? string.Empty);

        public List<UndoImportEntry> LoadUndoManifest()
        {
            var path = UndoPath;
            var entries = new List<UndoImportEntry>();
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return entries;
            foreach (var line in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var parts = line.Split('\t');
                if (parts.Length < 3) continue;
                entries.Add(new UndoImportEntry { SourceDirectory = parts[0], ImportedFileName = parts[1], CurrentPath = parts[2] });
            }
            return entries;
        }

        public void SaveUndoManifest(List<UndoImportEntry> entries)
        {
            var path = UndoPath;
            if (string.IsNullOrWhiteSpace(path)) return;
            if (entries == null || entries.Count == 0)
            {
                if (File.Exists(path)) File.Delete(path);
                return;
            }

            File.WriteAllLines(path, entries.Select(entry => string.Join("\t", new[]
            {
                entry.SourceDirectory ?? string.Empty,
                entry.ImportedFileName ?? string.Empty,
                entry.CurrentPath ?? string.Empty
            })).ToArray());
        }

        public MoveStepResult MoveFilesToLibraryDestination(
            IEnumerable<string> files,
            string summaryLabel,
            Action<int, int, string> progress = null,
            CancellationToken cancellationToken = default)
        {
            var destinationRoot = d.GetDestinationRoot == null ? string.Empty : d.GetDestinationRoot() ?? string.Empty;
            int moved = 0, skipped = 0, renamedConflict = 0;
            var entries = new List<UndoImportEntry>();
            var fileList = (files ?? Enumerable.Empty<string>()).Where(File.Exists).ToList();
            var total = fileList.Count;
            if (progress != null) progress(0, total, "Starting move step for " + total + " file(s).");
            for (int i = 0; i < total; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var file = fileList[i];
                var remaining = total - (i + 1);
                var sourceDirectory = Path.GetDirectoryName(file);
                var target = Path.Combine(destinationRoot, Path.GetFileName(file));
                if (File.Exists(target))
                {
                    var mode = d.GetConflictMode == null ? "Rename" : (d.GetConflictMode() ?? "Rename");
                    if (string.Equals(mode, "Skip", StringComparison.OrdinalIgnoreCase))
                    {
                        skipped++;
                        if (progress != null) progress(i + 1, total, "Skipped move " + (i + 1) + " of " + total + " | " + remaining + " remaining | conflict | " + Path.GetFileName(file));
                        continue;
                    }
                    if (string.Equals(mode, "Rename", StringComparison.OrdinalIgnoreCase))
                    {
                        target = d.UniquePath == null ? target : d.UniquePath(target);
                        renamedConflict++;
                    }
                    if (string.Equals(mode, "Overwrite", StringComparison.OrdinalIgnoreCase)) File.Delete(target);
                }
                File.Move(file, target);
                d.MoveMetadataSidecarIfPresent?.Invoke(file, target);
                moved++;
                entries.Add(new UndoImportEntry { SourceDirectory = sourceDirectory, ImportedFileName = Path.GetFileName(target), CurrentPath = target });
                d.AddSidecarUndoEntryIfPresent?.Invoke(target, sourceDirectory, entries);
                d.Log?.Invoke("Moved: " + Path.GetFileName(file) + " -> " + target);
                if (progress != null) progress(i + 1, total, "Moved " + (i + 1) + " of " + total + " | " + remaining + " remaining | " + Path.GetFileName(target));
            }
            if (progress != null) progress(total, total, summaryLabel + ": moved " + moved + ", skipped " + skipped + ", renamed-on-conflict " + renamedConflict + ".");
            d.Log?.Invoke(summaryLabel + ": moved " + moved + ", skipped " + skipped + ", renamed-on-conflict " + renamedConflict + ".");
            return new MoveStepResult { Moved = moved, Skipped = skipped, RenamedOnConflict = renamedConflict, Entries = entries };
        }

        public SortStepResult SortDestinationRootIntoGameFolders(string destinationRoot, string libraryRoot, CancellationToken cancellationToken = default)
        {
            d.EnsureDirectoryExists?.Invoke(destinationRoot, "Destination folder");
            var files = Directory.EnumerateFiles(destinationRoot, "*", SearchOption.TopDirectoryOnly).Where(f => d.IsMedia != null && d.IsMedia(f)).ToList();
            if (files.Count == 0)
            {
                d.Log?.Invoke("Sort destination found no root-level media files to organize.");
                return new SortStepResult();
            }

            int moved = 0, created = 0, renamedConflict = 0;
            var indexedTargets = new List<string>();
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var folderName = d.GetSafeGameFolderName == null
                    ? string.Empty
                    : d.GetSafeGameFolderName(d.GetGameNameFromFileName == null
                        ? Path.GetFileNameWithoutExtension(file)
                        : d.GetGameNameFromFileName(Path.GetFileNameWithoutExtension(file)));
                var targetDirectory = Path.Combine(destinationRoot, folderName);
                if (!Directory.Exists(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                    created++;
                }

                var target = Path.Combine(targetDirectory, Path.GetFileName(file));
                if (File.Exists(target))
                {
                    target = d.UniquePath == null ? target : d.UniquePath(target);
                    renamedConflict++;
                }

                File.Move(file, target);
                d.MoveMetadataSidecarIfPresent?.Invoke(file, target);
                moved++;
                indexedTargets.Add(target);
                d.Log?.Invoke("Sorted: " + Path.GetFileName(file) + " -> " + target);
            }

            var scanner = d.GetLibraryScanner == null ? null : d.GetLibraryScanner();
            scanner?.UpsertLibraryMetadataIndexEntries(indexedTargets, libraryRoot);
            d.Log?.Invoke("Sort summary: sorted " + moved + ", folders created " + created + ", renamed-on-conflict " + renamedConflict + ".");
            return new SortStepResult { Sorted = moved, FoldersCreated = created, RenamedOnConflict = renamedConflict };
        }

        public UndoImportExecutionResult ExecuteUndoImportMoves(IEnumerable<UndoImportEntry> entries)
        {
            var result = new UndoImportExecutionResult();
            var destinationRoot = d.GetDestinationRoot == null ? string.Empty : d.GetDestinationRoot() ?? string.Empty;
            var libraryRoot = d.GetLibraryRoot == null ? string.Empty : d.GetLibraryRoot() ?? string.Empty;
            var usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in entries ?? Enumerable.Empty<UndoImportEntry>())
            {
                var currentPath = ResolveUndoCurrentPath(entry, usedPaths, destinationRoot, libraryRoot);
                if (string.IsNullOrWhiteSpace(currentPath) || !File.Exists(currentPath))
                {
                    result.Skipped++;
                    result.RemainingEntries.Add(entry);
                    d.Log?.Invoke("Undo skipped: could not find " + entry.ImportedFileName + " in the destination/library folders.");
                    continue;
                }

                Directory.CreateDirectory(entry.SourceDirectory);
                var target = d.UniquePath == null
                    ? Path.Combine(entry.SourceDirectory, Path.GetFileName(currentPath))
                    : d.UniquePath(Path.Combine(entry.SourceDirectory, Path.GetFileName(currentPath)));
                File.Move(currentPath, target);
                result.Moved++;
                result.RemovedFromLibraryPaths.Add(currentPath);
                d.Log?.Invoke("Undo move: " + currentPath + " -> " + target);
            }
            return result;
        }

        public SourceInventory BuildSourceInventory(bool recurseRename)
        {
            var enumerate = d.EnumerateSourceMediaFiles;
            var isMedia = d.IsMedia;
            if (enumerate == null || isMedia == null) return new SourceInventory();
            var topLevelMediaFiles = enumerate(SearchOption.TopDirectoryOnly, isMedia).ToList();
            return new SourceInventory
            {
                TopLevelMediaFiles = topLevelMediaFiles,
                RenameScopeFiles = recurseRename
                    ? enumerate(SearchOption.AllDirectories, isMedia).ToList()
                    : topLevelMediaFiles.ToList()
            };
        }

        public DeleteStepResult DeleteSourceFiles(IEnumerable<string> filePaths, Action<int, int, string> progress = null, CancellationToken cancellationToken = default)
        {
            int deleted = 0, skipped = 0;
            var paths = (filePaths ?? Enumerable.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var total = paths.Count;
            if (progress != null) progress(0, total, "Starting delete step for " + total + " file(s).");
            for (int i = 0; i < total; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = paths[i];
                var remaining = total - (i + 1);
                if (!File.Exists(path))
                {
                    skipped++;
                    if (progress != null) progress(i + 1, total, "Skipped delete " + (i + 1) + " of " + total + " | " + remaining + " remaining | file missing");
                    continue;
                }
                File.Delete(path);
                deleted++;
                var name = Path.GetFileName(path);
                d.Log?.Invoke("Deleted before processing: " + name);
                if (progress != null) progress(i + 1, total, "Deleted " + (i + 1) + " of " + total + " | " + remaining + " remaining | " + name);
            }
            if (progress != null) progress(total, total, "Delete step complete: deleted " + deleted + ", skipped " + skipped + ".");
            if (deleted > 0 || skipped > 0) d.Log?.Invoke("Delete summary: deleted " + deleted + ", skipped " + skipped + ".");
            return new DeleteStepResult { Deleted = deleted, Skipped = skipped };
        }

        public RenameStepResult RunSteamRename(IEnumerable<string> sourceFiles, Action<int, int, string> progress = null, CancellationToken cancellationToken = default)
        {
            int renamed = 0, skipped = 0;
            var pathMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var recordedSteamAppIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var parse = d.ParseFilenameForImport;
            var steamTitle = d.ResolveSteamStoreTitle;
            if (parse == null || steamTitle == null)
            {
                d.Log?.Invoke("Steam rename skipped: ParseFilenameForImport or ResolveSteamStoreTitle not configured.");
                return new RenameStepResult { Renamed = 0, Skipped = 0, OldPathToNewPath = pathMap };
            }

            var libraryRoot = d.GetLibraryRoot == null ? string.Empty : d.GetLibraryRoot() ?? string.Empty;
            var files = (sourceFiles ?? Enumerable.Empty<string>()).Where(File.Exists).ToList();
            var total = files.Count;
            if (progress != null) progress(0, total, "Starting rename step for " + total + " file(s).");
            for (int i = 0; i < total; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var file = files[i];
                var remaining = total - (i + 1);
                var parsed = parse(file);
                var appId = parsed == null ? null : parsed.SteamAppId;
                if (string.IsNullOrWhiteSpace(appId))
                {
                    skipped++;
                    if (progress != null) progress(i + 1, total, "Skipped rename " + (i + 1) + " of " + total + " | " + remaining + " remaining | no Steam AppID in filename");
                    continue;
                }
                var game = steamTitle(appId);
                if (string.IsNullOrWhiteSpace(game))
                {
                    skipped++;
                    if (progress != null) progress(i + 1, total, "Skipped rename " + (i + 1) + " of " + total + " | " + remaining + " remaining | no Steam title match");
                    continue;
                }
                if (recordedSteamAppIds.Add(appId)) d.EnsureSteamAppIdInGameIndex?.Invoke(libraryRoot, game, appId);
                var baseName = Path.GetFileNameWithoutExtension(file);
                string newBase;
                var titleHint = parsed == null ? null : parsed.GameTitleHint;
                if (!SteamImportRename.TryBuildSteamRenameBase(baseName, appId, game, titleHint, out newBase))
                {
                    skipped++;
                    if (progress != null) progress(i + 1, total, "Skipped rename " + (i + 1) + " of " + total + " | " + remaining + " remaining | not AppID-prefixed or title_timestamp form | " + Path.GetFileName(file));
                    continue;
                }
                if (string.Equals(newBase, baseName, StringComparison.OrdinalIgnoreCase))
                {
                    skipped++;
                    if (progress != null) progress(i + 1, total, "Skipped rename " + (i + 1) + " of " + total + " | " + remaining + " remaining | already canonical name | " + Path.GetFileName(file));
                    continue;
                }
                var combined = Path.Combine(Path.GetDirectoryName(file) ?? string.Empty, newBase + Path.GetExtension(file));
                var target = d.UniquePath == null ? combined : d.UniquePath(combined);
                File.Move(file, target);
                pathMap[file] = target;
                d.MoveMetadataSidecarIfPresent?.Invoke(file, target);
                renamed++;
                d.Log?.Invoke("Renamed: " + Path.GetFileName(file) + " -> " + Path.GetFileName(target));
                if (progress != null) progress(i + 1, total, "Renamed " + (i + 1) + " of " + total + " | " + remaining + " remaining | " + Path.GetFileName(target));
            }
            if (progress != null) progress(total, total, "Rename step complete: renamed " + renamed + ", skipped " + skipped + ".");
            d.Log?.Invoke("Rename summary: renamed " + renamed + ", skipped " + skipped + ".");
            return new RenameStepResult { Renamed = renamed, Skipped = skipped, OldPathToNewPath = pathMap };
        }

        public RenameStepResult RunManualRename(List<ManualMetadataItem> items, Action<int, int, string> progress = null, CancellationToken cancellationToken = default)
        {
            var sanitize = d.SanitizeManualRenameGameTitle;
            var normalize = d.NormalizeTitleForManualRename;
            if (sanitize == null || normalize == null)
            {
                d.Log?.Invoke("Manual rename skipped: SanitizeManualRenameGameTitle or NormalizeTitleForManualRename not configured.");
                return new RenameStepResult();
            }

            int renamed = 0, skipped = 0;
            var total = items == null ? 0 : items.Count;
            if (progress != null) progress(0, total, "Starting rename step for " + total + " image(s).");
            for (int i = 0; i < total; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = items[i];
                var remaining = total - (i + 1);
                if (!File.Exists(item.FilePath))
                {
                    skipped++;
                    if (progress != null) progress(i + 1, total, "Skipped rename " + (i + 1) + " of " + total + " | " + remaining + " remaining | file missing");
                    continue;
                }
                var gameName = sanitize(item.GameName ?? string.Empty);
                if (string.IsNullOrWhiteSpace(gameName))
                {
                    skipped++;
                    if (progress != null) progress(i + 1, total, "Skipped rename " + (i + 1) + " of " + total + " | " + remaining + " remaining | no game title");
                    continue;
                }
                var currentBase = Path.GetFileNameWithoutExtension(item.FilePath);
                var normalizedCurrent = normalize(currentBase);
                var normalizedGame = normalize(gameName);
                if (currentBase.StartsWith(gameName + "_", StringComparison.OrdinalIgnoreCase) || normalizedCurrent == normalizedGame || normalizedCurrent.StartsWith(normalizedGame + " "))
                {
                    skipped++;
                    if (progress != null) progress(i + 1, total, "Skipped rename " + (i + 1) + " of " + total + " | " + remaining + " remaining | " + item.FileName);
                    continue;
                }
                var oldName = item.FileName;
                var dir = Path.GetDirectoryName(item.FilePath) ?? string.Empty;
                var combined = Path.Combine(dir, gameName + "_" + currentBase + Path.GetExtension(item.FilePath));
                var target = d.UniquePath == null ? combined : d.UniquePath(combined);
                var originalPath = item.FilePath;
                File.Move(item.FilePath, target);
                d.MoveMetadataSidecarIfPresent?.Invoke(originalPath, target);
                d.Log?.Invoke("Manual rename: " + oldName + " -> " + Path.GetFileName(target));
                item.FilePath = target;
                item.FileName = Path.GetFileName(target);
                renamed++;
                if (progress != null) progress(i + 1, total, "Renamed " + (i + 1) + " of " + total + " | " + remaining + " remaining | " + item.FileName);
            }
            if (progress != null) progress(total, total, "Rename step complete: renamed " + renamed + ", skipped " + skipped + ".");
            if (renamed > 0 || skipped > 0) d.Log?.Invoke("Manual rename summary: renamed " + renamed + ", skipped " + skipped + ".");
            return new RenameStepResult { Renamed = renamed, Skipped = skipped };
        }

        string ResolveUndoCurrentPath(UndoImportEntry entry, HashSet<string> usedPaths, string destinationRoot, string libraryRoot)
        {
            if (entry == null) return null;
            if (!string.IsNullOrWhiteSpace(entry.CurrentPath) && File.Exists(entry.CurrentPath))
            {
                var fullCurrent = Path.GetFullPath(entry.CurrentPath);
                if (usedPaths.Add(fullCurrent)) return fullCurrent;
            }

            foreach (var root in new[] { destinationRoot, libraryRoot }.Where(r => !string.IsNullOrWhiteSpace(r) && Directory.Exists(r)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                foreach (var candidate in Directory.EnumerateFiles(root, entry.ImportedFileName, SearchOption.AllDirectories)
                    .OrderByDescending(path => File.GetLastWriteTime(path)))
                {
                    var fullCandidate = Path.GetFullPath(candidate);
                    if (usedPaths.Add(fullCandidate)) return fullCandidate;
                }
            }
            return null;
        }
    }
}
