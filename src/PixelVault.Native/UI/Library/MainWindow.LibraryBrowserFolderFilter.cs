using System;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        /// <summary>True when primary or any platform label normalizes to Steam.</summary>
        internal static bool LibraryBrowserFolderViewIsSteamTagged(LibraryBrowserFolderView folder, Func<string, string> normalizeConsoleLabel)
        {
            return LibraryBrowseFolderSummary.IsSteamTagged(LibraryBrowseFolderSummary.FromFolderView(folder), normalizeConsoleLabel);
        }

        /// <summary>True when primary or any platform label normalizes to Emulation.</summary>
        internal static bool LibraryBrowserFolderViewIsEmulationTagged(LibraryBrowserFolderView folder, Func<string, string> normalizeConsoleLabel)
        {
            return LibraryBrowseFolderSummary.IsEmulationTagged(LibraryBrowseFolderSummary.FromFolderView(folder), normalizeConsoleLabel);
        }

        /// <summary>Folder-list filter predicate; semantics live on <see cref="LibraryBrowseFolderSummary.MatchesFilter"/> (PV-PLN-UI-001 Step 4).</summary>
        internal static bool LibraryBrowserFolderViewMatchesFilter(string normalizedFilterMode, LibraryBrowserFolderView folder, Func<string, string> normalizeConsoleLabel)
        {
            return LibraryBrowseFolderSummary.MatchesFilter(normalizedFilterMode, LibraryBrowseFolderSummary.FromFolderView(folder), normalizeConsoleLabel);
        }
    }
}
