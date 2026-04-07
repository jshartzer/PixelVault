using System;

namespace PixelVaultNative
{
    /// <summary>Actions exposed as the library command palette (Slice E, <c>PV-PLN-V1POL-001</c>). Nullable entries are omitted from the list.</summary>
    internal sealed class LibraryBrowserPaletteContext
    {
        public Action RefreshLibraryFolders { get; init; }
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
        public Action FilterFoldersNeedsSteamAppId { get; init; }
        public Action FilterFoldersNoCover { get; init; }

        public Action GroupFoldersAllGames { get; init; }
        public Action GroupFoldersByConsole { get; init; }
        public Action GroupFoldersTimeline { get; init; }
        public Action GroupFoldersFolderGrid { get; init; }
    }
}
