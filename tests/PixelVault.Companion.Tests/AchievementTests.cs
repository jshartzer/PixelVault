using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace PixelVault.Companion.Tests;
public sealed partial class CompanionTests
{
    static AchievementGameSync AchievementFixture(string workspace)=>new(workspace,"steam","42",Approved,DateTime.UtcNow.Ticks,DateTime.UtcNow.Ticks,
        [new("ONE","One achievement","Description",0,false,true,false,0,12.5,2,10,"","**A saved guide**\n- First step\n- Second step","https://example.com/guide","Saved source","Collectible",true,new("Story","Expansion",3)),new("TWO","Unknown progress","Description",1,false,false,true,0,null,null,null,"","","","","",false,new("Actions"))],["Story","Actions","Collectibles"],["Actions","Story"],["Expansion"]);
    static string GroupVersion(JsonElement view)=>view.GetProperty("version").GetString()!;
    [Fact]
    public async Task AchievementsPreserveGuidesHierarchyUnknownProgressAndRequireOwnerAndExplicitPcScope()
    {
        using var fixture=new WorkspaceCase();SourceForTest(fixture);using var host=new Host(false,root:fixture.Root);using var owner=host.Client();await Setup(host,owner);var pair=PairForTest(fixture,host);using var pc=host.Client();pc.DefaultRequestHeaders.Authorization=new("Bearer",pair.Token);var data=AchievementFixture(pair.WorkspaceId);
        Assert.Equal(HttpStatusCode.Unauthorized,(await host.Client().GetAsync("/api/v1/achievements/steam/42")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await pc.PostAsJsonAsync("/connector/v1/achievements/snapshot",data)).StatusCode);
        host.Services.GetRequiredService<ConnectorSyncState>().EnableLibraryAndAutomatic();
        data=data with{Rows=data.Rows.Select(r=>r.Id=="ONE"?r with{Title=new string('A',611)}:r).ToArray()};
        Assert.Equal(HttpStatusCode.BadRequest,(await pc.PostAsJsonAsync("/connector/v1/achievements/snapshot",data with{Account=Other})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await pc.PostAsJsonAsync("/connector/v1/achievements/snapshot",data with{WorkspaceId="other"})).StatusCode);
        Assert.Equal(HttpStatusCode.OK,(await pc.PostAsJsonAsync("/connector/v1/achievements/snapshot",data)).StatusCode);
        var game=await owner.GetFromJsonAsync<JsonElement>("/api/v1/achievements/steam/42");Assert.Equal("Actions",game.GetProperty("categoryOrder")[0].GetString());
        var one=await owner.GetFromJsonAsync<JsonElement>("/api/v1/achievements/steam/42/ONE");Assert.Equal(data.Rows[0].Guide,one.GetProperty("achievement").GetProperty("guide").GetString());Assert.Equal("Expansion",one.GetProperty("achievement").GetProperty("groups").GetProperty("dlc").GetString());
        var two=await owner.GetFromJsonAsync<JsonElement>("/api/v1/achievements/steam/42/TWO");Assert.False(two.GetProperty("achievement").GetProperty("unlocked").GetBoolean());Assert.False(two.GetProperty("achievement").GetProperty("progressKnown").GetBoolean());
        await pc.PostAsJsonAsync("/connector/v1/achievements/snapshot",data with{CapturedUtc=DateTime.UtcNow.Ticks,ProgressUpdatedUtc=0,Rows=data.Rows.Select(r=>r with{ProgressKnown=false,CounterCurrent=null,Guide="Updated guide metadata"}).ToArray()});
        var retained=await owner.GetFromJsonAsync<JsonElement>("/api/v1/achievements/steam/42/ONE");Assert.True(retained.GetProperty("achievement").GetProperty("progressKnown").GetBoolean());Assert.Equal(2,retained.GetProperty("achievement").GetProperty("counterCurrent").GetDouble());Assert.Equal("Updated guide metadata",retained.GetProperty("achievement").GetProperty("guide").GetString());
        Assert.Equal(HttpStatusCode.NotFound,(await owner.GetAsync("/api/v1/achievements/retroachievements/42")).StatusCode);
        owner.DefaultRequestHeaders.Remove("X-CSRF-TOKEN");Assert.Equal(HttpStatusCode.BadRequest,(await owner.PostAsJsonAsync("/api/v1/achievements/steam/42/ONE/groups",new EditAchievementGroups(GroupVersion(one),Guid.NewGuid().ToString(),new("Collectibles","Expansion",3)))).StatusCode);
    }
    [Fact]
    public async Task WebGroupsSurvivePcRefreshAndConflictsRequireExplicitResolutionWithoutChangingGuides()
    {
        using var fixture=new WorkspaceCase();SourceForTest(fixture);using var host=new Host(false,root:fixture.Root);using var owner=host.Client();await Setup(host,owner);var pair=PairForTest(fixture,host);using var pc=host.Client();pc.DefaultRequestHeaders.Authorization=new("Bearer",pair.Token);host.Services.GetRequiredService<ConnectorSyncState>().EnableLibraryAndAutomatic();var data=AchievementFixture(pair.WorkspaceId);await pc.PostAsJsonAsync("/connector/v1/achievements/snapshot",data);
        var initial=await owner.GetFromJsonAsync<JsonElement>("/api/v1/achievements/steam/42/ONE");var edit=new EditAchievementGroups(GroupVersion(initial),Guid.NewGuid().ToString(),new("Collectibles","Expansion",3));
        var response=await owner.PostAsJsonAsync("/api/v1/achievements/steam/42/ONE/groups",edit);Assert.Equal("pending",(await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("groupSync").GetString());
        Assert.Equal(HttpStatusCode.OK,(await owner.PostAsJsonAsync("/api/v1/achievements/steam/42/ONE/groups",edit)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict,(await owner.PostAsJsonAsync("/api/v1/achievements/steam/42/ONE/groups",edit with{RequestId=Guid.NewGuid().ToString()})).StatusCode);
        await pc.PostAsJsonAsync("/connector/v1/achievements/snapshot",data with{CapturedUtc=DateTime.UtcNow.Ticks});var changes=(await pc.GetFromJsonAsync<AchievementGroupChange[]>("/connector/v1/achievements/groups"))!;Assert.Single(changes);Assert.Equal("Story",changes[0].Baseline.Category);
        var changed=data.Rows.Select(r=>r.Id=="ONE"?r with{Groups=new("Difficulty","Expansion",3)}:r).ToArray();
        await pc.PostAsJsonAsync("/connector/v1/achievements/snapshot",data with{CapturedUtc=DateTime.UtcNow.Ticks,Rows=changed});var conflict=await owner.GetFromJsonAsync<JsonElement>("/api/v1/achievements/steam/42/ONE");Assert.Equal("conflict",conflict.GetProperty("groupSync").GetString());Assert.Equal("Collectibles",conflict.GetProperty("achievement").GetProperty("groups").GetProperty("category").GetString());Assert.Empty((await pc.GetFromJsonAsync<AchievementGroupChange[]>("/connector/v1/achievements/groups"))!);
        await owner.PostAsJsonAsync("/api/v1/achievements/steam/42/ONE/groups",new EditAchievementGroups(GroupVersion(conflict),Guid.NewGuid().ToString(),edit.Groups));var next=(await pc.GetFromJsonAsync<AchievementGroupChange[]>("/connector/v1/achievements/groups"))!.Single();Assert.Equal("Difficulty",next.Baseline.Category);
        await pc.PostAsJsonAsync("/connector/v1/achievements/groups/ack",new AchievementGroupAck("steam","42","ONE",next.Id,"applied",next.Desired));var done=await owner.GetFromJsonAsync<JsonElement>("/api/v1/achievements/steam/42/ONE");Assert.Equal("synced",done.GetProperty("groupSync").GetString());Assert.Equal(data.Rows[0].Guide,done.GetProperty("achievement").GetProperty("guide").GetString());
    }
    [Fact]
    public async Task AcknowledgingAnEarlierWebEditDoesNotLoseTheNewerEditOrCreateAFalseDesktopConflict()
    {
        using var fixture=new WorkspaceCase();SourceForTest(fixture);using var host=new Host(false,root:fixture.Root);using var owner=host.Client();await Setup(host,owner);var pair=PairForTest(fixture,host);using var pc=host.Client();pc.DefaultRequestHeaders.Authorization=new("Bearer",pair.Token);host.Services.GetRequiredService<ConnectorSyncState>().EnableLibraryAndAutomatic();await pc.PostAsJsonAsync("/connector/v1/achievements/snapshot",AchievementFixture(pair.WorkspaceId));
        var initial=await owner.GetFromJsonAsync<JsonElement>("/api/v1/achievements/steam/42/ONE");var firstId=Guid.NewGuid().ToString();var firstGroups=new AchievementGroups("Collectibles","Expansion",3);
        var first=await (await owner.PostAsJsonAsync("/api/v1/achievements/steam/42/ONE/groups",new EditAchievementGroups(GroupVersion(initial),firstId,firstGroups))).Content.ReadFromJsonAsync<JsonElement>();
        var secondId=Guid.NewGuid().ToString();await owner.PostAsJsonAsync("/api/v1/achievements/steam/42/ONE/groups",new EditAchievementGroups(GroupVersion(first),secondId,new("Actions","Expansion",3)));
        await pc.PostAsJsonAsync("/connector/v1/achievements/groups/ack",new AchievementGroupAck("steam","42","ONE",firstId,"applied",firstGroups));
        var pending=(await pc.GetFromJsonAsync<AchievementGroupChange[]>("/connector/v1/achievements/groups"))!.Single();Assert.Equal(secondId,pending.Id);Assert.Equal(firstGroups,pending.Baseline);Assert.Equal("Actions",pending.Desired.Category);
    }
    [Theory]
    [InlineData("http://127.0.0.1/private")]
    [InlineData("https://example.com/icon.jpg")]
    [InlineData("https://shared.fastly.steamstatic.com/steamcommunity/public/images/apps/43/aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa.jpg")]
    [InlineData("https://shared.fastly.steamstatic.com@127.0.0.1/image.jpg")]
    public void AchievementIconFetchCannotUseAnArbitraryHostOrAnotherGame(string uri)=>Assert.Null(AchievementIcons.Canonical("steam","42",uri));
    [Fact]
    public void CurrentAndLegacySteamBadgePathsUseTheSameOfficialCdnWithoutAllowingOtherPaths()
    {
        var hash=new string('a',40);var expected=$"https://shared.fastly.steamstatic.com/community_assets/images/apps/42/{hash}.jpg";
        Assert.Equal(expected,AchievementIcons.Canonical("steam","42",expected));Assert.Equal(expected,AchievementIcons.Canonical("steam","42",$"https://steamcdn-a.akamaihd.net/steamcommunity/public/images/apps/42/{hash}.jpg"));
        Assert.Null(AchievementIcons.Canonical("steam","42",$"https://shared.fastly.steamstatic.com/community_assets/images/apps/43/{hash}.jpg"));
    }
    [Fact]
    public void AchievementAccountsCanAddAConnectedProviderWithoutChangingTrackerScopeOrReplacingAnExistingAccount()
    {
        using var fixture=new WorkspaceCase();fixture.Prepare();var options=new CompanionOptions{DataRoot=fixture.Root};var workspace=new TrackerWorkspace(options);var identity=workspace.Identity;
        AchievementAccountScope.Configure(options,workspace,new(){["steam"]=Approved,["retroachievements"]="connected-ra"});Assert.Equal(identity,workspace.Identity);Assert.False(workspace.Manifest!.Accounts.ContainsKey("retroachievements"));Assert.Equal("connected-ra",AchievementAccountScope.Read(options,workspace)["retroachievements"]);
        Assert.Throws<InvalidDataException>(()=>AchievementAccountScope.Configure(options,workspace,new(){["steam"]=Other,["retroachievements"]="connected-ra"}));
        Assert.Throws<InvalidDataException>(()=>AchievementAccountScope.Configure(options,workspace,new(){["steam"]=Approved,["retroachievements"]="replacement-ra"}));
    }
}
