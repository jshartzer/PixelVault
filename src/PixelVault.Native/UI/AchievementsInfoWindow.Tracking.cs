using System.Linq;
using System.Windows;
using System.Windows.Controls;

namespace PixelVaultNative
{
    sealed partial class AchievementsInfoWindow
    {
        AchievementTrackingStore _trackingStore;
        Button BuildTrackingEntryButton()
        {
            var button = AchievementTrackerWindow.Button("Trackers", "Open this game's achievement checklists");
            button.HorizontalAlignment = HorizontalAlignment.Left; button.Margin = new Thickness(0, 8, 0, 0);
            button.IsEnabled = false;
            Loaded += (_, __) => button.IsEnabled = _trackingStore != null;
            button.Click += (_, __) => OpenTracking(null);
            return button;
        }

        internal void AttachTrackingMenu(FrameworkElement element, GameAchievementsFetchService.AchievementRow row)
        {
            if (_trackingStore == null || element == null || row == null || !row.HasStableProviderIdentity) return;
            element.ContextMenu ??= new ContextMenu();
            var item = new MenuItem { Header = "Track progress…" };
            item.Click += (_, __) => OpenTracking(row);
            element.ContextMenu.Items.Add(item);
        }

        void OpenTracking(GameAchievementsFetchService.AchievementRow row)
        {
            if (_trackingStore == null) return;
            var first = row ?? _loadedResult?.Rows?.FirstOrDefault(r => r.HasStableProviderIdentity);
            var provider = first?.Provider ?? (!string.IsNullOrWhiteSpace(_folder?.SteamAppId) ? "steam" : "retroachievements");
            var id = first?.ProviderGameId ?? (provider == "steam" ? _folder?.SteamAppId : _folder?.RetroAchievementsGameId);
            var game = new AchievementHubGame { Provider = provider, Id = id ?? "", Title = _folder?.Name ?? _heading.Text, Folder = _folder };
            var navigation=new TrackerNavigation(_trackingStore,()=>new AchievementHubSession{SteamAccount=_scoreSteamAccount,RetroAccount=_scoreRetroAccount,Games=new(){game}});
            if(row==null)navigation.OpenGame(this,game);else navigation.OpenAchievement(this,game,row.ProviderAchievementId);
        }
    }
}
