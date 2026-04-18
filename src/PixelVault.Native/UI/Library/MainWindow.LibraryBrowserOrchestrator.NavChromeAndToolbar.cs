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
            Action openSelectedLibraryMetadataEditor,
            Action deleteSelectedLibraryFiles,
            Action<string> setLibraryGroupingMode,
            Action<string> setLibrarySortMode,
            Action<string> setLibraryFilterMode)
        {
            void OpenLibraryButtonMenu(Button anchor, params object[] items)
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
                    if (item is MenuItem mi) menu.Items.Add(mi);
                    else if (item is Separator) menu.Items.Add(new Separator());
                }
                anchor.ContextMenu = menu;
                menu.IsOpen = true;
            }

            navChrome.RefreshButton.Click += delegate
            {
                if (refreshLibraryFoldersAsync != null) refreshLibraryFoldersAsync(false);
            };
            navChrome.SettingsButton.Click += delegate
            {
                var settingsItem = new MenuItem { Header = "Settings..." };
                settingsItem.Click += delegate
                {
                    ShowSettingsWindow();
                    if (refreshIntakeReviewBadge != null) refreshIntakeReviewBadge();
                };

                var exportStarredItem = new MenuItem
                {
                    Header = "Export Starred",
                    IsEnabled = navChrome.ExportStarredButton == null || navChrome.ExportStarredButton.IsEnabled
                };
                exportStarredItem.Click += delegate { ExportStarredLibraryCapturesToFolder(libraryWindow); };

                OpenLibraryButtonMenu(navChrome.SettingsButton, settingsItem, exportStarredItem);
            };
            navChrome.GameIndexButton.Click += delegate { OpenGameIndexEditor(); };
            navChrome.PhotoIndexButton.Click += delegate { OpenPhotoIndexEditor(); };
            navChrome.PhotographyGalleryButton.Click += delegate { ShowPhotographyGallery(libraryWindow); };
            navChrome.MyCoversButton.Click += delegate { OpenSavedCoversFolder(); };
            navChrome.ImportButton.Click += delegate { RunWorkflow(false); if (refreshIntakeReviewBadge != null) refreshIntakeReviewBadge(); };
            navChrome.ImportCommentsButton.Click += delegate { RunWorkflow(true); if (refreshIntakeReviewBadge != null) refreshIntakeReviewBadge(); };
            navChrome.ManualImportButton.Click += delegate { OpenManualIntakeWindow(); if (refreshIntakeReviewBadge != null) refreshIntakeReviewBadge(); };
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
            panes.SortFilterMenuButton.Click += delegate
            {
                var sortNorm = NormalizeLibraryFolderSortMode(libraryFolderSortMode);
                var filterNorm = NormalizeLibraryFolderFilterMode(libraryFolderFilterMode);
                var alphaItem = new MenuItem
                {
                    Header = "Alphabetical",
                    IsCheckable = true,
                    IsChecked = string.Equals(sortNorm, "alpha", StringComparison.OrdinalIgnoreCase)
                };
                alphaItem.Click += delegate { setLibrarySortMode("alpha"); };

                var capturedItem = new MenuItem
                {
                    Header = "Date Captured",
                    IsCheckable = true,
                    IsChecked = string.Equals(sortNorm, "captured", StringComparison.OrdinalIgnoreCase)
                };
                capturedItem.Click += delegate { setLibrarySortMode("captured"); };

                var addedItem = new MenuItem
                {
                    Header = "Date Added",
                    IsCheckable = true,
                    IsChecked = string.Equals(sortNorm, "added", StringComparison.OrdinalIgnoreCase)
                };
                addedItem.Click += delegate { setLibrarySortMode("added"); };

                var photosItem = new MenuItem
                {
                    Header = "Most Photos",
                    IsCheckable = true,
                    IsChecked = string.Equals(sortNorm, "photos", StringComparison.OrdinalIgnoreCase)
                };
                photosItem.Click += delegate { setLibrarySortMode("photos"); };

                var allItem = new MenuItem
                {
                    Header = "All Games",
                    IsCheckable = true,
                    IsChecked = string.Equals(filterNorm, "all", StringComparison.OrdinalIgnoreCase)
                };
                allItem.Click += delegate { setLibraryFilterMode("all"); };

                var completedItem = new MenuItem
                {
                    Header = "100% Achievements",
                    IsCheckable = true,
                    IsChecked = string.Equals(filterNorm, "completed", StringComparison.OrdinalIgnoreCase)
                };
                completedItem.Click += delegate { setLibraryFilterMode("completed"); };

                var crossPlatformItem = new MenuItem
                {
                    Header = "Cross-Platform",
                    IsCheckable = true,
                    IsChecked = string.Equals(filterNorm, "crossplatform", StringComparison.OrdinalIgnoreCase)
                };
                crossPlatformItem.Click += delegate { setLibraryFilterMode("crossplatform"); };

                var largeItem = new MenuItem
                {
                    Header = "25+ Captures",
                    IsCheckable = true,
                    IsChecked = string.Equals(filterNorm, "large", StringComparison.OrdinalIgnoreCase)
                };
                largeItem.Click += delegate { setLibraryFilterMode("large"); };

                var missingIdItem = new MenuItem
                {
                    Header = "Missing ID / assignment",
                    ToolTip = "Folders missing SteamGrid ID, Steam/Retro IDs when expected, or captures with no game assignment yet.",
                    IsCheckable = true,
                    IsChecked = string.Equals(filterNorm, "missingid", StringComparison.OrdinalIgnoreCase)
                };
                missingIdItem.Click += delegate { setLibraryFilterMode("missingid"); };

                var noCoverItem = new MenuItem
                {
                    Header = "No cover path",
                    IsCheckable = true,
                    IsChecked = string.Equals(filterNorm, "nocover", StringComparison.OrdinalIgnoreCase)
                };
                noCoverItem.Click += delegate { setLibraryFilterMode("nocover"); };

                OpenLibraryButtonMenu(
                    panes.SortFilterMenuButton,
                    alphaItem,
                    capturedItem,
                    addedItem,
                    photosItem,
                    new Separator(),
                    allItem,
                    completedItem,
                    crossPlatformItem,
                    largeItem,
                    missingIdItem,
                    noCoverItem);
            };
        }
    }
}
