using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

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
            Action<string> setLibrarySortMode,
            Action<string> setLibraryFilterMode)
        {
            void OpenLibraryButtonMenu(Button anchor, params MenuItem[] items)
            {
                if (anchor == null || items == null || items.Length == 0) return;
                var menu = new ContextMenu
                {
                    PlacementTarget = anchor,
                    Placement = PlacementMode.Bottom,
                    StaysOpen = false
                };
                foreach (var item in items)
                {
                    if (item != null) menu.Items.Add(item);
                }
                anchor.ContextMenu = menu;
                menu.IsOpen = true;
            }

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
                    libraryWindow,
                    "Refresh cover art for the entire library?",
                    "PixelVault",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Question);
                if (choice != MessageBoxResult.OK) return;
                runCoverRefresh();
            };
            navChrome.ExportStarredButton.Click += delegate { ExportStarredLibraryCapturesToFolder(libraryWindow); };
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
            panes.GroupTimelineButton.Click += delegate { setLibraryGroupingMode("timeline"); };
            panes.ExitTimelineButton.Click += delegate { setLibraryGroupingMode("folders"); };
            panes.SortMenuButton.Click += delegate
            {
                var alphaItem = new MenuItem
                {
                    Header = "Alphabetical",
                    IsCheckable = true,
                    IsChecked = string.Equals(NormalizeLibraryFolderSortMode(libraryFolderSortMode), "alpha", StringComparison.OrdinalIgnoreCase)
                };
                alphaItem.Click += delegate { setLibrarySortMode("alpha"); };

                var capturedItem = new MenuItem
                {
                    Header = "Date Captured",
                    IsCheckable = true,
                    IsChecked = string.Equals(NormalizeLibraryFolderSortMode(libraryFolderSortMode), "captured", StringComparison.OrdinalIgnoreCase)
                };
                capturedItem.Click += delegate { setLibrarySortMode("captured"); };

                var addedItem = new MenuItem
                {
                    Header = "Date Added",
                    IsCheckable = true,
                    IsChecked = string.Equals(NormalizeLibraryFolderSortMode(libraryFolderSortMode), "added", StringComparison.OrdinalIgnoreCase)
                };
                addedItem.Click += delegate { setLibrarySortMode("added"); };

                var photosItem = new MenuItem
                {
                    Header = "Most Photos",
                    IsCheckable = true,
                    IsChecked = string.Equals(NormalizeLibraryFolderSortMode(libraryFolderSortMode), "photos", StringComparison.OrdinalIgnoreCase)
                };
                photosItem.Click += delegate { setLibrarySortMode("photos"); };

                OpenLibraryButtonMenu(panes.SortMenuButton, alphaItem, capturedItem, addedItem, photosItem);
            };
            panes.FilterMenuButton.Click += delegate
            {
                var allItem = new MenuItem
                {
                    Header = "All Games",
                    IsCheckable = true,
                    IsChecked = string.Equals(NormalizeLibraryFolderFilterMode(libraryFolderFilterMode), "all", StringComparison.OrdinalIgnoreCase)
                };
                allItem.Click += delegate { setLibraryFilterMode("all"); };

                var completedItem = new MenuItem
                {
                    Header = "100% Achievements",
                    IsCheckable = true,
                    IsChecked = string.Equals(NormalizeLibraryFolderFilterMode(libraryFolderFilterMode), "completed", StringComparison.OrdinalIgnoreCase)
                };
                completedItem.Click += delegate { setLibraryFilterMode("completed"); };

                var crossPlatformItem = new MenuItem
                {
                    Header = "Cross-Platform",
                    IsCheckable = true,
                    IsChecked = string.Equals(NormalizeLibraryFolderFilterMode(libraryFolderFilterMode), "crossplatform", StringComparison.OrdinalIgnoreCase)
                };
                crossPlatformItem.Click += delegate { setLibraryFilterMode("crossplatform"); };

                var largeItem = new MenuItem
                {
                    Header = "25+ Captures",
                    IsCheckable = true,
                    IsChecked = string.Equals(NormalizeLibraryFolderFilterMode(libraryFolderFilterMode), "large", StringComparison.OrdinalIgnoreCase)
                };
                largeItem.Click += delegate { setLibraryFilterMode("large"); };

                var missingIdItem = new MenuItem
                {
                    Header = "Missing ID",
                    IsCheckable = true,
                    IsChecked = string.Equals(NormalizeLibraryFolderFilterMode(libraryFolderFilterMode), "missingid", StringComparison.OrdinalIgnoreCase)
                };
                missingIdItem.Click += delegate { setLibraryFilterMode("missingid"); };

                var noCoverItem = new MenuItem
                {
                    Header = "No cover path",
                    IsCheckable = true,
                    IsChecked = string.Equals(NormalizeLibraryFolderFilterMode(libraryFolderFilterMode), "nocover", StringComparison.OrdinalIgnoreCase)
                };
                noCoverItem.Click += delegate { setLibraryFilterMode("nocover"); };

                OpenLibraryButtonMenu(panes.FilterMenuButton, allItem, completedItem, crossPlatformItem, largeItem, missingIdItem, noCoverItem);
            };
        }
    }
}
