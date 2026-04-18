using System;
using System.Collections.Generic;

namespace PixelVaultNative
{
    /// <summary>
    /// Dependency surface that <see cref="LibraryBrowserViewModel"/> needs from its outer owner
    /// (today: <see cref="MainWindow"/>). Keeping this narrow and explicit documents the true
    /// coupling between the library read-model and the WPF shell, so future iOS / backend
    /// projection clients can implement this interface against their own state instead of
    /// pulling in MainWindow.
    /// </summary>
    /// <remarks>
    /// The read-only <see cref="LibraryRoot"/> / <see cref="LibraryGroupingMode"/> are live
    /// getters (not captured values) because they mutate during the session — e.g. when the user
    /// switches between "All" and "Console" grouping or changes library roots, every subsequent
    /// call must observe the new value.
    ///
    /// Pure-static helpers (<c>TextAndPathHelpers</c>, <c>LibraryPlatformLabels</c>,
    /// <c>MetadataHelpers.NormalizeConsoleLabel</c>, <c>SettingsService.NormalizeLibraryGroupingMode</c>)
    /// are intentionally NOT part of this interface — <see cref="LibraryBrowserViewModel"/> calls
    /// them directly because they carry no host state.
    ///
    /// Signatures are intentionally non-nullable-annotated to match MainWindow's oblivious
    /// context; the implementing adapter (<c>MainWindowLibraryBrowserVmHost</c>) forwards
    /// straight to MainWindow without annotation friction. Tests can implement this interface
    /// with in-memory stubs.
    /// </remarks>
    internal interface ILibraryBrowserViewModelHost
    {
        string LibraryRoot { get; }
        string LibraryGroupingMode { get; }

        LibraryFolderInfo CloneLibraryFolderInfo(LibraryFolderInfo folder);
        bool SameLibraryFolderSelection(LibraryFolderInfo left, LibraryFolderInfo right);
        DateTime GetLibraryFolderNewestDate(LibraryFolderInfo folder);

        string NormalizeGameId(string value);
        string NormalizeGameIndexName(string name, string folderPath = null);
        string GuessGameIndexNameForFile(string file);
        string PrimaryPlatformLabel(string file);

        DateTime ResolveIndexedLibraryDate(string libraryRoot, string file, Dictionary<string, LibraryMetadataIndexEntry> index = null);
        LibraryMetadataIndexEntry TryGetLibraryMetadataIndexEntry(string libraryRoot, string file, Dictionary<string, LibraryMetadataIndexEntry> index);
        long ResolveLibraryFileRecentSortUtcTicks(string libraryRoot, string file, Dictionary<string, LibraryMetadataIndexEntry> index = null);

        string FormatViewKeyForTroubleshooting(string viewKey);
        string FormatPathForTroubleshooting(string path);
    }
}
