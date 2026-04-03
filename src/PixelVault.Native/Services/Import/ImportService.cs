using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

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

        /// <summary>Write Exif/metadata for intake review items (standard import / import-and-comment workflow).</summary>
        MetadataStepResult WriteMetadataForReviewItems(IEnumerable<ReviewItem> reviewItems, Action<int, int, string> progress = null, CancellationToken cancellationToken = default);

        /// <summary>
        /// Import-and-edit: for rows still tagged Steam, when the user did not change the loaded game title from the original,
        /// replace numeric/placeholder titles with the Steam store name for that AppID (same idea as automatic import).
        /// </summary>
        Task ApplyImportAndEditSteamStoreTitlesWhenGameNameUnchangedAsync(IEnumerable<ManualMetadataItem> items, CancellationToken cancellationToken = default);

        /// <summary>
        /// Manual metadata / import-and-edit finish: normalize each item’s display name, resolve <see cref="ManualMetadataItem.GameId"/>
        /// from the game index, copy canonical name and Steam AppID onto the matched row, then persist the index.
        /// </summary>
        void FinalizeManualMetadataItemsAgainstGameIndex(string libraryRoot, List<GameIndexEditorRow> gameRows, IEnumerable<ManualMetadataItem> pendingItems);

        /// <summary>
        /// Before final confirm: distinct sorted UI labels for pending rows whose name/platform/id are not yet in <paramref name="gameRows"/> (drives “Add New Game” prompt).
        /// </summary>
        List<string> BuildUnresolvedManualMetadataMasterRecordLabels(List<GameIndexEditorRow> gameRows, IEnumerable<ManualMetadataItem> pendingItems);

        /// <summary>
        /// After user accepts adding new master records: ensure placeholder rows exist in the in-memory <paramref name="gameRows"/> list (not persisted until <see cref="FinalizeManualMetadataItemsAgainstGameIndex"/>).
        /// </summary>
        void EnsureNewManualMetadataMasterRecordsInGameIndex(List<GameIndexEditorRow> gameRows, IEnumerable<ManualMetadataItem> pendingItems);
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

        /// <summary>Optional override for Steam store title by AppID; when null, <see cref="RunSteamRename"/> uses <see cref="ICoverService.SteamNameAsync"/>.</summary>
        public Func<string, string> ResolveSteamStoreTitle;

        /// <summary>Record Steam AppID on the saved game index when first seen during rename.</summary>
        public Action<string, string, string> EnsureSteamAppIdInGameIndex;

        /// <summary>Sanitize user-entered game title for use in filenames.</summary>
        public Func<string, string> SanitizeManualRenameGameTitle;

        /// <summary>Normalize title for “already game-prefixed?” checks during manual rename.</summary>
        public Func<string, string> NormalizeTitleForManualRename;

        public IFileSystemService FileSystem;

        /// <summary>Exif args and batched ExifTool writes for import metadata step.</summary>
        public IMetadataService MetadataService;

        /// <summary>Filesystem creation time (for preserve-timestamps restore metadata).</summary>
        public Func<string, DateTime> GetFileCreationTime;

        /// <summary>Filesystem last write time (for preserve-timestamps restore metadata).</summary>
        public Func<string, DateTime> GetFileLastWriteTime;

        /// <summary>Display label for photography tag in progress/log text (e.g. “Game Photography”).</summary>
        public string GamePhotographyTagLabel;

        /// <summary>Steam / store lookups (async store title for import-and-edit).</summary>
        public ICoverService CoverService;

        /// <summary>Same normalization as game-index manual metadata (single-argument form: title only).</summary>
        public Func<string, string> NormalizeGameIndexName;

        /// <summary>Platform label for a manual metadata row (checkboxes + tags).</summary>
        public Func<ManualMetadataItem, string> DetermineManualMetadataPlatformLabel;

        /// <summary>True when the row’s game id should not drive index assignment (library edit grouping).</summary>
        public Func<ManualMetadataItem, bool> ManualMetadataChangesGroupingIdentity;

        /// <summary>Resolve and persist game index rows for manual metadata finish.</summary>
        public IGameIndexEditorAssignmentService GameIndexEditorAssignment;

        /// <summary>“Game Name | Console” label for manual metadata game-title combo (same as library edit UI).</summary>
        public Func<string, string, string> BuildManualMetadataGameTitleChoiceLabel;
    }

    internal sealed class ImportService : IImportService
    {
        readonly ImportServiceDependencies d;
        readonly IFileSystemService fs;

        public ImportService(ImportServiceDependencies dependencies)
        {
            d = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
            fs = d.FileSystem ?? throw new ArgumentNullException(nameof(dependencies) + "." + nameof(dependencies.FileSystem));
            if (d.MetadataService == null) throw new ArgumentNullException(nameof(dependencies) + "." + nameof(dependencies.MetadataService));
            if (d.GetFileCreationTime == null) throw new ArgumentNullException(nameof(dependencies) + "." + nameof(dependencies.GetFileCreationTime));
            if (d.GetFileLastWriteTime == null) throw new ArgumentNullException(nameof(dependencies) + "." + nameof(dependencies.GetFileLastWriteTime));
            if (d.CoverService == null) throw new ArgumentNullException(nameof(dependencies) + "." + nameof(dependencies.CoverService));
            if (d.NormalizeGameIndexName == null) throw new ArgumentNullException(nameof(dependencies) + "." + nameof(dependencies.NormalizeGameIndexName));
        }

        string UndoPath => d.UndoManifestPath == null ? string.Empty : (d.UndoManifestPath() ?? string.Empty);

        public List<UndoImportEntry> LoadUndoManifest()
        {
            var path = UndoPath;
            var entries = new List<UndoImportEntry>();
            if (string.IsNullOrWhiteSpace(path)) return entries;
            foreach (var line in fs.ReadAllLines(path))
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
                if (fs.FileExists(path)) fs.DeleteFile(path);
                return;
            }

            fs.WriteAllLines(path, entries.Select(entry => string.Join("\t", new[]
            {
                entry.SourceDirectory ?? string.Empty,
                entry.ImportedFileName ?? string.Empty,
                entry.CurrentPath ?? string.Empty
            })).ToList());
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
            var fileList = (files ?? Enumerable.Empty<string>()).Where(fs.FileExists).ToList();
            var total = fileList.Count;
            if (progress != null) progress(0, total, "Starting move step for " + total + " file(s).");
            for (int i = 0; i < total; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var file = fileList[i];
                var remaining = total - (i + 1);
                var sourceDirectory = Path.GetDirectoryName(file);
                var target = Path.Combine(destinationRoot, Path.GetFileName(file));
                if (fs.FileExists(target))
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
                    if (string.Equals(mode, "Overwrite", StringComparison.OrdinalIgnoreCase)) fs.DeleteFile(target);
                }
                fs.MoveFile(file, target);
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
            var files = fs.EnumerateFiles(destinationRoot, "*", SearchOption.TopDirectoryOnly).Where(f => d.IsMedia != null && d.IsMedia(f)).ToList();
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
                if (!fs.DirectoryExists(targetDirectory))
                {
                    fs.CreateDirectory(targetDirectory);
                    created++;
                }

                var target = Path.Combine(targetDirectory, Path.GetFileName(file));
                if (fs.FileExists(target))
                {
                    target = d.UniquePath == null ? target : d.UniquePath(target);
                    renamedConflict++;
                }

                fs.MoveFile(file, target);
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
                if (string.IsNullOrWhiteSpace(currentPath) || !fs.FileExists(currentPath))
                {
                    result.Skipped++;
                    result.RemainingEntries.Add(entry);
                    d.Log?.Invoke("Undo skipped: could not find " + entry.ImportedFileName + " in the destination/library folders.");
                    continue;
                }

                fs.CreateDirectory(entry.SourceDirectory);
                var target = d.UniquePath == null
                    ? Path.Combine(entry.SourceDirectory, Path.GetFileName(currentPath))
                    : d.UniquePath(Path.Combine(entry.SourceDirectory, Path.GetFileName(currentPath)));
                fs.MoveFile(currentPath, target);
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
                if (!fs.FileExists(path))
                {
                    skipped++;
                    if (progress != null) progress(i + 1, total, "Skipped delete " + (i + 1) + " of " + total + " | " + remaining + " remaining | file missing");
                    continue;
                }
                fs.DeleteFile(path);
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
            if (parse == null)
            {
                d.Log?.Invoke("Steam rename skipped: ParseFilenameForImport not configured.");
                return new RenameStepResult { Renamed = 0, Skipped = 0, OldPathToNewPath = pathMap };
            }
            if (d.ResolveSteamStoreTitle == null && d.CoverService == null)
            {
                d.Log?.Invoke("Steam rename skipped: ResolveSteamStoreTitle and CoverService not available.");
                return new RenameStepResult { Renamed = 0, Skipped = 0, OldPathToNewPath = pathMap };
            }

            var libraryRoot = d.GetLibraryRoot == null ? string.Empty : d.GetLibraryRoot() ?? string.Empty;
            var files = (sourceFiles ?? Enumerable.Empty<string>()).Where(fs.FileExists).ToList();
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
                string game = null;
                if (d.ResolveSteamStoreTitle != null) game = d.ResolveSteamStoreTitle(appId);
                else game = d.CoverService.SteamNameAsync(appId, cancellationToken).ConfigureAwait(false).GetAwaiter().GetResult();
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
                fs.MoveFile(file, target);
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
                if (!fs.FileExists(item.FilePath))
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
                fs.MoveFile(item.FilePath, target);
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

        public MetadataStepResult WriteMetadataForReviewItems(IEnumerable<ReviewItem> reviewItems, Action<int, int, string> progress = null, CancellationToken cancellationToken = default)
        {
            var photoTag = string.IsNullOrWhiteSpace(d.GamePhotographyTagLabel) ? "Game Photography" : d.GamePhotographyTagLabel.Trim();
            int updated = 0, skipped = 0;
            var requests = new List<ExifWriteRequest>();
            var items = (reviewItems ?? Enumerable.Empty<ReviewItem>()).Where(i => i != null).ToList();
            var total = items.Count;
            if (progress != null) progress(0, total, "Starting metadata step for " + total + " file(s).");
            for (int i = 0; i < total; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = items[i];
                var remaining = total - (i + 1);
                if (item.DeleteBeforeProcessing)
                {
                    skipped++;
                    if (progress != null) progress(i + 1, total, "Skipped metadata " + (i + 1) + " of " + total + " | " + remaining + " remaining | file marked for delete");
                    continue;
                }
                var file = item.FilePath;
                if (!fs.FileExists(file))
                {
                    skipped++;
                    if (progress != null) progress(i + 1, total, "Skipped metadata " + (i + 1) + " of " + total + " | " + remaining + " remaining | file missing");
                    continue;
                }
                var selectedPlatformTags = new List<string>();
                if (item.TagSteam) selectedPlatformTags.Add("Steam");
                if (item.TagPs5)
                {
                    selectedPlatformTags.Add("PS5");
                    selectedPlatformTags.Add("PlayStation");
                }
                if (item.TagXbox) selectedPlatformTags.Add("Xbox");
                if (selectedPlatformTags.Count == 0 && item.PlatformTags != null) selectedPlatformTags.AddRange(item.PlatformTags);
                var platformTags = selectedPlatformTags.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                var metadataTarget = item.CaptureTime.ToString("yyyy-MM-dd HH:mm:ss") + (item.PreserveFileTimes ? " (preserving file timestamps)" : string.Empty);
                var notes = new List<string>();
                if (!string.IsNullOrWhiteSpace(item.Comment)) notes.Add("comment added");
                if (item.AddPhotographyTag) notes.Add(photoTag + " tag added");
                var noteSuffix = notes.Count > 0 ? " [" + string.Join(", ", notes.ToArray()) + "]" : string.Empty;
                d.Log?.Invoke("Updating metadata: " + item.FileName + " -> " + metadataTarget + (platformTags.Length > 0 ? " [" + string.Join(", ", platformTags) + "]" : " [no platform tag]") + noteSuffix);
                var originalCreate = DateTime.MinValue;
                var originalWrite = DateTime.MinValue;
                if (item.PreserveFileTimes)
                {
                    originalCreate = d.GetFileCreationTime(file);
                    originalWrite = d.GetFileLastWriteTime(file);
                }
                requests.Add(new ExifWriteRequest
                {
                    FilePath = file,
                    FileName = item.FileName,
                    Arguments = d.MetadataService.BuildExifArgs(file, item.CaptureTime, platformTags, item.PreserveFileTimes, item.Comment, item.AddPhotographyTag),
                    RestoreFileTimes = item.PreserveFileTimes,
                    OriginalCreateTime = originalCreate,
                    OriginalWriteTime = originalWrite,
                    SuccessDetail = item.FileName
                });
            }
            updated = d.MetadataService.RunExifWriteRequests(requests, requests.Count + skipped, skipped, progress, cancellationToken);
            if (progress != null) progress(total, total, "Metadata step complete: updated " + updated + ", skipped " + skipped + ".");
            d.Log?.Invoke("Metadata summary: updated " + updated + ", skipped " + skipped + ".");
            return new MetadataStepResult { Updated = updated, Skipped = skipped };
        }

        public async Task ApplyImportAndEditSteamStoreTitlesWhenGameNameUnchangedAsync(IEnumerable<ManualMetadataItem> items, CancellationToken cancellationToken = default)
        {
            var normalize = d.NormalizeGameIndexName;
            var cover = d.CoverService;
            foreach (var item in items ?? Enumerable.Empty<ManualMetadataItem>())
            {
                if (item == null) continue;
                if (!item.TagSteam) continue;
                var cur = normalize(item.GameName ?? string.Empty);
                var orig = normalize(item.OriginalGameName ?? string.Empty);
                if (!string.Equals(cur, orig, StringComparison.OrdinalIgnoreCase)) continue;
                var appId = Regex.Replace(item.SteamAppId ?? string.Empty, @"[^\d]", string.Empty);
                if (string.IsNullOrWhiteSpace(appId)) continue;
                cancellationToken.ThrowIfCancellationRequested();
                // This path is called from the live manual metadata window, so keep row mutation
                // on the captured context instead of continuing on a thread-pool thread.
                var storeTitle = await cover.SteamNameAsync(appId, cancellationToken);
                if (string.IsNullOrWhiteSpace(storeTitle)) continue;
                item.GameName = storeTitle;
            }
        }

        public void FinalizeManualMetadataItemsAgainstGameIndex(string libraryRoot, List<GameIndexEditorRow> gameRows, IEnumerable<ManualMetadataItem> pendingItems)
        {
            if (gameRows == null) throw new ArgumentNullException(nameof(gameRows));
            var assignment = d.GameIndexEditorAssignment;
            var platformLabelFn = d.DetermineManualMetadataPlatformLabel;
            var groupingIdentityFn = d.ManualMetadataChangesGroupingIdentity;
            var normalize = d.NormalizeGameIndexName;
            var nameFromFile = d.GetGameNameFromFileName;
            if (assignment == null || platformLabelFn == null || groupingIdentityFn == null || normalize == null || nameFromFile == null)
            {
                throw new InvalidOperationException("ImportServiceDependencies must set GameIndexEditorAssignment, DetermineManualMetadataPlatformLabel, ManualMetadataChangesGroupingIdentity, NormalizeGameIndexName, and GetGameNameFromFileName for finalize.");
            }

            foreach (var item in pendingItems ?? Enumerable.Empty<ManualMetadataItem>())
            {
                if (item == null || item.DeleteBeforeProcessing) continue;
                var resolvedName = normalize(
                    string.IsNullOrWhiteSpace(item.GameName)
                        ? nameFromFile(Path.GetFileNameWithoutExtension(item.FilePath))
                        : item.GameName);
                if (!string.IsNullOrWhiteSpace(resolvedName)) item.GameName = resolvedName;
                var preferredGameId = groupingIdentityFn(item) ? string.Empty : item.GameId;
                var resolvedRow = assignment.ResolveExistingGameIndexRowForAssignment(gameRows, item.GameName, platformLabelFn(item), preferredGameId);
                item.GameId = resolvedRow == null ? string.Empty : resolvedRow.GameId;
                if (resolvedRow != null)
                {
                    if (!string.IsNullOrWhiteSpace(resolvedRow.Name)) item.GameName = resolvedRow.Name;
                    if (!string.IsNullOrWhiteSpace(item.SteamAppId))
                    {
                        resolvedRow.SteamAppId = item.SteamAppId;
                        resolvedRow.SuppressSteamAppIdAutoResolve = false;
                    }
                }
            }

            assignment.SaveSavedGameIndexRows(libraryRoot, gameRows);
        }

        public List<string> BuildUnresolvedManualMetadataMasterRecordLabels(List<GameIndexEditorRow> gameRows, IEnumerable<ManualMetadataItem> pendingItems)
        {
            if (gameRows == null) throw new ArgumentNullException(nameof(gameRows));
            var assignment = d.GameIndexEditorAssignment;
            var normalize = d.NormalizeGameIndexName;
            var nameFromFile = d.GetGameNameFromFileName;
            var platformFn = d.DetermineManualMetadataPlatformLabel;
            var groupingFn = d.ManualMetadataChangesGroupingIdentity;
            var choiceLabel = d.BuildManualMetadataGameTitleChoiceLabel;
            if (assignment == null || normalize == null || nameFromFile == null || platformFn == null || groupingFn == null || choiceLabel == null)
            {
                throw new InvalidOperationException("ImportServiceDependencies must set GameIndexEditorAssignment, NormalizeGameIndexName, GetGameNameFromFileName, DetermineManualMetadataPlatformLabel, ManualMetadataChangesGroupingIdentity, and BuildManualMetadataGameTitleChoiceLabel.");
            }

            return (pendingItems ?? Enumerable.Empty<ManualMetadataItem>())
                .Where(item => item != null && !item.DeleteBeforeProcessing)
                .Select(item =>
                {
                    var normalizedName = normalize(
                        string.IsNullOrWhiteSpace(item.GameName)
                            ? nameFromFile(Path.GetFileNameWithoutExtension(item.FilePath))
                            : item.GameName);
                    return new
                    {
                        Name = normalizedName,
                        PlatformLabel = platformFn(item),
                        PreferredGameId = groupingFn(item) ? string.Empty : item.GameId
                    };
                })
                .Where(e => !string.IsNullOrWhiteSpace(e.Name))
                .Where(e => assignment.ManualMetadataMasterRecordNeedsNewPlaceholder(gameRows, e.Name, e.PlatformLabel, e.PreferredGameId))
                .Select(e => choiceLabel(e.Name, e.PlatformLabel))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public void EnsureNewManualMetadataMasterRecordsInGameIndex(List<GameIndexEditorRow> gameRows, IEnumerable<ManualMetadataItem> pendingItems)
        {
            if (gameRows == null) throw new ArgumentNullException(nameof(gameRows));
            var assignment = d.GameIndexEditorAssignment;
            var normalize = d.NormalizeGameIndexName;
            var nameFromFile = d.GetGameNameFromFileName;
            var platformFn = d.DetermineManualMetadataPlatformLabel;
            var groupingFn = d.ManualMetadataChangesGroupingIdentity;
            if (assignment == null || normalize == null || nameFromFile == null || platformFn == null || groupingFn == null)
            {
                throw new InvalidOperationException("ImportServiceDependencies must set GameIndexEditorAssignment, NormalizeGameIndexName, GetGameNameFromFileName, DetermineManualMetadataPlatformLabel, and ManualMetadataChangesGroupingIdentity.");
            }

            foreach (var item in pendingItems ?? Enumerable.Empty<ManualMetadataItem>())
            {
                if (item == null || item.DeleteBeforeProcessing) continue;
                var resolvedName = normalize(
                    string.IsNullOrWhiteSpace(item.GameName)
                        ? nameFromFile(Path.GetFileNameWithoutExtension(item.FilePath))
                        : item.GameName);
                var resolvedPlatform = platformFn(item);
                var preferredGameId = groupingFn(item) ? string.Empty : item.GameId;
                if (!assignment.ManualMetadataMasterRecordNeedsNewPlaceholder(gameRows, resolvedName, resolvedPlatform, preferredGameId)) continue;
                assignment.EnsureManualMetadataMasterRow(gameRows, resolvedName, resolvedPlatform, preferredGameId);
            }
        }

        string ResolveUndoCurrentPath(UndoImportEntry entry, HashSet<string> usedPaths, string destinationRoot, string libraryRoot)
        {
            if (entry == null) return null;
            if (!string.IsNullOrWhiteSpace(entry.CurrentPath) && fs.FileExists(entry.CurrentPath))
            {
                var fullCurrent = Path.GetFullPath(entry.CurrentPath);
                if (usedPaths.Add(fullCurrent)) return fullCurrent;
            }

            foreach (var root in new[] { destinationRoot, libraryRoot }.Where(r => !string.IsNullOrWhiteSpace(r) && fs.DirectoryExists(r)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                foreach (var candidate in fs.EnumerateFiles(root, entry.ImportedFileName, SearchOption.AllDirectories)
                    .OrderByDescending(path => fs.GetLastWriteTime(path)))
                {
                    var fullCandidate = Path.GetFullPath(candidate);
                    if (usedPaths.Add(fullCandidate)) return fullCandidate;
                }
            }
            return null;
        }
    }
}
