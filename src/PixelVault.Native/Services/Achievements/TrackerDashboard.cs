using System;
using System.Collections.Generic;
using System.Linq;

namespace PixelVaultNative
{
    interface ITrackerGameFileSource
    {
        bool Supports(AchievementTrackerDefinition definition);
        string Coverage(AchievementTrackerDefinition definition);
    }
    sealed class TrackerConnectionInfo
    {
        internal bool GameFilesConnected, AutomaticAvailable, AutomaticEnabled;
        internal string Label, Detail;
    }
    static class TrackerPresentation
    {
        internal static TrackerConnectionInfo Connection(AchievementTrackerDefinition definition, TrackerSourceBinding binding,
            TrackerAutomationSettings automatic, TrackerAutomationState state, IEnumerable<ITrackerDataSource> sources)
        {
            var adapters=sources.ToList();var adapter=adapters.FirstOrDefault(a=>a.Id==binding?.AdapterId&&a.Version==binding.AdapterVersion);
            var gameReader=adapter as ITrackerGameFileSource;
            var nativeAvailable=adapters.OfType<ITrackerGameFileSource>().Any(a=>a.Supports(definition));
            bool native=gameReader?.Supports(definition)==true, canAuto=adapter is ITrackerSourceChangeDetector;
            bool enabled=canAuto&&automatic!=null&&automatic.Mode!=TrackerAutomationMode.Manual&&!automatic.Paused;
            var source=binding==null?(nativeAvailable?"Game files available · Not connected":"Manual · Game-file reader unavailable")
                :native?"Game files connected":adapter is SpreadsheetTrackerSource?"Spreadsheet connected":adapter is JsonFileTrackerSource?"Progress file connected":"Source unavailable";
            var mode=enabled?state?.ReviewPending==true?"Automatic · Review needed":"Automatic on":canAuto?automatic?.Paused==true?"Automatic paused":"Automatic available · Off":"Automatic unavailable";
            return new TrackerConnectionInfo { GameFilesConnected=native,AutomaticAvailable=canAuto,AutomaticEnabled=enabled,
                Label=source+" · "+mode,Detail=binding==null?nativeAvailable?"Connect the game-save reader with Refresh or Manage tracker → Source. Automatic refresh is opt-in.":"No validated game-file reader is registered for this tracker. Manual tracking and external progress imports remain available."
                    :(native?gameReader.Coverage(definition)+" ":"")+mode+". Reads use the saved account and existing reconciliation safeguards." };
        }
    }
    sealed class TrackerDashboardItem
    {
        internal AchievementTrackerDefinition Definition;
        internal int Completed,Total,Unconfirmed;
        internal bool AccountConnected;
        internal TrackerConnectionInfo Connection;
        internal string AchievementTitle;
        internal bool Complete=>Total>0&&Completed>=Total;
        internal double Percent=>Total==0?0:100.0*Completed/Total;
        internal string Progress=>AccountConnected?Completed+" / "+Total+" "+Definition.CompletionLabel.ToLowerInvariant():"Connect account to view progress";
    }
    sealed class TrackerDashboardGame
    {
        internal AchievementHubGame Game;
        internal List<TrackerDashboardItem> Trackers=new();
        internal string Summary=>Trackers.Count+" tracker"+(Trackers.Count==1?"":"s")+" · "+Trackers.Count(t=>t.Complete)+" complete";
        internal double? Percent
        {
            get
            {
                var active=Trackers.Where(t=>t.Total>0).ToList();
                if(active.Count==0||active.Any(t=>!t.AccountConnected))return null;
                return 100.0*active.Sum(t=>(long)Math.Clamp(t.Completed,0,t.Total))/active.Sum(t=>(long)t.Total);
            }
        }
        internal string Sources
        {
            get
            {
                int native=Trackers.Count(t=>t.Connection.GameFilesConnected),automatic=Trackers.Count(t=>t.Connection.AutomaticEnabled);
                var source=native>0?native+" game files · "+Trackers.Count(t=>!t.Connection.AutomaticAvailable)+" manual":Trackers.Any(t=>t.Connection.AutomaticAvailable)?"External progress":"Manual trackers";
                return source+"\n"+(automatic>0?"Automatic on: "+automatic:Trackers.Any(t=>t.Connection.AutomaticAvailable)?"Automatic off":"Automatic unavailable");
            }
        }
    }
    static class TrackerDashboard
    {
        internal static List<TrackerDashboardGame> Load(AchievementTrackingStore store,AchievementHubSession session,AchievementHubGame onlyGame=null)
        {
            session??=new();var definitions=store.Definitions();
            var library=(session.Games??new()).Where(g=>g!=null).GroupBy(g=>g.Key).ToDictionary(g=>g.Key,g=>g.First());
            var bundled=AchievementTrackingCatalog.Builtins().Select(d=>d.Id).ToHashSet(StringComparer.Ordinal);
            var custom=definitions.Where(d=>!bundled.Contains(d.Id)).Select(d=>new AchievementHubGame{Provider=d.Provider,Id=d.GameId,Title=d.GameTitle});
            foreach(var game in custom)if(!library.ContainsKey(game.Key))library.Add(game.Key,game);
            if(onlyGame!=null)library=library.Where(p=>p.Key==onlyGame.Key).ToDictionary(p=>p.Key,p=>p.Value);
            var result=new List<TrackerDashboardGame>();var service=new TrackerRefreshService(store);
            var achievements=store.AchievementChoices(session.SteamAccount,session.RetroAccount).GroupBy(r=>(r.Provider,r.ProviderGameId,r.ProviderAchievementId)).ToDictionary(g=>g.Key,g=>g.First().Title);
            foreach(var game in library.Values.OrderBy(g=>g.Title,StringComparer.CurrentCultureIgnoreCase).ThenBy(g=>g.Key))
            {
                var matching=definitions.Where(d=>d.Provider==game.Provider&&d.GameId==game.Id).OrderBy(d=>d.Title,StringComparer.CurrentCultureIgnoreCase).ToList();
                if(matching.Count==0)continue;var entry=new TrackerDashboardGame{Game=game};var account=session.Account(game.Provider);
                foreach(var definition in matching)
                {
                    var states=store.Load(definition.Id,account).Where(s=>!s.Item.Archived).ToList();
                    var totals=TrackerTotals.Calculate(definition,states);
                    var automation=string.IsNullOrWhiteSpace(account)?(new TrackerAutomationSettings(),new TrackerAutomationState()):store.Automation(definition.Id,account);
                    entry.Trackers.Add(new TrackerDashboardItem{Definition=definition,Completed=totals.Completed,Total=totals.Total,Unconfirmed=totals.Unknown,AccountConnected=!string.IsNullOrWhiteSpace(account),
                        AchievementTitle=string.IsNullOrEmpty(definition.AchievementId)?null:achievements.GetValueOrDefault((definition.Provider,definition.GameId,definition.AchievementId)),
                        Connection=service.Connection(definition,store.Source(definition.Id,account),automation.Item1,automation.Item2)});
                }
                result.Add(entry);
            }
            return result;
        }
    }
}
