namespace PixelVault.Companion;

public sealed partial class AchievementStore
{
    CompanionAchievement LocalRow(string provider,string game,string id)=>local!.Game(provider,game).Rows.SingleOrDefault(r=>r.Id==id)??throw new KeyNotFoundException();
    object LocalView(string provider,string game,CompanionAchievement row)
    {
        PixelVaultNative.AchievementGroupState state;
        try{state=local!.GroupState(provider,game,row.Id);}catch(KeyNotFoundException){return LocalView(row,null);}
        return LocalView(row,state);
    }
    static object LocalView(CompanionAchievement row,PixelVaultNative.AchievementGroupState? state)
    {
        if(state is null)return new{achievement=row,version="",groupSync="unavailable",desktopGroups=row.Groups};
        var groups=new AchievementGroups(state.Groups.Category,state.Groups.Dlc,state.Groups.SortOrder);
        return new{achievement=row with{Groups=groups},version=state.Version,groupSync="synced",desktopGroups=groups};
    }
    object LocalGame(string provider,string game)
    {
        var data=local!.Game(provider,game);
        var groups=local.GroupStates(provider,game);
        return new{provider,gameId=game,progressUpdatedUtc=data.ProgressUpdatedUtc,syncedUtc=data.ProgressUpdatedUtc,
            categories=data.Categories,categoryOrder=data.CategoryOrder,dlcs=data.Dlcs,
            rows=data.Rows.Select(r=>LocalView(r,groups.GetValueOrDefault(r.Id))).ToArray(),
            century=new{group=PixelVault.Tracking.CenturyAchievementCatalog.GroupName,planned=PixelVault.Tracking.CenturyAchievementCatalog.PlannedPerGame,
                status=PixelVault.Tracking.CenturyAchievementCatalog.ConnectionStatus,definitions=PixelVault.Tracking.CenturyAchievementCatalog.ForGame(provider,game).Select(d=>
                {
                    var p=CenturyProgress(provider,game,d.Id);
                    return new{d.Id,d.Provider,d.GameId,d.GameTitle,d.Title,d.Description,d.Category,d.Requirement,d.Order,d.Points,
                        progressKnown=p is not null,unlocked=p?.Unlocked??false,unlockUtc=p?.UnlockUtc??0,counterCurrent=p?.Current,counterTarget=p?.Target,
                        earnedPoints=p?.EarnedPoints??0,verifiedUtc=p?.VerifiedUtc??0,connectionStatus=p?.Status??PixelVault.Tracking.CenturyAchievementCatalog.ConnectionStatus};
                }).ToArray()}};
    }
    IReadOnlyDictionary<string,CompletionSummary> LocalCompletionSummaries()=>local!.Achievements().ToDictionary(g=>g.Provider+":"+g.GameId,g=>
    {
        var progress=g.Rows.Select(r=>new PixelVault.Tracking.AchievementCompletionProgress(r.ProgressKnown,r.Unlocked,!string.IsNullOrWhiteSpace(r.Groups.Dlc))).ToArray();
        return new CompletionSummary(PixelVault.Tracking.AchievementCompletion.Name(PixelVault.Tracking.AchievementCompletion.Classify(progress)),progress.Count(r=>r.Known&&r.Unlocked),progress.Length);
    });
}
