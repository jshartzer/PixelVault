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
            _ = Task.Run(delegate
            {
                var ok = 0;
                var failed = 0;
                string lastError = null;
                foreach (var src in paths.OrderBy(p => p, StringComparer.OrdinalIgnoreCase))
                {
                    try
                    {
                        var name = Path.GetFileName(src);
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        var dst = Path.Combine(destNorm, name);
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
