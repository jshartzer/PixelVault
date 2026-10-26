namespace PixelVault.Companion;

public sealed record AchievementGroups(string Category="",string Dlc="",int SortOrder=0);
public sealed record CompanionAchievement(string Id,string Title,string Description,int Order,bool Hidden,
    bool ProgressKnown,bool Unlocked,long UnlockUtc,double? Rarity,double? CounterCurrent,double? CounterTarget,
    string Icon,string Guide,string SourceUrl,string SourceTitle,string Tags,bool Missable,AchievementGroups Groups);
public sealed record AchievementGameSync(string WorkspaceId,string Provider,string GameId,string Account,long CapturedUtc,long ProgressUpdatedUtc,
    IReadOnlyList<CompanionAchievement> Rows,IReadOnlyList<string> Categories,IReadOnlyList<string> CategoryOrder,IReadOnlyList<string> Dlcs);
public sealed record EditAchievementGroups(string Version,string RequestId,AchievementGroups Groups,bool UseDesktop=false);
public sealed record AchievementGroupChange(string Provider,string GameId,string AchievementId,string Id,AchievementGroups Baseline,AchievementGroups Desired);
public sealed record AchievementGroupAck(string Provider,string GameId,string AchievementId,string Id,string Status,AchievementGroups Desktop);
