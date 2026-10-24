using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        TrackerAutomationMonitor trackerAutomation;
        DispatcherTimer trackerAutomationTimer;
        void InitializeTrackerAutomation()
        {
            trackerAutomation = new TrackerAutomationMonitor(achievementTrackingStore);
            achievementTrackingStore.AutomationMonitor = trackerAutomation;
            trackerAutomation.Notice += notice => Dispatcher.BeginInvoke(new Action(() =>
            {
                if (Dispatcher.HasShutdownStarted || notice.Account != (achievementTrackingStore.Definitions().First(d => d.Id == notice.TrackerId).Provider == "steam"
                    ? AchievementTrackingCatalog.AccountKey("steam", CurrentSteamUserId64()) : AchievementTrackingCatalog.AccountKey("retroachievements", CurrentRetroAchievementsUsername()))) return;
                Log("Tracker: " + notice.Message);
                var ws = _libraryBrowserLiveWorkingSet;
                if (!AchievementFullscreenGuard.Suppress() && ws?.LibraryToastBorder != null) ShowLibraryBrowserToast(ws, notice.Title + ": " + notice.Message, () =>
                    {
                        var definition = achievementTrackingStore.Definitions().FirstOrDefault(d => d.Id == notice.TrackerId);
                        if (definition != null) new AchievementTrackerWindow(this, achievementTrackingStore, CurrentSteamUserId64(), CurrentRetroAchievementsUsername(),
                            provider: definition.Provider, gameId: definition.GameId, achievementId: definition.AchievementId).Show();
                    }, "Review tracker");
            }));
            trackerAutomationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            trackerAutomationTimer.Tick += async (_, __) =>
            {
                trackerAutomation.SetAccounts(CurrentSteamUserId64(), CurrentRetroAchievementsUsername());
                try { await Task.Run(() => trackerAutomation.TickAsync()); }
                catch (Exception ex) { Log("Automatic tracker refresh: " + ex.Message); }
            };
            trackerAutomationTimer.Start();
            Closed += (_, __) => { trackerAutomationTimer.Stop(); trackerAutomation.Dispose(); achievementTrackingStore.AutomationMonitor = null; };
        }
    }
}
