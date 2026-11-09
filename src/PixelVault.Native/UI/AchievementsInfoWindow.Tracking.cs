using System.Linq;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace PixelVaultNative
{
    sealed partial class AchievementsInfoWindow
    {
        AchievementTrackingStore _trackingStore;
        void SubscribeToSavedProgress()
        {
            var owner=Owner;
            while(owner!=null && owner is not MainWindow)owner=owner.Owner;
            if(owner is not MainWindow main)return;
            string appliedKey=null;
            SavedRefreshSubscription.Attach(this,main.RefreshUpdateReceiver,(before,after)=>after.Changed(before,"achievements"),async()=>
            {
                var result=_loadedResult;var first=result?.Rows?.FirstOrDefault(r=>r.HasStableProviderIdentity);
                if(first==null)return;
                var account=first.Provider=="steam"?_scoreSteamAccount:first.Provider=="xbox"?_scoreXboxAccount:_scoreRetroAccount;
                if(string.IsNullOrWhiteSpace(account))return;
                var revision=main.RefreshUpdateReceiver.Current?.Achievements.GetValueOrDefault(first.Provider+":"+first.ProviderGameId);
                var key=account+"|"+first.Provider+":"+first.ProviderGameId+"|"+revision;
                if(key==appliedKey)return;
                var store=new AchievementHubStore(System.IO.Path.Combine(main.RefreshDataRoot,"achievement-hub"),_guideService as IAchievementScoreStore);
                var saved=await store.LoadGameAsync(first.Provider,account,first.ProviderGameId,default);
                if(_isClosed||_isLoading||_loadedResult!=result)return;
                if(saved!=null)
                {
                    var rows=saved.Rows.ToDictionary(r=>r.ProviderAchievementId);
                    foreach(var row in result.Rows)if(rows.TryGetValue(row.ProviderAchievementId,out var next))
                    {
                        row.ProgressKnown=next.ProgressKnown;row.Unlocked=next.Unlocked;row.UnlockUtcTicks=next.UnlockUtcTicks;
                        row.CounterCurrent=next.CounterCurrent;row.CounterMinimum=next.CounterMinimum;row.CounterTarget=next.CounterTarget;
                        row.Points=next.Points;row.AwardedPoints=next.AwardedPoints;
                    }
                    result.CapturedUtcTicks=saved.UpdatedUtc;result.ScoreTotals=saved.ScoreTotals;
                }
                ApplyLoadedResult(result);appliedKey=key;
            },()=>_isLoading);
        }
        Button _trackingButton;
        Button BuildTrackingEntryButton()
        {
            var button = _trackingButton = AchievementTrackerWindow.Button("Trackers", "Open this game's achievement checklists");
            button.HorizontalAlignment = HorizontalAlignment.Left; button.Margin = new Thickness(0, 8, 0, 0);
            button.IsEnabled = false;
            Loaded += (_, __) => button.IsEnabled = _trackingStore != null;
            button.Click += (_, __) => OpenTracking(null);
            return button;
        }

        internal void AttachTrackingMenu(FrameworkElement element, GameAchievementsFetchService.AchievementRow row)
        {
            if (_trackingStore == null || element == null || row == null || !row.HasStableAchievementIdentity) return;
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
