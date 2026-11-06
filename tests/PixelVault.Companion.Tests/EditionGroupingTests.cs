using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace PixelVault.Companion.Tests;

public sealed partial class CompanionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeXboxLinkRefreshesCompanionGroupingAndPreservesPinsAndEditionProgress(bool previouslySplit)
    {
        using var fixture=new DirectCase();
        File.WriteAllText(Path.Combine(fixture.State,"achievement-accounts.json"),JsonSerializer.Serialize(new Dictionary<string,string>{{"steam",Approved},{"xbox","111"}}));
        var played=DateTime.UtcNow.AddDays(-1).Ticks;
        File.WriteAllText(Path.Combine(fixture.App,"xbox-library.json"),JsonSerializer.Serialize(new{Xuid="111",Titles=new[]{new{Id="84",Name="Different Xbox title",Legacy=false,LastPlayedUtc=played}}}));
        File.WriteAllText(Path.Combine(fixture.App,"achievement-hub","xbox.json"),JsonSerializer.Serialize(new{Provider="xbox",Account="111",Games=new Dictionary<string,object>{["84"]=new{UpdatedUtc=DateTime.UtcNow.Ticks,Rows=new[]{new{Provider="xbox",ProviderGameId="84",ProviderAchievementId="ONE",Title="Xbox achievement",ProgressKnown=true,Unlocked=true}}}}}));
        using var host=fixture.Host();using var owner=host.Client();await Setup(host,owner);
        var before=(await owner.GetFromJsonAsync<GameCard[]>("/api/v1/games"))!;
        var xbox=before.Single(g=>g.Provider=="xbox");var steam=before.Single(g=>g.Provider=="steam"&&g.GameId=="42");
        Assert.NotEqual(steam.GroupId,xbox.GroupId);
        Assert.Equal(played,xbox.Stats.LastPlayedUtc);
        (await owner.PostAsJsonAsync("/api/v1/preferences/library/pins",new SaveLibraryPin(xbox.GroupId,true))).EnsureSuccessStatusCode();
        if(previouslySplit)
        {
            using var c=new Microsoft.Data.Sqlite.SqliteConnection("Data Source="+Path.Combine(fixture.State,"companion.sqlite")+";Pooling=False");c.Open();
            using var q=c.CreateCommand();q.CommandText="UPDATE companion_preference SET value=$value WHERE name='library'";
            q.Parameters.AddWithValue("$value",JsonSerializer.Serialize(new LibraryLayout("title-az",new[]{xbox.EditionGroupId})));q.ExecuteNonQuery();
        }
        File.WriteAllText(Path.Combine(fixture.App,"game-profile-platforms.json"),JsonSerializer.Serialize(new Dictionary<string,object>{["id:G1"]=new{XboxTitleId="84",DefaultKey="xbox:84"}}));
        var after=(await owner.GetFromJsonAsync<GameCard[]>("/api/v1/games"))!;
        Assert.Equal(steam.GroupId,after.Single(g=>g.Provider=="xbox").GroupId);
        Assert.Equal(0,after.Single(g=>g.Provider=="steam"&&g.GameId=="42").Stats.AchievementUnlocked);
        Assert.Equal(1,after.Single(g=>g.Provider=="xbox").Stats.AchievementUnlocked);
        var pins=(await owner.GetFromJsonAsync<LibraryLayout>("/api/v1/preferences/library"))!;
        Assert.Equal(steam.GroupId,Assert.Single(pins.Pins));
        var sorted=await (await owner.PostAsJsonAsync("/api/v1/preferences/library/sort",new SaveLibrarySort("last-played"))).Content.ReadFromJsonAsync<LibraryLayout>();
        Assert.Equal(steam.GroupId,Assert.Single(sorted!.Pins));
        (await owner.PostAsJsonAsync("/api/v1/preferences/library/pins",new SaveLibraryPin(steam.GroupId,false))).EnsureSuccessStatusCode();
        Assert.Empty(host.Services.GetRequiredService<LibraryPreferences>().Load().Pins);
        File.WriteAllText(Path.Combine(fixture.App,"game-profile-platforms.json"),"{}");
        var unlinked=(await owner.GetFromJsonAsync<GameCard[]>("/api/v1/games"))!;
        Assert.NotEqual(unlinked.Single(g=>g.Provider=="xbox").GroupId,unlinked.Single(g=>g.Provider=="steam"&&g.GameId=="42").GroupId);
    }

    [Fact]
    public void RomanAliasesMatchNativeWhileExplicitLinksAndRemastersRemainDistinct()
    {
        LibraryGame Game(string provider,string id,string name)=>new(provider,id,name,provider,null,null,new());
        var games=new Dictionary<string,LibraryGame>{["steam:1"]=Game("steam","1","Alan Wake 2"),["xbox:2"]=Game("xbox","2","Alan Wake II"),["xbox:3"]=Game("xbox","3","Alan Wake II Remastered"),["xbox:4"]=Game("xbox","4","Other name")};
        var captures=new[]{new EditionGrouping.Capture("G1","steam:1")};
        EditionGrouping.Apply(games,captures,new Dictionary<string,EditionGrouping.Preference>());
        Assert.Equal(games["steam:1"].GroupId,games["xbox:2"].GroupId);
        Assert.NotEqual(games["steam:1"].GroupId,games["xbox:3"].GroupId);
        EditionGrouping.Apply(games,captures,new Dictionary<string,EditionGrouping.Preference>{{"id:G1",new("4")}});
        Assert.Equal(games["steam:1"].GroupId,games["xbox:4"].GroupId);
        Assert.NotEqual(games["steam:1"].GroupId,games["xbox:2"].GroupId);
        EditionGrouping.Apply(games,captures,new Dictionary<string,EditionGrouping.Preference>{{"id:G1",new("unavailable")}});
        Assert.NotEqual(games["steam:1"].GroupId,games["xbox:2"].GroupId);
    }
    [Fact]
    public async Task DeadSpaceRemakeSeparatesOriginalAndKeepsProgressAndPinsOnTheirOwnEdition()
    {
        using var fixture=new DirectCase();
        using(var c=new Microsoft.Data.Sqlite.SqliteConnection("Data Source="+fixture.Index+";Pooling=False"))
        {
            c.Open();using var q=c.CreateCommand();q.CommandText="ALTER TABLE game_index ADD COLUMN igdb_id TEXT;ALTER TABLE game_index ADD COLUMN igdb_platforms TEXT;UPDATE game_index SET name='Dead Space',igdb_id='159119',igdb_platforms='Xbox Series X|S, PC (Microsoft Windows), PlayStation 5'";q.ExecuteNonQuery();
        }
        File.WriteAllText(Path.Combine(fixture.State,"achievement-accounts.json"),JsonSerializer.Serialize(new Dictionary<string,string>{{"steam",Approved},{"xbox","111"}}));
        File.WriteAllText(Path.Combine(fixture.App,"xbox-library.json"),JsonSerializer.Serialize(new{Xuid="111",Titles=new[]{new{Id="2008403666",Name="Dead Space",Legacy=false},new{Id="1161889879",Name="Dead Space\uFFFD",Legacy=true}}}));
        File.WriteAllText(Path.Combine(fixture.App,"achievement-hub","xbox.json"),JsonSerializer.Serialize(new{Provider="xbox",Account="111",Games=new Dictionary<string,object>{["1161889879"]=new{UpdatedUtc=DateTime.UtcNow.Ticks,Rows=new[]{new{Provider="xbox",ProviderGameId="1161889879",ProviderAchievementId="ONE",Title="Original achievement",ProgressKnown=true,Unlocked=true}}}}}));
        using var host=fixture.Host();using var owner=host.Client();await Setup(host,owner);
        var cards=(await owner.GetFromJsonAsync<GameCard[]>("/api/v1/games"))!;
        var remake=cards.Single(g=>g.Provider=="steam"&&g.GameId=="42");
        var original=cards.Single(g=>g.GameId=="1161889879");var modern=cards.Single(g=>g.GameId=="2008403666");
        Assert.Equal(remake.GroupId,modern.GroupId);Assert.NotEqual(remake.GroupId,original.GroupId);
        Assert.Equal("Xbox 360",original.Platform);Assert.Equal("Xbox",modern.Platform);
        Assert.Equal(1,original.Stats.AchievementUnlocked);Assert.Equal(0,remake.Stats.AchievementUnlocked);
        Assert.Equal(GameLibrary.GroupKey("Dead Space"),remake.GroupId);
        Assert.NotEqual(remake.TitleGroupId,original.TitleGroupId);
        (await owner.PostAsJsonAsync("/api/v1/preferences/library/pins",new SaveLibraryPin(remake.GroupId,true))).EnsureSuccessStatusCode();
        var layout=(await owner.GetFromJsonAsync<LibraryLayout>("/api/v1/preferences/library"))!;
        Assert.Equal(remake.GroupId,Assert.Single(layout.Pins));Assert.DoesNotContain(original.TitleGroupId,layout.Pins);
    }

    [Theory]
    [InlineData("",false)]
    [InlineData("Xbox Series X|S, PC",true)]
    [InlineData("Xbox 360, Xbox One",false)]
    public void SameNameXboxGenerationsNeedOneCompatibleEdition(string platforms,bool modernMatch)
    {
        LibraryGame Game(string provider,string id,string platform)=>new(provider,id,"Dead Space",platform,null,null,new());
        var games=new Dictionary<string,LibraryGame>{["steam:1"]=Game("steam","1","Steam"),["xbox:2"]=Game("xbox","2","Xbox"),["xbox:3"]=Game("xbox","3","Xbox 360")};
        var captures=new[]{new EditionGrouping.Capture("G1","steam:1","159119",platforms)};
        EditionGrouping.Apply(games,captures,new Dictionary<string,EditionGrouping.Preference>());
        Assert.Equal(modernMatch,games["steam:1"].GroupId==games["xbox:2"].GroupId);
        Assert.NotEqual(games["steam:1"].GroupId,games["xbox:3"].GroupId);
        Assert.NotEqual(games["xbox:2"].GroupId,games["xbox:3"].GroupId);
        EditionGrouping.Apply(games,captures,new Dictionary<string,EditionGrouping.Preference>{{"id:G1",new("3")}});
        Assert.Equal(games["steam:1"].GroupId,games["xbox:3"].GroupId);
        Assert.NotEqual(games["steam:1"].GroupId,games["xbox:2"].GroupId);
    }

    [Fact]
    public void SameProviderReleasesCannotMergeThroughOneSharedXboxTitle()
    {
        LibraryGame Game(string provider,string id)=>new(provider,id,"Prey",provider,null,null,new());
        var games=new Dictionary<string,LibraryGame>{["steam:1"]=Game("steam","1"),["steam:2"]=Game("steam","2"),["xbox:3"]=Game("xbox","3")};
        EditionGrouping.Apply(games,new[]{new EditionGrouping.Capture("G1","steam:1","100"),new EditionGrouping.Capture("G2","steam:2","200")},new Dictionary<string,EditionGrouping.Preference>());
        Assert.Equal(3,games.Values.Select(g=>g.GroupId).Distinct().Count());
    }

    [Fact]
    public void DifferentCaptureConsolesGroupWithSteamAndXboxWithoutConflatingSameConsoleReleases()
    {
        LibraryGame Game(string provider,string id,string platform)=>new(provider,id,"Death Stranding Director's Cut",platform,null,null,new());
        var games=new Dictionary<string,LibraryGame>{["library:PS5"]=Game("library","PS5","PS5"),["library:XB"]=Game("library","XB","Xbox"),["steam:1850570"]=Game("steam","1850570","Steam"),["xbox:100"]=Game("xbox","100","Xbox")};
        var captures=new[]{new EditionGrouping.Capture("G20","library:PS5"),new EditionGrouping.Capture("G21","library:XB"),new EditionGrouping.Capture("G421","steam:1850570")};
        EditionGrouping.Apply(games,captures,new Dictionary<string,EditionGrouping.Preference>());
        Assert.Single(games.Values.Select(g=>g.GroupId).Distinct());
        Assert.Equal(4,games.Count);
        games["library:other"]=Game("library","other","PS5");
        EditionGrouping.Apply(games,captures.Append(new("G99","library:other")),new Dictionary<string,EditionGrouping.Preference>());
        Assert.NotEqual(games["library:PS5"].GroupId,games["library:other"].GroupId);
    }

    [Fact]
    public void ConflictingCatalogueIdsStillKeepDifferentConsoleCaptureReleasesSeparate()
    {
        var games=new Dictionary<string,LibraryGame>{["library:old"]=new("library","old","Example","PS5",null,null,new()),["library:new"]=new("library","new","Example","Xbox",null,null,new())};
        EditionGrouping.Apply(games,new[]{new EditionGrouping.Capture("G1","library:old","100"),new EditionGrouping.Capture("G2","library:new","200")},new Dictionary<string,EditionGrouping.Preference>());
        Assert.NotEqual(games["library:old"].GroupId,games["library:new"].GroupId);
    }

}
