using System;
using System.Threading.Tasks;
using System.Windows;
namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        void LibraryBrowserScheduleIntakeReviewBadgeRefresh(Window libraryWindow, LibraryBrowserWorkingSet ws, LibraryBrowserNavChrome navChrome)
        {
            if (navChrome.IntakeReviewButton == null || navChrome.IntakeReviewBadge == null || navChrome.IntakeReviewBadgeText == null) return;
            var refreshVersion = ++ws.IntakeBadgeRefreshVersion;
            Task.Factory.StartNew(delegate
            {
                try
                {
                    EnsureSourceFolders();
                    return importService.BuildSourceInventory(importSearchSubfoldersForRename).TopLevelMediaFiles.Count;
                }
                catch
                {
                    return -1;
                }
            }).ContinueWith(delegate(Task<int> badgeTask)
            {
                libraryWindow.Dispatcher.BeginInvoke(new Action(delegate
                {
                    if (refreshVersion != ws.IntakeBadgeRefreshVersion) return;
                    var count = badgeTask.Status == TaskStatus.RanToCompletion ? badgeTask.Result : -1;
                    if (count > 0)
                    {
                        navChrome.IntakeReviewBadgeText.Text = IntakeBadgeCountText(count);
                        navChrome.IntakeReviewBadge.Visibility = Visibility.Visible;
                        navChrome.IntakeReviewButton.ToolTip = count + " intake item(s) waiting";
                    }
                    else
                    {
                        navChrome.IntakeReviewBadgeText.Text = string.Empty;
                        navChrome.IntakeReviewBadge.Visibility = Visibility.Collapsed;
                        navChrome.IntakeReviewButton.ToolTip = count == 0 ? "No intake items waiting" : "Preview upload queue";
                    }
                }));
            }, TaskScheduler.Default);
        }
    }
}
