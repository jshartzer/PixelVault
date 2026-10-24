using System.Windows;
using System.Linq;
using System.Windows.Controls;

namespace PixelVaultNative
{
    sealed partial class AchievementHubWindow
    {
        internal void OpenTracking(AchievementHubGame game = null, GameAchievementsFetchService.AchievementRow row = null)
        {
            if (trackingStore == null) return;
            if(game==null&&row==null)
            {TrackerDashboardWindow.ShowFor(this,new TrackerNavigation(trackingStore,()=>getSession?.Invoke()??new(),cover));return;}
            var navigation=new TrackerNavigation(trackingStore,()=>getSession?.Invoke()??new(),cover);
            if(game!=null){if(row==null)navigation.OpenGame(this,game);else navigation.OpenAchievement(this,game,row.ProviderAchievementId);}
        }

        void AttachTrackingMenu(FrameworkElement element, AchievementHubGame game, GameAchievementsFetchService.AchievementRow row)
        {
            if (trackingStore == null || row?.HasStableProviderIdentity != true) return;
            element.ContextMenu ??= new ContextMenu();
            var item = new MenuItem { Header = "Track progress…" }; item.Click += (_, __) => OpenTracking(game, row);
            element.ContextMenu.Items.Add(item);
        }
    }
}
