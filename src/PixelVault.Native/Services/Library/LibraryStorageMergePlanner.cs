using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PixelVaultNative
{
    /// <summary>
    /// Dry-run planning for merging per-platform folders into shared storage targets (PV-PLN-LIBST-001 Step 5).
    /// </summary>
    internal static class LibraryStorageMergePlanner
    {
        static string NormDir(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        static bool SameDirectory(string a, string b) =>
            string.Equals(NormDir(a), NormDir(b), StringComparison.OrdinalIgnoreCase);

        static bool PathBeginsWithLibraryRoot(string fullPath, string libraryRoot)
        {
            if (string.IsNullOrWhiteSpace(fullPath) || string.IsNullOrWhiteSpace(libraryRoot)) return false;
            try
            {
                var root = NormDir(Path.GetFullPath(libraryRoot));
                var path = NormDir(Path.GetFullPath(fullPath));
                return !(string.IsNullOrWhiteSpace(root) || string.IsNullOrWhiteSpace(path))
                    && path.Length >= root.Length
                    && path.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                    && (path.Length == root.Length || path[root.Length] == Path.DirectorySeparatorChar || path[root.Length] == Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Build a dry-run from the current working set of game index rows (file paths and <see cref="GameIndexEditorRow.StorageGroupId"/> must be current).
        /// </summary>
        internal static LibraryStorageMergeDryRunResult PlanDryRun(
            string libraryRoot,
            IReadOnlyList<GameIndexEditorRow> allRows,
            Func<string, string, string> normalizeGameIndexNameWithFolder,
            Func<string, string> getSafeGameFolderName,
            Func<string, string> normalizeConsoleLabel,
            IReadOnlyDictionary<string, int> titleCounts,
            Func<string, bool> fileExists,
            Func<string, bool> isLibraryMediaFile,
            Func<string, IEnumerable<string>> enumerateDirectoryFilesTopLevel)
        {
            var result = new LibraryStorageMergeDryRunResult();
            if (string.IsNullOrWhiteSpace(libraryRoot) || allRows == null || allRows.Count == 0) return result;
            if (normalizeGameIndexNameWithFolder == null || getSafeGameFolderName == null || normalizeConsoleLabel == null)
                return result;
            fileExists ??= File.Exists;
            isLibraryMediaFile ??= (p => !string.IsNullOrWhiteSpace(p) && fileExists(p));
            enumerateDirectoryFilesTopLevel ??= (dir =>
            {
                if (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir)) return Array.Empty<string>();
                try { return Directory.EnumerateFiles(dir); }
                catch { return Array.Empty<string>(); }
            });

            var rows = allRows.Where(r => r != null && !string.IsNullOrWhiteSpace(NormalizeGameIdStatic(r.GameId))).ToList();
            foreach (var sg in rows
                         .Where(r => !string.IsNullOrWhiteSpace((r.StorageGroupId ?? string.Empty).Trim()))
                         .GroupBy(r => (r.StorageGroupId ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase))
            {
                var members = sg.OrderBy(r => (r.GameId ?? string.Empty).Trim(), StringComparer.OrdinalIgnoreCase).ToList();
                if (members.Count == 0) continue;

                var rep = LibraryPlacementService.FindStorageGroupRepresentativeRow(members[0], members, normalizeGameIndexNameWithFolder) ?? members[0];
                var targetDir = LibraryPlacementService.BuildCanonicalStorageFolderPath(
                    libraryRoot,
                    rep,
                    rows,
                    normalizeGameIndexNameWithFolder,
                    getSafeGameFolderName,
                    normalizeConsoleLabel,
                    titleCounts);
                if (string.IsNullOrWhiteSpace(targetDir) || !PathBeginsWithLibraryRoot(targetDir, libraryRoot))
                {
                    result.Warnings.Add("Skipping storage group " + sg.Key + ": could not resolve a target folder under the library root.");
                    continue;
                }

                var fileMoves = new List<LibraryStorageMergeFileMovePreview>();
                foreach (var row in members)
                {
                    foreach (var file in row.FilePaths ?? Array.Empty<string>())
                    {
                        if (string.IsNullOrWhiteSpace(file) || !fileExists(file)) continue;
                        if (!PathBeginsWithLibraryRoot(file, libraryRoot)) continue;
                        var curDir = Path.GetDirectoryName(file) ?? string.Empty;
                        if (SameDirectory(curDir, targetDir)) continue;
                        var targetFile = Path.Combine(targetDir, Path.GetFileName(file));
                        var conflict = fileExists(targetFile)
                            && !string.Equals(Path.GetFullPath(file), Path.GetFullPath(targetFile), StringComparison.OrdinalIgnoreCase);
                        fileMoves.Add(new LibraryStorageMergeFileMovePreview
                        {
                            SourcePath = file,
                            TargetPath = targetFile,
                            WouldRenameForConflict = conflict
                        });
                        if (conflict) result.TotalConflictRenames++;
                        result.TotalFileMoves++;
                    }
                }

                if (fileMoves.Count == 0) continue;

                var preview = new LibraryStorageMergeGroupPreview
                {
                    StorageGroupId = sg.Key,
                    TargetDirectory = targetDir,
                    MemberRows = members,
                    FileMoves = fileMoves
                };

                var sourceDirs = fileMoves
                    .Select(m => Path.GetDirectoryName(m.SourcePath) ?? string.Empty)
                    .Where(d => !string.IsNullOrWhiteSpace(d))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                foreach (var dir in sourceDirs)
                {
                    if (SameDirectory(dir, targetDir)) continue;
                    try
                    {
                        var mediaInDir = enumerateDirectoryFilesTopLevel(dir).Where(isLibraryMediaFile).ToList();
                        if (mediaInDir.Count == 0) continue;
                        var moving = new HashSet<string>(fileMoves.Select(m => m.SourcePath), StringComparer.OrdinalIgnoreCase);
                        if (mediaInDir.All(moving.Contains))
                            preview.DirectoriesThatMayBeRemovedIfEmpty.Add(dir);
                    }
                    catch
                    {
                        // ignore per-dir enumeration errors in preview
                    }
                }

                result.Groups.Add(preview);
            }

            return result;
        }

        static string NormalizeGameIdStatic(string gameId) => (gameId ?? string.Empty).Trim();
    }
}
