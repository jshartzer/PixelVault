using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        bool TryGetLibraryFileStarredFromIndex(string filePath, out bool starred)
        {
            starred = false;
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return false;
            var root = libraryRoot;
            if (string.IsNullOrWhiteSpace(root)) return false;
            var index = LoadLibraryMetadataIndex(root, false);
            LibraryMetadataIndexEntry row;
            if (!index.TryGetValue(filePath, out row) || row == null) return false;
            starred = row.Starred;
            return true;
        }

        void ToggleLibraryFileStarredByPath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;
            try
            {
                var root = libraryWorkspace.LibraryRoot;
                if (string.IsNullOrWhiteSpace(root)) return;
                var index = LoadLibraryMetadataIndex(root, true);
                LibraryMetadataIndexEntry row;
                if (!index.TryGetValue(filePath, out row) || row == null) return;
                row.Starred = !row.Starred;
                SaveLibraryMetadataIndex(root, index);
            }
            catch (Exception ex)
            {
                LogException("ToggleLibraryFileStarredByPath", ex);
                MessageBox.Show(ex.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        bool LibraryFileIndexHasGamePhotographyTag(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return false;
            var root = libraryRoot;
            if (string.IsNullOrWhiteSpace(root)) return false;
            var index = LoadLibraryMetadataIndex(root, false);
            LibraryMetadataIndexEntry row;
            if (!index.TryGetValue(filePath, out row) || row == null) return false;
            var tagText = row.TagText ?? string.Empty;
            var candidates = new List<string> { GamePhotographyTag, "Photography" };
            return MetadataIndexTagTextMatchesCandidates(tagText, candidates);
        }

        void ToggleLibraryFileGamePhotographyTagByPath(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;
            try
            {
                EnsureExifTool();
                var item = BuildLibraryMetadataItemForPath(filePath, null);
                if (item == null) return;
                item.AddPhotographyTag = !item.AddPhotographyTag;
                item.ForceTagMetadataWrite = true;
                RunManualMetadata(new List<ManualMetadataItem> { item }, null, CancellationToken.None);
                if (librarySession != null && string.Equals(libraryRoot, librarySession.LibraryRoot, StringComparison.OrdinalIgnoreCase))
                    librarySession.UpsertLibraryMetadataIndexEntries(new[] { item });
                else
                    libraryScanner.UpsertLibraryMetadataIndexEntries(new[] { item }, libraryRoot);
                RemoveCachedFileTagEntries(new[] { filePath });
                if (status != null)
                    status.Text = item.AddPhotographyTag ? "Game Photography tag added" : "Game Photography tag removed";
            }
            catch (Exception ex)
            {
                LogException("ToggleLibraryFileGamePhotographyTagByPath", ex);
                MessageBox.Show(ex.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        void OpenStandaloneLibraryMetadataEditor(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;
            EnsureExifTool();
            var item = BuildLibraryMetadataItemForPath(filePath, null);
            if (item == null)
            {
                MessageBox.Show("That file could not be loaded for metadata editing.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            var title = Path.GetFileName(filePath);
            if (!ShowManualMetadataWindow(new List<ManualMetadataItem> { item }, true, title)) return;
            RunLibraryMetadataWorkflowWithProgress(null, new List<ManualMetadataItem> { item }, null);
        }
    }
}
