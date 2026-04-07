using System;
using System.Collections.Generic;
using System.Windows.Controls;
using System.Windows.Threading;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        /// <summary>Mutable per-open state for the Library browser window (folder list, detail pane, search, scroll).</summary>
        internal sealed class LibraryBrowserWorkingSet
        {
            internal LibraryBrowserPaneRefs Panes;
            internal readonly List<LibraryFolderInfo> Folders = new List<LibraryFolderInfo>();
            internal readonly List<LibraryBrowserFolderView> ViewFolders = new List<LibraryBrowserFolderView>();
            internal LibraryBrowserFolderView Current;
            internal readonly HashSet<string> CollapsedPlatformSections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            internal readonly HashSet<string> SelectedDetailFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            internal readonly List<Border> DetailTiles = new List<Border>();
            internal readonly List<string> DetailFilesDisplayOrder = new List<string>();
            internal bool PreserveFolderScrollOnNextRender;
            internal double PreservedFolderScrollOffset;
            internal bool PreserveDetailScrollOnNextRender;
            internal double PreservedDetailScrollOffset;
            internal string PendingLibrarySearchText = string.Empty;
            internal string AppliedLibrarySearchText = string.Empty;
            internal int LastDetailTileSize = -1;
            internal int LastDetailColumns = -1;
            internal double LastDetailViewportWidth;
            internal int LastFolderTileSize = -1;
            internal int LastFolderColumns = -1;
            internal double LastFolderViewportWidth;
            internal bool LibraryFoldersLoading;
            internal int LibraryFolderRefreshVersion;
            internal int DetailRenderSequence;
            internal bool ResetDetailRowsToLoadingOnNextRender;
            internal int EstimatedDetailRowHeight = 420;
            internal int DetailSelectionAnchorIndex = -1;
            internal int IntakeBadgeRefreshVersion;
            internal bool PendingSessionRestore;
            internal string PendingRestoreViewKey;
            internal double PendingRestoreDetailScrollAfterShow;
            internal string TimelineDatePresetKey = "30d";
            internal DateTime TimelineStartDate = DateTime.MinValue;
            internal DateTime TimelineEndDate = DateTime.MinValue;
            internal Border LibraryToastBorder;
            internal TextBlock LibraryToastLabel;
            internal DispatcherTimer LibraryToastTimer;
            internal Grid QuickEditDrawerHost;
            internal bool QuickEditDrawerOpen;

            /// <summary>See <see cref="LibraryWorkspaceMode"/> (<c>PV-PLN-LIBWS-001</c>).</summary>
            internal LibraryWorkspaceMode WorkspaceMode = LibraryWorkspaceMode.Folder;

            /// <summary>Refreshes sort/filter chrome when <see cref="WorkspaceMode"/> changes (photo rail uses separate persisted sort/filter).</summary>
            internal Action RefreshSortFilterChrome;

            internal Action RefreshPhotoRailColumnPickerUi;

            /// <summary>Re-runs <see cref="MainWindow.LibraryBrowserRenderFolderList"/> (folder grid / photo rail tiles).</summary>
            internal Action RerenderFolderList;

            /// <summary>When true, next folder rail render scrolls so <see cref="Current"/> is at the top (Photo workspace).</summary>
            internal bool ScrollPhotoRailSelectionToTopPending;

            internal bool IsFolderWorkspaceMode => WorkspaceMode == LibraryWorkspaceMode.Folder;

            internal bool IsPhotoWorkspaceMode => WorkspaceMode == LibraryWorkspaceMode.Photo;

            internal bool IsTimelineWorkspaceMode => WorkspaceMode == LibraryWorkspaceMode.Timeline;
        }
    }
}
