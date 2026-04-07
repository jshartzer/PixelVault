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
            return TryGetLibraryMetadataStarredFromCachedIndex(root, filePath, out starred);
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

        /// <summary>Host worker for <see cref="ILibrarySession.RequestToggleCaptureStarred"/> — Exif + index on a worker thread; <paramref name="uiAfter"/> receives whether the star was toggled in the index.</summary>
        void ToggleLibraryFileStarredByPath(string filePath, Action<bool> uiAfter = null)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                uiAfter?.Invoke(false);
                return;
            }

            var root = libraryWorkspace.LibraryRoot;
            if (string.IsNullOrWhiteSpace(root))
            {
                uiAfter?.Invoke(false);
                return;
            }

            var dispatcher = Dispatcher;
            Task.Run(delegate
            {
                Exception caught = null;
                var applied = false;
                try
                {
                    var index = LoadLibraryMetadataIndexViaSessionWhenActive(root, true);
                    LibraryMetadataIndexEntry row;
                    if (!index.TryGetValue(filePath, out row) || row == null)
                    {
                        dispatcher.BeginInvoke(new Action(delegate { uiAfter?.Invoke(false); }));
                        return;
                    }

                    var nextStarred = !row.Starred;
                    ApplyEmbeddedXmpStarRating(filePath, nextStarred);
                    row.Starred = nextStarred;
                    row.Stamp = BuildLibraryMetadataStamp(filePath);
                    SaveLibraryMetadataIndexViaSessionWhenActive(root, index);
                    applied = true;
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
                        TryLibraryToast("Could not update star: " + caught.Message, MessageBoxImage.Warning);
                        uiAfter?.Invoke(false);
                        return;
                    }

                    uiAfter?.Invoke(applied);
                }));
            });
        }

        bool LibraryFileIndexHasGamePhotographyTag(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return false;
            var root = libraryRoot;
            if (string.IsNullOrWhiteSpace(root)) return false;
            var index = LoadLibraryMetadataIndexViaSessionWhenActive(root, false);
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
                TryLibraryToast(ex.Message, MessageBoxImage.Warning);
            }
        }

        /// <summary>Host worker for <see cref="ILibrarySession.RequestSaveCaptureComment"/> — embedded comment via manual-metadata path + index stamp refresh.</summary>
        void SaveLibraryFileCommentByPath(string filePath, string comment, Action<bool> uiAfter = null)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            {
                uiAfter?.Invoke(false);
                return;
            }
            var root = libraryWorkspace.LibraryRoot;
            var dispatcher = Dispatcher;
            Task.Run(delegate
            {
                Exception caught = null;
                var saved = false;
                try
                {
                    EnsureExifTool();
                    var item = BuildLibraryMetadataItemForPath(filePath, null);
                    if (item != null)
                    {
                        var cleanedComment = CleanComment(comment);
                        if (!SameManualText(cleanedComment, item.OriginalComment))
                        {
                            item.Comment = cleanedComment;
                            RunManualMetadata(new List<ManualMetadataItem> { item }, null, CancellationToken.None);
                            if (!string.IsNullOrWhiteSpace(root))
                            {
                                var index = LoadLibraryMetadataIndexViaSessionWhenActive(root, true);
                                LibraryMetadataIndexEntry row;
                                if (index.TryGetValue(filePath, out row) && row != null)
                                {
                                    row.Stamp = BuildLibraryMetadataStamp(filePath);
                                    SaveLibraryMetadataIndexViaSessionWhenActive(root, index);
                                }
                            }
                            saved = true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    caught = ex;
                }
                dispatcher.BeginInvoke(new Action(delegate
                {
                    if (caught != null)
                    {
                        LogException("SaveLibraryFileCommentByPath", caught);
                        TryLibraryToast("Could not save comment: " + caught.Message, MessageBoxImage.Warning);
                        uiAfter?.Invoke(false);
                        return;
                    }
                    if (status != null)
                    {
                        if (saved) status.Text = string.IsNullOrWhiteSpace(CleanComment(comment)) ? "Comment cleared" : "Comment saved";
                    }
                    uiAfter?.Invoke(true);
                }));
            });
        }

        void OpenStandaloneLibraryMetadataEditor(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath)) return;
            EnsureExifTool();
            var item = BuildLibraryMetadataItemForPath(filePath, null);
            if (item == null)
            {
                TryLibraryToast("That file could not be loaded for metadata editing.");
                return;
            }
            var title = Path.GetFileName(filePath);
            if (!ShowManualMetadataWindow(new List<ManualMetadataItem> { item }, true, title)) return;
            RunLibraryMetadataWorkflowWithProgress(null, new List<ManualMetadataItem> { item }, null);
        }
    }
}
