using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        /// <summary>
        /// When the capture path lies under the library root, returns relative path (subfolders + file name) for mirroring under the export folder; otherwise file name only.
        /// </summary>
        static string BuildStarredExportRelativePath(string libraryRoot, string sourceFilePath)
        {
            try
            {
                var rootNorm = Path.GetFullPath(libraryRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                var srcNorm = Path.GetFullPath(sourceFilePath);
                var rel = Path.GetRelativePath(rootNorm, srcNorm);
                if (string.IsNullOrWhiteSpace(rel)) return Path.GetFileName(sourceFilePath) ?? string.Empty;
                var parts = rel.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0 && string.Equals(parts[0], "..", StringComparison.Ordinal))
                    return Path.GetFileName(sourceFilePath) ?? string.Empty;
                return rel;
            }
            catch
            {
                return Path.GetFileName(sourceFilePath) ?? string.Empty;
            }
        }

        /// <summary>Stable hash of on-disk metadata stamp plus photo-index fields; when it changes, export copies again.</summary>
        string ComputeStarredExportFingerprint(string sourceFile, LibraryMetadataIndexEntry entry)
        {
            long fileStamp = 0;
            if (!string.IsNullOrWhiteSpace(sourceFile) && File.Exists(sourceFile))
                fileStamp = MetadataCacheStamp(sourceFile);
            var st = entry?.Stamp ?? string.Empty;
            var gid = NormalizeGameId(entry?.GameId ?? string.Empty);
            var con = entry?.ConsoleLabel ?? string.Empty;
            var tag = entry?.TagText ?? string.Empty;
            var cap = entry != null ? entry.CaptureUtcTicks : 0L;
            var payload = string.Join("\u241e", new[]
            {
                fileStamp.ToString(CultureInfo.InvariantCulture),
                st,
                gid,
                con,
                tag,
                cap.ToString(CultureInfo.InvariantCulture)
            });
            using (var sha = SHA256.Create())
            {
                return Convert.ToHexString(sha.ComputeHash(Encoding.UTF8.GetBytes(payload)));
            }
        }

        static string NormalizePathForStarredExportTracking(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            try
            {
                return Path.GetFullPath(path);
            }
            catch
            {
                return path.Trim();
            }
        }

        static void ClearReadOnlyForOverwrite(string destPath)
        {
            if (string.IsNullOrWhiteSpace(destPath) || !File.Exists(destPath)) return;
            try
            {
                var attrs = File.GetAttributes(destPath);
                if ((attrs & FileAttributes.ReadOnly) != 0)
                    File.SetAttributes(destPath, attrs & ~FileAttributes.ReadOnly);
            }
            catch
            {
            }
        }

        internal void ExportStarredLibraryCapturesToFolder(Window owner)
        {
            var dest = (starredExportFolder ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(dest))
            {
                MessageBox.Show(
                    owner,
                    "Set a Starred export folder in Path Settings, then try again.",
                    "Export Starred",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }
            var root = libraryWorkspace != null && !string.IsNullOrWhiteSpace(libraryWorkspace.LibraryRoot)
                ? libraryWorkspace.LibraryRoot
                : libraryRoot;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                MessageBox.Show(
                    owner,
                    "Library folder is not set or was not found.",
                    "Export Starred",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            var paths = new List<string>();
            Dictionary<string, LibraryMetadataIndexEntry> metadataIndex = null;
            try
            {
                metadataIndex = LoadLibraryMetadataIndex(root, false);
                foreach (var kv in metadataIndex)
                {
                    if (kv.Value == null || !kv.Value.Starred) continue;
                    var path = kv.Key;
                    if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) continue;
                    paths.Add(path);
                }
            }
            catch (Exception ex)
            {
                LogException("Export Starred (enumerate)", ex);
                MessageBox.Show(owner, ex.Message, "Export Starred", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (paths.Count == 0)
            {
                MessageBox.Show(
                    owner,
                    "No starred files found in the photo index for this library.",
                    "Export Starred",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            try
            {
                Directory.CreateDirectory(dest);
            }
            catch (Exception ex)
            {
                LogException("Export Starred (create folder)", ex);
                MessageBox.Show(owner, "Could not create or access the export folder." + Environment.NewLine + Environment.NewLine + ex.Message, "Export Starred", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dispatcher = Dispatcher;
            var destNorm = Path.GetFullPath(dest);
            var rootNorm = Path.GetFullPath(root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            var pathsOrdered = paths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
            var entryByPath = new Dictionary<string, LibraryMetadataIndexEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in pathsOrdered)
            {
                LibraryMetadataIndexEntry e;
                entryByPath[p] = metadataIndex != null && metadataIndex.TryGetValue(p, out e) ? e : null;
            }
            var rootForDb = root;
            _ = Task.Run(delegate
            {
                var tracking = indexPersistenceService.LoadStarredExportFingerprints(rootForDb, destNorm);
                var copied = 0;
                var skipped = 0;
                var failed = 0;
                string lastError = null;
                foreach (var src in pathsOrdered)
                {
                    try
                    {
                        var rel = BuildStarredExportRelativePath(rootNorm, src);
                        if (string.IsNullOrWhiteSpace(rel)) continue;
                        var dst = Path.Combine(destNorm, rel);
                        if (string.Equals(Path.GetFullPath(src), Path.GetFullPath(dst), StringComparison.OrdinalIgnoreCase))
                        {
                            skipped++;
                            continue;
                        }
                        var srcNorm = NormalizePathForStarredExportTracking(src);
                        LibraryMetadataIndexEntry entry;
                        entryByPath.TryGetValue(src, out entry);
                        var fingerprint = ComputeStarredExportFingerprint(src, entry);
                        if (tracking.TryGetValue(srcNorm, out var prevPrint)
                            && string.Equals(prevPrint, fingerprint, StringComparison.Ordinal)
                            && File.Exists(dst))
                        {
                            skipped++;
                            continue;
                        }
                        var dstDir = Path.GetDirectoryName(dst);
                        if (!string.IsNullOrEmpty(dstDir))
                            Directory.CreateDirectory(dstDir);
                        ClearReadOnlyForOverwrite(dst);
                        fileSystemService.CopyFile(src, dst, true);
                        indexPersistenceService.UpsertStarredExportFingerprint(rootForDb, destNorm, srcNorm, fingerprint);
                        tracking[srcNorm] = fingerprint;
                        copied++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        lastError = ex.Message;
                    }
                }
                var activeNorm = pathsOrdered.Select(NormalizePathForStarredExportTracking).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
                indexPersistenceService.PruneStarredExportFingerprints(rootForDb, destNorm, activeNorm);
                dispatcher.BeginInvoke(new Action(delegate
                {
                    var summary = "Export Starred: " + copied + " copied, " + skipped + " up to date" + (failed > 0 ? ", " + failed + " failed" : string.Empty) + " → " + destNorm;
                    Log(summary);
                    if (status != null) status.Text = failed == 0
                        ? "Export Starred: " + copied + " updated, " + skipped + " skipped"
                        : "Export Starred: " + failed + " failed";
                    if (failed == 0)
                    {
                        MessageBox.Show(
                            owner,
                            copied + " file" + (copied == 1 ? string.Empty : "s") + " copied (new or changed)." + Environment.NewLine
                            + skipped + " already up to date." + Environment.NewLine + Environment.NewLine + destNorm,
                            "Export Starred",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show(
                            owner,
                            copied + " copied, " + skipped + " skipped, " + failed + " failed."
                            + (string.IsNullOrWhiteSpace(lastError) ? string.Empty : Environment.NewLine + Environment.NewLine + lastError),
                            "Export Starred",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                }));
            });
        }
    }
}
