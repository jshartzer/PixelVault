namespace PixelVault.Companion;

public sealed record WorkspaceManifest(int Version,Dictionary<string,string> Accounts,DateTime CopiedUtc,string Mode="isolated-copy");
public sealed record GameStats(int? PlaytimeMinutes=null,int? AchievementUnlocked=null,int? AchievementTotal=null,
    long? LastPlayedUtc=null,long? UpdatedUtc=null,int? Captures=null,long? LastScreenshotUtc=null);
public sealed record LibraryGame(string Provider,string GameId,string Title,string Platform,string? Cover,string? Banner,GameStats Stats);
public sealed record GameCatalogue(int Version,string WorkspaceId,DateTime ExportedUtc,List<LibraryGame> Games);
public sealed record LibraryUploadPlan(string Id,IReadOnlyList<string> MissingAssets,bool Current);
