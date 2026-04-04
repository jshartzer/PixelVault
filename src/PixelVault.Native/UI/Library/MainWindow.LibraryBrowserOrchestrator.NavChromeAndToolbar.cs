using System;
using System.Windows;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        /// <summary>Wires Library browser nav chrome and pane toolbar clicks (invoked from <see cref="LibraryBrowserHost.Show"/> after delegates exist).</summary>
        void LibraryBrowserWireNavChromeAndToolbar(
            Window libraryWindow,
            LibraryBrowserWorkingSet ws,
            LibraryBrowserPaneRefs panes,
            LibraryBrowserNavChrome navChrome,
            Action refreshIntakeReviewBadge,
            Action<bool> refreshLibraryFoldersAsync,
            Action runCoverRefresh,
            Action openSelectedLibraryMetadataEditor,
            Action deleteSelectedLibraryFiles,
            Action<string> setLibraryGroupingMode,
            Action<string> setLibrarySortMode)
        {
            navChrome.RefreshButton.Click += delegate
            {
                if (refreshLibraryFoldersAsync != null) refreshLibraryFoldersAsync(false);
            };
            navChrome.SettingsButton.Click += delegate { ShowSettingsWindow(); if (refreshIntakeReviewBadge != null) refreshIntakeReviewBadge(); };
            navChrome.GameIndexButton.Click += delegate { OpenGameIndexEditor(); };
            navChrome.PhotoIndexButton.Click += delegate { OpenPhotoIndexEditor(); };
            navChrome.PhotographyGalleryButton.Click += delegate { ShowPhotographyGallery(libraryWindow); };
            navChrome.FilenameRulesButton.Click += delegate { OpenFilenameConventionEditor(); };
            navChrome.MyCoversButton.Click += delegate { OpenSavedCoversFolder(); };
            navChrome.ImportButton.Click += delegate { RunWorkflow(false); if (refreshIntakeReviewBadge != null) refreshIntakeReviewBadge(); };
            navChrome.ImportCommentsButton.Click += delegate { RunWorkflow(true); if (refreshIntakeReviewBadge != null) refreshIntakeReviewBadge(); };
            navChrome.ManualImportButton.Click += delegate { OpenManualIntakeWindow(); if (refreshIntakeReviewBadge != null) refreshIntakeReviewBadge(); };
            navChrome.FetchButton.Click += delegate
            {
                var choice = MessageBox.Show(
                    "Refresh cover art for the entire library?",
                    "PixelVault",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Question);
                if (choice != MessageBoxResult.OK) return;
                runCoverRefresh();
            };
            navChrome.IntakeReviewButton.Click += delegate
            {
                ShowIntakePreviewWindow(false);
                if (refreshIntakeReviewBadge != null) refreshIntakeReviewBadge();
            };
            panes.OpenFolderButton.Click += delegate
            {
                foreach (var folderPath in GetLibraryBrowserSourceFolderPaths(ws.Current)) OpenFolder(folderPath);
            };
            panes.EditMetadataButton.Click += delegate { openSelectedLibraryMetadataEditor(); };
            panes.DeleteSelectedButton.Click += delegate { deleteSelectedLibraryFiles(); };
            panes.GroupAllButton.Click += delegate { setLibraryGroupingMode("all"); };
            panes.GroupConsoleButton.Click += delegate { setLibraryGroupingMode("console"); };
            panes.SortPlatformButton.Click += delegate { setLibrarySortMode("platform"); };
            panes.SortRecentButton.Click += delegate { setLibrarySortMode("recent"); };
            panes.SortPhotosButton.Click += delegate { setLibrarySortMode("photos"); };
        }
    }
}
