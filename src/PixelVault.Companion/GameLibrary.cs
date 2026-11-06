using System.Text.Json;
using System.Text.RegularExpressions;
using System.Text;
using System.Security.Cryptography;

namespace PixelVault.Companion;

public sealed record GameCard(string Provider,string GameId,string Title,string Platform,int TrackerCount,int Completed,int Total,
    string? CoverUrl,string? BannerUrl,GameStats Stats,bool Sample)
{
    public string GroupId { get; init; } = "";
    public string TitleGroupId { get; init; } = GameLibrary.GroupKey(Title);
    public string EditionGroupId => GameLibrary.GroupKey("edition:"+Provider+":"+GameId);
    public ScreenshotLink? Screenshots { get; init; }
    public string CompletionTier { get; init; } = "none";
    public int? CompletionEarned { get; init; }
    public int? CompletionTotal { get; init; }
}

/// <summary>Sanitized, workspace-bound desktop metadata and artwork. Never opens desktop paths or contacts providers.</summary>
public sealed class GameLibrary(CompanionOptions options,TrackerWorkspace workspace,AchievementStore? achievements=null)
{
    static readonly JsonSerializerOptions Json=new(JsonSerializerDefaults.Web);
    Dictionary<string,LibraryGame> metadata=Load(options,workspace);
    readonly LocalApplicationData? local=workspace.LocalData;
    static string Key(string provider,string game)=>provider+":"+game;
    public static string GroupKey(string title)
    {
        // Punctuation/case/platform spelling varies; retain every word and edition number.
        var normalized=string.Concat(title.Normalize(NormalizationForm.FormD).Where(char.IsLetterOrDigit)).ToLowerInvariant();
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized))).ToLowerInvariant()[..32];
    }
    static Dictionary<string,LibraryGame> Load(CompanionOptions options,TrackerWorkspace workspace)
    {
        var result=new Dictionary<string,LibraryGame>(StringComparer.Ordinal);
        if(workspace.DirectLocal)return result;
        var file=Path.Combine(options.DataRoot,"library","catalog.json");
        if(workspace.Sample||workspace.Manifest is null||!File.Exists(file))return result;
        if(new FileInfo(file).Length>10*1024*1024)throw new InvalidDataException("The game catalogue is too large.");
        var catalog=JsonSerializer.Deserialize<GameCatalogue>(File.ReadAllText(file),Json)??throw new InvalidDataException("The game catalogue is invalid.");
        return Validate(catalog,workspace.Identity);
    }
    static Dictionary<string,LibraryGame> Validate(GameCatalogue catalog,string identity)
    {
        var result=new Dictionary<string,LibraryGame>(StringComparer.Ordinal);
        if(catalog.Version!=1)throw new InvalidDataException("This game catalogue needs a newer Companion.");
        // Changing approved account scope invalidates a previous catalogue instead of exposing that scope's stats.
        if(catalog.WorkspaceId!=identity)return result;
        if(catalog.Games is null||catalog.Games.Count>10000)throw new InvalidDataException("The game catalogue is invalid.");
        foreach(var game in catalog.Games)
        {
            if(!Regex.IsMatch(game.Provider??"","^[a-z0-9-]{1,32}$")||!Regex.IsMatch(game.GameId??"","^[a-zA-Z0-9._-]{1,80}$")
                ||string.IsNullOrWhiteSpace(game.Title)||game.Title.Length>250||game.Platform is null||game.Platform.Length>80||game.Stats is null)
                throw new InvalidDataException("The game catalogue contains an invalid game.");
            var safeGame=game with{Cover=game.Cover is not null&&SafeAsset(game.Cover)?game.Cover:null,Banner=game.Banner is not null&&SafeAsset(game.Banner)?game.Banner:null};
            if(game.Stats.PlaytimeMinutes<0||game.Stats.AchievementUnlocked<0||game.Stats.AchievementTotal<0
                ||game.Stats.AchievementUnlocked>game.Stats.AchievementTotal||game.Stats.Captures<0||game.Stats.LastScreenshotUtc<0||game.Stats.LastScreenshotUtc>DateTime.MaxValue.Ticks)throw new InvalidDataException("The game stats are invalid.");
            if(!result.TryAdd(Key(game.Provider!,game.GameId!),safeGame))throw new InvalidDataException("The game catalogue contains duplicate games.");
        }
        return result;
    }
    static bool SafeAsset(string name)=>Regex.IsMatch(name,"^[a-f0-9]{64}\\.(jpg|png|webp)$");
    public void ValidateIncoming(GameCatalogue catalog)
    {
        if(workspace.ReadOnly||catalog.WorkspaceId!=workspace.Identity||catalog.Games is null||catalog.Games.Count==0)throw new InvalidDataException("The library does not match this workspace.");
        _=Validate(catalog,workspace.Identity);
        var accounts=AchievementAccountScope.Read(options,workspace);
        if(catalog.Games.Any(g=>g.Provider is not ("library" or "steam" or "retroachievements" or "xbox")
            ||g.Provider!="library"&&!accounts.ContainsKey(g.Provider)&&(g.Stats.PlaytimeMinutes is not null||g.Stats.AchievementUnlocked is not null||g.Stats.AchievementTotal is not null||g.Stats.LastPlayedUtc is not null||g.Stats.UpdatedUtc is not null)
            ||g.Cover is not null&&!SafeAsset(g.Cover)||g.Banner is not null&&!SafeAsset(g.Banner)))throw new InvalidDataException("The library contains an unapproved provider or artwork reference.");
    }
    public void Reload()=>Volatile.Write(ref metadata,Load(options,workspace));
    string? ArtUrl(LibraryGame? game,string kind)
    {
        var asset=kind=="cover"?game?.Cover:game?.Banner;
        if(asset is null||!workspace.DirectLocal&&!File.Exists(Path.Combine(options.DataRoot,"library","assets",asset)))return null;
        return $"/api/v1/games/{Uri.EscapeDataString(game!.Provider)}/{Uri.EscapeDataString(game.GameId)}/art/{kind}?v={asset[..12]}";
    }
    public IReadOnlyList<GameCard> Games()
    {
        var current=local?.Games()??Volatile.Read(ref metadata);
        var completions=achievements?.CompletionSummaries();
        var screenshots=ScreenshotLibrary.AvailableAll(options,workspace);
        var totals=workspace.Overview().GroupBy(t=>Key(t.Definition.Provider,t.Definition.GameId)).ToDictionary(g=>g.Key,g=>new
        {
            Provider=g.First().Definition.Provider,GameId=g.First().Definition.GameId,Title=g.First().Definition.GameTitle,
            Count=g.Count(),Completed=g.Sum(t=>t.Totals.AggregateCompleted),Total=g.Sum(t=>t.Totals.AggregateTotal)
        },StringComparer.Ordinal);
        var cards=current.Keys.Union(totals.Keys,StringComparer.Ordinal).Select(key=>
        {
            current.TryGetValue(key,out var game);totals.TryGetValue(key,out var tracker);
            return new GameCard(game?.Provider??tracker!.Provider,game?.GameId??tracker!.GameId,game?.Title??tracker!.Title,
                game?.Platform??(tracker!.Provider=="steam"?"Steam":tracker.Provider=="retroachievements"?"RetroAchievements":"Sample"),
                tracker?.Count??0,tracker?.Completed??0,tracker?.Total??0,ArtUrl(game,"cover"),ArtUrl(game,"banner"),game?.Stats??new(),workspace.Sample)
                {GroupId=workspace.DirectLocal&&game?.GroupId is not null?game.GroupId:GroupKey(game?.Title??tracker!.Title),Screenshots=screenshots.GetValueOrDefault((game?.Provider??tracker!.Provider)+"\n"+(game?.GameId??tracker!.GameId)),
                    CompletionTier=completions?.GetValueOrDefault(key)?.Tier??"none",
                    CompletionEarned=completions?.GetValueOrDefault(key)?.Earned,CompletionTotal=completions?.GetValueOrDefault(key)?.Total};
        }).OrderBy(g=>g.Title,StringComparer.CurrentCultureIgnoreCase).ThenBy(g=>g.Provider,StringComparer.Ordinal).ToList();
        // A historic merged pin belongs to one retained card, never every split same-name edition.
        var ambiguous=cards.GroupBy(g=>g.TitleGroupId).Where(g=>g.Select(c=>c.GroupId).Distinct().Count()>1).Select(g=>g.Key).ToHashSet();
        return cards.Select(g=>ambiguous.Contains(g.TitleGroupId)?g with{TitleGroupId=g.GroupId}:g).ToArray();
    }
    public IReadOnlyDictionary<string,string> GroupAliases()
    {
        if(local is null)return Games().SelectMany(g=>new[]{g.TitleGroupId,g.EditionGroupId}.Select(alias=>(Alias:alias,g.GroupId)))
            .GroupBy(g=>g.Alias).Where(g=>g.Select(c=>c.GroupId).Distinct().Count()==1).ToDictionary(g=>g.Key,g=>g.First().GroupId);
        var rows=local.Games().Values.Select(g=>(g.Provider,g.GameId,g.Title,GroupId:g.GroupId??GroupKey(g.Title))).ToList();
        var present=rows.Select(g=>Key(g.Provider,g.GameId)).ToHashSet();
        rows.AddRange(workspace.Definitions().Where(d=>!present.Contains(Key(d.Provider,d.GameId))).Select(d=>(d.Provider,d.GameId,d.GameTitle,GroupKey(d.GameTitle))));
        return rows.SelectMany(g=>new[]{GroupKey(g.Title),GroupKey("edition:"+g.Provider+":"+g.GameId)}.Select(alias=>(Alias:alias,g.GroupId)))
            .GroupBy(g=>g.Alias).Where(g=>g.Select(c=>c.GroupId).Distinct().Count()==1).ToDictionary(g=>g.Key,g=>g.First().GroupId);
    }
    internal string MediaTitle(string provider,string id)=>(local?.Games()??Volatile.Read(ref metadata)).TryGetValue(Key(provider,id),out var game)?game.Title:Games().FirstOrDefault(g=>g.Provider==provider&&g.GameId==id)?.Title??throw new KeyNotFoundException();
    public GameCard Game(string provider,string id)=>Games().FirstOrDefault(g=>g.Provider==provider&&g.GameId==id)??throw new KeyNotFoundException();
    public (string Path,string ContentType) Artwork(string provider,string id,string kind)
    {
        if(local is not null){if(kind is not ("cover" or "banner"))throw new KeyNotFoundException();return local.Artwork(provider,id,kind);}
        if(kind is not ("cover" or "banner")||!Volatile.Read(ref metadata).TryGetValue(Key(provider,id),out var game))throw new KeyNotFoundException();
        var name=kind=="cover"?game.Cover:game.Banner;if(name is null||!SafeAsset(name))throw new KeyNotFoundException();
        var path=Path.Combine(options.DataRoot,"library","assets",name);
        if(!File.Exists(path)||(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new KeyNotFoundException();
        return(path,Path.GetExtension(name) switch{".png"=>"image/png",".webp"=>"image/webp",_=>"image/jpeg"});
    }
}
