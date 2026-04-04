using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

        void ApplyEmbeddedXmpStarRating(string filePath, bool starred)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;
            EnsureExifTool();
            var args = metadataService.BuildStarRatingExifArgs(filePath, starred);
            if (args == null || args.Length == 0) return;
            RunExifToolBatch(new List<ExifWriteRequest>
            {
                new ExifWriteRequest
                {
                    FilePath = filePath,
                    FileName = Path.GetFileName(filePath),
                    Arguments = args,
                    RestoreFileTimes = false,
                    OriginalCreateTime = DateTime.MinValue,
                    OriginalWriteTime = DateTime.MinValue,
                    SuccessDetail = "XMP star rating"
                }
            });
        }

        /// <summary>Runs Exif write + index save on a worker thread; invokes <paramref name="uiAfter"/> on the UI dispatcher when finished (success or failure).</summary>
        void ToggleLibraryFileStarredByPath(string filePath, Action uiAfter = null)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;
            var root = libraryWorkspace.LibraryRoot;
            if (string.IsNullOrWhiteSpace(root)) return;
            var dispatcher = Dispatcher;
            Task.Run(delegate
            {
                Exception caught = null;
                try
                {
                    var index = LoadLibraryMetadataIndex(root, true);
                    LibraryMetadataIndexEntry row;
                    if (!index.TryGetValue(filePath, out row) || row == null) return;
                    var nextStarred = !row.Starred;
                    ApplyEmbeddedXmpStarRating(filePath, nextStarred);
                    row.Starred = nextStarred;
                    row.Stamp = BuildLibraryMetadataStamp(filePath);
                    SaveLibraryMetadataIndex(root, index);
                }
                catch (Exception ex)
                {
                    caught = ex;
                }
                dispatcher.BeginInvoke(new Action(delegate
                {
                    if (caught != null)
                    {
                        LogException("ToggleLibraryFileStarredByPath", caught);
                        MessageBox.Show(caught.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                    uiAfter?.Invoke();
                }));
            });
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
