using System.Text.Json;
using PixelVault.Tracking;

namespace PixelVault.Companion;

internal static class EditionGrouping
{
    internal sealed record Capture(string NativeId,string Key,string IgdbId="",string Platforms="");
    internal sealed record Preference(string? XboxTitleId,string? DefaultKey=null);
    internal static Dictionary<string,Preference> ReadPreferences(string root)
    {
        var file=Path.Combine(root,"game-profile-platforms.json");
        if(!File.Exists(file))return [];
        LocalApplicationData.RequireFile(file,root);
        if(new FileInfo(file).Length>1024*1024)throw new InvalidDataException("Game platform links are too large.");
        using var input=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.Read|FileShare.Delete);
        return JsonSerializer.Deserialize<Dictionary<string,Preference>>(input)??[];
    }
    internal static void Apply(Dictionary<string,LibraryGame> games,IEnumerable<Capture> captures,IReadOnlyDictionary<string,Preference> preferences)
    {
        // Provider IDs are identity nodes. A title hash must never pre-merge originals and remakes.
        var parent=games.Keys.ToDictionary(k=>k,k=>k);
        // Capture entries represent different consoles; "library" is not an achievement provider.
        string Scope(LibraryGame game)=>game.Provider=="library"?"library:"+game.Platform.Trim().ToUpperInvariant():game.Provider;
        string Root(string id){while(parent[id]!=id){parent[id]=parent[parent[id]];id=parent[id];}return id;}
        void Join(string a,string b)
        {
            a=Root(a);b=Root(b);if(a==b)return;
            // A group cannot contain two different releases from the same provider.
            if(games.Where(p=>Root(p.Key)==a||Root(p.Key)==b).GroupBy(p=>Scope(p.Value)).Any(g=>g.Count()>1))return;
            parent[b]=a;
        }
        var sources=captures.Where(c=>games.ContainsKey(c.Key)).OrderBy(c=>c.NativeId,StringComparer.Ordinal).ToArray();
        var metadata=sources.GroupBy(c=>c.Key).ToDictionary(g=>g.Key,g=>g.ToArray());
        // Retain ordinary cross-provider groups only when neither provider IDs nor known catalogue IDs conflict.
        foreach(var group in games.Where(p=>p.Value.Provider!="xbox").GroupBy(p=>GameLibrary.GroupKey(p.Value.Title)))
        {
            if(group.GroupBy(p=>Scope(p.Value)).Any(g=>g.Count()>1))continue;
            var ids=group.SelectMany(p=>metadata.GetValueOrDefault(p.Key)??[]).Select(c=>c.IgdbId).Where(id=>!string.IsNullOrWhiteSpace(id)).Distinct().ToArray();
            if(ids.Length>1)continue;
            foreach(var item in group.Skip(1))Join(group.First().Key,item.Key);
        }
        var xbox=games.Values.Where(g=>g.Provider=="xbox").ToArray();
        var automatic=new List<(string Source,string Target)>();
        foreach(var capture in sources)
        {
            var game=games[capture.Key];
            preferences.TryGetValue("id:"+capture.NativeId,out var preference);
            var linked=preference?.XboxTitleId??(preference?.DefaultKey?.StartsWith("xbox:",StringComparison.Ordinal)==true?preference.DefaultKey[5..]:null);
            var target=GameTitleIdentity.MatchXbox(game.Title,xbox,g=>g.GameId,g=>g.Title,g=>g.Platform=="Xbox 360",
                GameTitleIdentity.XboxLegacyHint(game.Platform,capture.Platforms),linked);
            if(target is not null)
            {
                if(!string.IsNullOrWhiteSpace(linked))Join(capture.Key,"xbox:"+target.GameId);
                else automatic.Add((capture.Key,"xbox:"+target.GameId));
            }
        }
        var ambiguousSources=automatic.GroupBy(p=>Root(p.Source)).Where(g=>g.Select(p=>p.Target).Distinct().Count()>1).Select(g=>g.Key).ToHashSet();
        var ambiguousTargets=automatic.GroupBy(p=>(p.Target,games[p.Source].Provider)).Where(g=>g.Select(p=>Root(p.Source)).Distinct().Count()>1).Select(g=>g.Key).ToHashSet();
        var approved=automatic.Where(p=>!ambiguousSources.Contains(Root(p.Source))&&!ambiguousTargets.Contains((p.Target,games[p.Source].Provider))).ToArray();
        foreach(var proposal in approved)Join(proposal.Source,proposal.Target);
        // Keep one existing title ID (prefer the native capture). Split siblings get deterministic edition IDs.
        var components=games.Keys.GroupBy(Root).Select(g=>new{Root=g.Key,Keys=g.ToArray(),
            Anchor=sources.FirstOrDefault(c=>Root(c.Key)==g.Key)}).OrderBy(g=>g.Anchor is null?1:0)
            .ThenBy(g=>g.Anchor?.NativeId??g.Keys.Order(StringComparer.Ordinal).First(),StringComparer.Ordinal);
        var used=new HashSet<string>(StringComparer.Ordinal);
        foreach(var component in components)
        {
            var key=component.Anchor?.Key??component.Keys.Order(StringComparer.Ordinal).First();
            var group=GameLibrary.GroupKey(games[key].Title);
            if(!used.Add(group))
            {
                group=GameLibrary.GroupKey("edition:"+key);used.Add(group);
            }
            foreach(var member in component.Keys)games[member]=games[member] with{GroupId=group};
        }
    }
}
