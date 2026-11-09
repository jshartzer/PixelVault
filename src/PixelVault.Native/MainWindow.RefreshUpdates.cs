using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PixelVault.Companion.Client;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        LocalRefreshListener localRefreshUpdates;
        internal LocalRefreshListener RefreshUpdateReceiver=>localRefreshUpdates;
        internal string RefreshDataRoot=>dataRoot;
        RefreshSnapshot pendingLocalRefresh, appliedLocalRefresh;
        bool readingLocalRefresh;

        void InitializeRefreshUpdates()
        {
            localRefreshUpdates=new LocalRefreshListener(dataRoot);
            achievementTrackingStore.RefreshUpdates=localRefreshUpdates;
            localRefreshUpdates.Changed+=(previous,snapshot)=>Dispatcher.BeginInvoke(new Action(()=>
            {
                if(_achievementMonitorCancellation.IsCancellationRequested)return;
                pendingLocalRefresh=snapshot;_ = ReadPushedAchievementsAsync();
            }));
            localRefreshUpdates.Start();
            Closed+=(_,__)=>{localRefreshUpdates.Dispose();achievementTrackingStore.RefreshUpdates=null;};
        }
        async Task ReadPushedAchievementsAsync()
        {
            if(readingLocalRefresh || pendingLocalRefresh==null || _achievementMonitorCancellation.IsCancellationRequested)return;
            readingLocalRefresh=true;
            try
            {
                while(pendingLocalRefresh is { } snapshot)
                {
                    pendingLocalRefresh=null;
                    if(snapshot.Changed(appliedLocalRefresh,"achievements"))
                    {
                        var titles=GetSavedGameIndexRowsForRoot(libraryRoot)??new List<GameIndexEditorRow>();
                        foreach(var identity in new[]{(Provider:"steam",Account:CurrentSteamUserId64()),(Provider:"retroachievements",Account:CurrentRetroAchievementsUsername())})
                        {
                            if(string.IsNullOrWhiteSpace(identity.Account))continue;
                            var changed=snapshot.Achievements.Keys.Where(key=>key.StartsWith(identity.Provider+":",StringComparison.Ordinal)
                                && (appliedLocalRefresh==null || appliedLocalRefresh.WorkspaceId!=snapshot.WorkspaceId
                                || appliedLocalRefresh.Achievements.GetValueOrDefault(key)!=snapshot.Achievements[key])).ToHashSet();
                            if(changed.Count==0)continue;
                            var saved=await _achievementHubStore.LoadAsync(identity.Provider,identity.Account,_achievementMonitorCancellation.Token);
                            var currentAccount=identity.Provider=="steam"?CurrentSteamUserId64():CurrentRetroAchievementsUsername();
                            if(!string.Equals(identity.Account,currentAccount,StringComparison.OrdinalIgnoreCase))continue;
                            foreach(var game in saved.Games.Where(g=>changed.Contains(identity.Provider+":"+g.Key)))
                                ObserveSavedAchievement(new(){Rows=game.Value.Rows,NotificationOnly=true,CapturedUtcTicks=game.Value.UpdatedUtc,
                                    GameTitle=titles.FirstOrDefault(g=>(identity.Provider=="steam"?g.SteamAppId:g.RetroAchievementsGameId)==game.Key)?.Name},identity.Account);
                        }
                        QueueTimelineAchievementRefresh();QueueLibraryCompletionRefresh();
                    }
                    appliedLocalRefresh=snapshot;
                }
            }
            catch(OperationCanceledException){}
            catch(Exception ex){pendingLocalRefresh=localRefreshUpdates.Current;Log("Saved progress update: "+ex.Message);}
            finally{readingLocalRefresh=false;}
        }
        void ObserveSavedAchievement(GameAchievementsFetchService.FetchResult result,string account)
        {
            // The service already committed this snapshot. Do not write it back and create a feedback loop.
            if(_achievementMonitorCancellation.IsCancellationRequested)return;
            _=SyncAchievementCompletionAsync(result,account,libraryRoot);
            var notices=_achievementNotifications.Observe(result,account);SaveAchievementNotifications();
            if(!AchievementFullscreenGuard.Suppress())foreach(var notice in notices)_achievementToastQueue.Enqueue(notice);
            UpdateAchievementNotificationBadge();_reloadAchievementHistory?.Invoke();
        }
    }
}
