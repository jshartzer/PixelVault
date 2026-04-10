using System;

namespace PixelVaultNative
{
    /// <summary>Actions exposed as the library command palette (Slice E, <c>PV-PLN-V1POL-001</c>). Nullable entries are omitted from the list.</summary>
    internal sealed class LibraryBrowserPaletteContext
    {
        public Action RefreshLibraryFolders { get; init; }
        /// <summary>Re-read EXIF and update SQLite photo-index rows for the folder under the current selection (or toast if none).</summary>
        public Action RunLibraryMetadataIndexScanSelectedFolder { get; init; }
        /// <summary>Re-read EXIF and update SQLite photo-index rows for the whole library (after confirm).</summary>
        public Action RunLibraryMetadataIndexScanFullLibrary { get; init; }
        public Action OpenSettings { get; init; }
        public Action OpenHealthDashboard { get; init; }
        public Action OpenGameIndex { get; init; }
        public Action OpenPhotoIndex { get; init; }
        public Action OpenFilenameRules { get; init; }
        public Action OpenPhotographyGallery { get; init; }
        public Action OpenSavedCoversFolder { get; init; }
        public Action RunImportQuick { get; init; }
        public Action RunImportWithReview { get; init; }
        public Action OpenManualIntake { get; init; }
        public Action OpenIntakePreview { get; init; }
        public Action ExportStarred { get; init; }
        public Action RefreshAllCovers { get; init; }
        public Action ShowKeyboardShortcuts { get; init; }

        public Action ClearLibrarySearch { get; init; }

        public Action SortFoldersAlpha { get; init; }
        public Action SortFoldersDateCaptured { get; init; }
        public Action SortFoldersDateAdded { get; init; }
        public Action SortFoldersMostPhotos { get; init; }

        public Action FilterFoldersAll { get; init; }
        public Action FilterFolders100Percent { get; init; }
        public Action FilterFoldersCrossPlatform { get; init; }
        public Action FilterFolders25PlusCaptures { get; init; }
        public Action FilterFoldersMissingId { get; init; }
        public Action FilterFoldersNoCover { get; init; }

        public Action ToggleQuickEditDrawer { get; init; }

        public Action GroupFoldersAllGames { get; init; }
        public Action GroupFoldersByConsole { get; init; }
        public Action GroupFoldersTimeline { get; init; }
        public Action GroupFoldersFolderGrid { get; init; }

        /// <summary>Open Photo workspace for the current folder selection (<c>PV-PLN-LIBWS-001</c>).</summary>
        public Action EnterPhotoWorkspace { get; init; }

        /// <summary>Exit Photo workspace back to the folder list.</summary>
        public Action ExitPhotoWorkspace { get; init; }
    }
}
