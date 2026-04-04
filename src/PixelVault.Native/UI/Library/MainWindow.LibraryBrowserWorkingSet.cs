using System;
using System.Collections.Generic;
using System.Windows.Controls;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        /// <summary>Mutable per-open state for <see cref="ShowLibraryBrowserCore"/> (folder list, detail pane, search, scroll).</summary>
        sealed class LibraryBrowserWorkingSet
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
            internal int LastFolderTileSize = -1;
            internal int LastFolderColumns = -1;
            internal bool LibraryFoldersLoading;
            internal int LibraryFolderRefreshVersion;
            internal int DetailRenderSequence;
            internal bool ResetDetailRowsToLoadingOnNextRender;
            internal int EstimatedDetailRowHeight = 420;
            internal int DetailSelectionAnchorIndex = -1;
            internal int IntakeBadgeRefreshVersion;
        }
    }
}
