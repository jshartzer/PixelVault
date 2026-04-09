using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

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

        internal void LibraryBrowserClearAchievementsSummary(LibraryBrowserPaneRefs panes)
        {
            var ws = _libraryBrowserLiveWorkingSet;
            if (ws != null) Interlocked.Increment(ref ws.AchievementsSummaryFetchGeneration);
            if (panes?.PhotoAchievementsSummary == null) return;
            panes.PhotoAchievementsSummary.Text = string.Empty;
            panes.PhotoAchievementsSummary.Visibility = Visibility.Collapsed;
        }

        /// <summary>Photo workspace: prefetch counts for the label next to the achievements button.</summary>
        internal void LibraryBrowserScheduleAchievementsSummaryRefresh(
            LibraryBrowserWorkingSet ws,
            LibraryBrowserPaneRefs panes,
            Window libraryWindow,
            LibraryBrowserFolderView info)
        {
            if (ws == null || panes == null || libraryWindow == null || info == null || panes.PhotoAchievementsSummary == null)
                return;

            var gen = Interlocked.Increment(ref ws.AchievementsSummaryFetchGeneration);
            panes.PhotoAchievementsSummary.Text = "Loading…";
            panes.PhotoAchievementsSummary.Visibility = Visibility.Visible;

            var folder = BuildLibraryBrowserDisplayFolder(info);
            var normalized = NormalizeConsoleLabel(folder == null ? string.Empty : folder.PlatformLabel);
            var captureInfo = info;

            _ = Task.Run(async () =>
            {
                GameAchievementsFetchService.FetchResult result;
                try
                {
                    result = await GameAchievementsFetchService.FetchAsync(
                        normalized,
                        folder,
                        CurrentSteamWebApiKey(),
                        CurrentRetroAchievementsApiKey(),
                        CurrentSteamUserId64(),
                        CurrentRetroAchievementsUsername(),
                        "PixelVault/" + AppVersion,
                        default).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    result = new GameAchievementsFetchService.FetchResult { ErrorMessage = ex.Message };
                }

                await libraryWindow.Dispatcher.InvokeAsync(() =>
                {
                    if (gen != Volatile.Read(ref ws.AchievementsSummaryFetchGeneration)) return;
                    if (!SameLibraryBrowserSelection(ws.Current, captureInfo)) return;
                    if (result.IsError || result.Rows == null || result.Rows.Count == 0)
                    {
                        panes.PhotoAchievementsSummary.Text = string.Empty;
                        panes.PhotoAchievementsSummary.Visibility = Visibility.Collapsed;
                        return;
                    }
                    var rows = result.Rows;
                    var total = rows.Count;
                    var earned = rows.Count(r => r.ProgressKnown && r.Unlocked);
                    panes.PhotoAchievementsSummary.Text = earned + " of " + total + " Achievements earned";
                    panes.PhotoAchievementsSummary.Visibility = Visibility.Visible;
                }, DispatcherPriority.Background);
            });
        }
    }
}
