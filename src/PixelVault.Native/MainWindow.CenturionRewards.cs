using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using PixelVault.Tracking;

namespace PixelVaultNative;

public sealed partial class MainWindow
{
    CenturionRewardLedger centurionLedger;
    readonly Queue<CenturionReward> centurionQueue=new();
    CenturionRewardWindow centurionWindow;
    string centurionWindowScope;
    DateTime nextCenturionCheck;
    void InitializeCenturionRewards()
    {
        try{var path=Path.Combine(dataRoot,"centurion-rewards.json");centurionLedger=File.Exists(path)?JsonSerializer.Deserialize<CenturionRewardLedger>(File.ReadAllText(path)):new();}
        catch(Exception ex){Log("Centurion notification history: "+ex.Message);centurionLedger=new();}
        centurionLedger??=new();centurionLedger.Seen??=new();
        Closed+=(_,__)=>{centurionQueue.Clear();centurionWindow?.Close();};
    }
    void PumpCenturionRewards()
    {
        if(centurionLedger==null)return;
        if(DateTime.UtcNow>=nextCenturionCheck)
        {
            nextCenturionCheck=DateTime.UtcNow.AddSeconds(5);
            try
            {
                var snapshots=new List<CenturionGame>();
                foreach(var group in CenturyAchievementCatalog.All.GroupBy(d=>(d.Provider,d.GameId)))
                {
                    var account=group.Key.Provider=="steam"?CurrentSteamUserId64():CurrentRetroAchievementsUsername();
                    if(string.IsNullOrWhiteSpace(account))continue;
                    snapshots.Add(CenturionRewards.Snapshot(group.Key.Provider,group.Key.GameId,CenturionRewards.Scope(dataRoot,group.Key.Provider,group.Key.GameId,account),
                        id=>achievementTrackingStore.Century(group.Key.Provider,group.Key.GameId,id,account)));
                }
                var scopes=snapshots.Select(s=>s.Scope).ToHashSet();
                if(centurionWindow!=null&&!scopes.Contains(centurionWindowScope))centurionWindow.Close();
                var retained=centurionQueue.Where(n=>scopes.Contains(n.Scope)).ToArray();centurionQueue.Clear();foreach(var item in retained)centurionQueue.Enqueue(item);
                var before=JsonSerializer.Serialize(centurionLedger);var notices=centurionLedger.Observe(snapshots);var after=JsonSerializer.Serialize(centurionLedger);
                if(before!=after)
                {
                    var path=Path.Combine(dataRoot,"centurion-rewards.json");File.WriteAllText(path+".tmp",after);File.Move(path+".tmp",path,true);
                }
                if(_achievementNotifications.Data.Enabled)foreach(var notice in notices)centurionQueue.Enqueue(notice);
            }
            catch(Exception ex){Log("Centurion rewards: "+ex.Message);}
        }
        if(!_achievementNotifications.Data.Enabled){centurionQueue.Clear();centurionWindow?.Close();return;}
        if(AchievementFullscreenGuard.Suppress()){centurionWindow?.Close();return;}
        if(centurionWindow!=null||_achievementToasts.Count>0||centurionQueue.Count==0)return;
        var reward=centurionQueue.Dequeue();centurionWindowScope=reward.Scope;centurionWindow=new CenturionRewardWindow(reward);
        centurionWindow.Closed+=(_,__)=>centurionWindow=null;centurionWindow.Show();
    }
}
