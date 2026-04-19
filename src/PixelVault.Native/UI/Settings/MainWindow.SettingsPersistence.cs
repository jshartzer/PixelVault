using System;
using System.IO;
using System.Windows;

namespace PixelVaultNative
{
    /// <summary>
    /// Ini load/save only: merge and persist via <see cref="ISettingsService"/>; field mapping lives in the <c>MainWindow.SettingsState</c> partial.
    /// </summary>
    public sealed partial class MainWindow
    {
        void LoadSettings()
        {
            var merged = settingsService.LoadFromIni(
                settingsPath,
                CaptureAppSettings(),
                appRoot,
                () => FindExecutableOnPath("ffmpeg.exe") ?? string.Empty,
                SettingsService.FindSteamGridDbApiTokenInEnvironment);
            ApplyAppSettings(merged);
            NotifyIfLibraryIndexScopeChanged();
        }

        /// <summary>Library index DB file names are derived from the library path; changing folders swaps to a different SQLite without migrating.</summary>
        void NotifyIfLibraryIndexScopeChanged()
        {
            if (string.IsNullOrWhiteSpace(libraryIndexAnchor)) return;
            if (PathsEqualForLibraryRoot(libraryRoot, libraryIndexAnchor)) return;
            var msg =
                "The library folder in settings is different from the folder your index snapshot is tied to."
                + Environment.NewLine + Environment.NewLine
                + "PixelVault stores the game index, photo index, and related caches **per library path** (separate pixelvault-index-*.sqlite files under your PixelVault data cache). "
                + "Pointing at another folder can look like an empty library until you point back at the original folder."
                + Environment.NewLine + Environment.NewLine
                + "Index snapshot path: " + libraryIndexAnchor + Environment.NewLine
                + "Current library path: " + (libraryRoot ?? string.Empty);
            Log("Library folder path differs from library_index_anchor — per-path index scope. " + libraryIndexAnchor + " vs " + (libraryRoot ?? string.Empty));
            try
            {
                TryLibraryToast(msg, MessageBoxImage.Information);
            }
            catch
            {
            }
        }

        static bool PathsEqualForLibraryRoot(string a, string b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
            try
            {
                return string.Equals(Path.GetFullPath(a.Trim()), Path.GetFullPath(b.Trim()), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
            }
        }

        void SaveSettings()
        {
            settingsService.SaveToIni(settingsPath, CaptureAppSettings());
            _backgroundIntakeAgent?.ApplySettingsAndStart();
        }
    }
}
