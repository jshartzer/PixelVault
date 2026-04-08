using System.Windows;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        internal void LibraryBrowserShowAchievementsInfo(Window owner, LibraryBrowserFolderView view)
        {
            if (owner == null || view == null) return;
            var folder = BuildLibraryBrowserDisplayFolder(view);
            var normalized = NormalizeConsoleLabel(folder == null ? string.Empty : folder.PlatformLabel);
            AchievementsInfoWindow.ShowModal(
                owner,
                normalized,
                folder,
                CurrentSteamWebApiKey(),
                CurrentRetroAchievementsApiKey(),
                CurrentSteamUserId64(),
                CurrentRetroAchievementsUsername(),
                "PixelVault/" + AppVersion);
        }
    }
}
