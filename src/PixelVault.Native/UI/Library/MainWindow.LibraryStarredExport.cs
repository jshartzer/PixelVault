using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            try
            {
                var index = LoadLibraryMetadataIndex(root, false);
                foreach (var kv in index)
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
            _ = Task.Run(delegate
            {
                var ok = 0;
                var failed = 0;
                string lastError = null;
                foreach (var src in paths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        var rel = BuildStarredExportRelativePath(rootNorm, src);
                        if (string.IsNullOrWhiteSpace(rel)) continue;
                        var dst = Path.Combine(destNorm, rel);
                        if (string.Equals(Path.GetFullPath(src), Path.GetFullPath(dst), StringComparison.OrdinalIgnoreCase))
                        {
                            ok++;
                            continue;
                        }
                        var dstDir = Path.GetDirectoryName(dst);
                        if (!string.IsNullOrEmpty(dstDir))
                            Directory.CreateDirectory(dstDir);
                        ClearReadOnlyForOverwrite(dst);
                        fileSystemService.CopyFile(src, dst, true);
                        ok++;
                    }
                    catch (Exception ex)
                    {
                        failed++;
                        lastError = ex.Message;
                    }
                }
                dispatcher.BeginInvoke(new Action(delegate
                {
                    Log("Export Starred: " + ok + " copied" + (failed > 0 ? "; " + failed + " failed" : string.Empty) + " → " + destNorm);
                    if (status != null) status.Text = "Export Starred: " + ok + " file" + (ok == 1 ? string.Empty : "s");
                    if (failed == 0)
                    {
                        MessageBox.Show(
                            owner,
                            ok + " file" + (ok == 1 ? string.Empty : "s") + " copied to:" + Environment.NewLine + destNorm,
                            "Export Starred",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show(
                            owner,
                            ok + " copied, " + failed + " failed."
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
