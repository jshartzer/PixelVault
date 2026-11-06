using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using PixelVault.Companion.Local;
using Xunit;

namespace PixelVault.Companion.Tests;
public sealed partial class CompanionTests
{
    [Fact]
    public async Task XboxDirectLocalProjectionRefreshesFromCanonicalSnapshot()
    {
        using var fixture=new DirectCase();
        File.WriteAllText(Path.Combine(fixture.State,"achievement-accounts.json"),JsonSerializer.Serialize(new Dictionary<string,string>{{"steam",Approved},{"xbox","111"}}));
        File.WriteAllText(Path.Combine(fixture.App,"xbox-library.json"),JsonSerializer.Serialize(new{Xuid="111",Titles=new[]{new{Id="42",Name="Xbox game",Legacy=false}}}));
        void Save(bool unlocked)=>File.WriteAllText(Path.Combine(fixture.App,"achievement-hub","xbox.json"),JsonSerializer.Serialize(new{Provider="xbox",Account="111",Games=new Dictionary<string,object>{["42"]=new{UpdatedUtc=DateTime.UtcNow.Ticks,Rows=new[]{new{Provider="xbox",ProviderGameId="42",ProviderAchievementId="ONE",Title="Xbox achievement",ProgressKnown=true,Unlocked=unlocked}}}}}));
        Save(false);
        using var host=fixture.Host();using var owner=host.Client();await Setup(host,owner);
        var library=host.Services.GetRequiredService<GameLibrary>();
        Assert.Contains(library.Games(),g=>g.Provider=="xbox"&&g.GameId=="42");
        var before=await owner.GetFromJsonAsync<JsonElement>("/api/v1/achievements/xbox/42/ONE");
        Assert.False(before.GetProperty("achievement").GetProperty("unlocked").GetBoolean());
        Save(true);
        var after=await owner.GetFromJsonAsync<JsonElement>("/api/v1/achievements/xbox/42/ONE");
        Assert.True(after.GetProperty("achievement").GetProperty("unlocked").GetBoolean());
    }
    [Fact]
    public async Task XboxSnapshotsRequireApprovedAccountAndAppearInCompanion()
    {
        using var fixture=new WorkspaceCase();SourceForTest(fixture);
        using var host=new Host(false,root:fixture.Root);using var owner=host.Client();await Setup(host,owner);
        var pair=PairForTest(fixture,host);using var pc=host.Client();pc.DefaultRequestHeaders.Authorization=new("Bearer",pair.Token);
        var options=host.Services.GetRequiredService<CompanionOptions>();var workspace=host.Services.GetRequiredService<TrackerWorkspace>();
        var accounts=AchievementAccountScope.Read(options,workspace);accounts["xbox"]="111";
        AchievementAccountScope.Configure(options,workspace,accounts);
        host.Services.GetRequiredService<ConnectorSyncState>().EnableLibraryAndAutomatic();
        var data=AchievementFixture(pair.WorkspaceId) with{Provider="xbox",Account="111"};
        Assert.Equal(HttpStatusCode.BadRequest,(await pc.PostAsJsonAsync("/connector/v1/achievements/snapshot",data with{Account="222"})).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await pc.PostAsJsonAsync("/connector/v1/achievements/snapshot",data)).StatusCode);
        var result=await owner.GetFromJsonAsync<JsonElement>("/api/v1/achievements/xbox/42");
        Assert.Equal(2,result.GetProperty("rows").GetArrayLength());
        Assert.Throws<InvalidDataException>(()=>AchievementAccountScope.Configure(options,workspace,new(accounts){["xbox"]="222"}));
        host.Services.GetRequiredService<GameLibrary>().ValidateIncoming(new(1,workspace.Identity,DateTime.UtcNow,
            [new("xbox","42","Xbox game","Xbox",null,null,new(AchievementUnlocked:0,AchievementTotal:2))]));
    }

    [Fact]
    public void XboxLocalCatalogueUsesMatchingAccountAndPreservesUnknownCoverage()
    {
        var root=Path.Combine(Path.GetTempPath(),"pv-xbox-local-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try
        {
            File.WriteAllText(Path.Combine(root,"xbox-library.json"),JsonSerializer.Serialize(new{Xuid="111",Titles=new[]{new{Id="42",Name="Xbox game",Legacy=false}}}));
            Assert.Equal("111",LocalAchievementReader.ConnectedAccounts(root,new())["xbox"]);
            Assert.Throws<InvalidDataException>(()=>LocalAchievementReader.ConnectedAccounts(root,new(){["xbox"]="222"}));
            Assert.Empty(LocalAchievementReader.XboxGames(root,new(){["xbox"]="222"},[]));
            var game=Assert.Single(LocalAchievementReader.XboxGames(root,new(){["xbox"]="111"},[]));
            Assert.Null(game.Stats.AchievementUnlocked);Assert.Null(game.Stats.AchievementTotal);
            var sync=AchievementFixture("workspace") with{Provider="xbox",Account="111"};
            game=Assert.Single(LocalAchievementReader.XboxGames(root,new(){["xbox"]="111"},[sync]));
            Assert.Equal(2,game.Stats.AchievementTotal);Assert.Null(game.Stats.AchievementUnlocked);
        }
        finally {Directory.Delete(root,true);}
    }
}
