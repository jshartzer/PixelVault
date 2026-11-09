using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace PixelVault.Companion.Tests;

public sealed partial class CompanionTests
{
    [Fact]
    public async Task CenturyCargoShowsKilogramsAndAwardsOnlyAt50000()
    {
        using var fixture = new WorkspaceCase(); fixture.Prepare(); using var host = new Host(false, root: fixture.Root);
        using var owner = host.Client(); await Setup(host, owner);
        var workspace = host.Services.GetRequiredService<TrackerWorkspace>(); var repo = workspace.Repository;
        var definition = PixelVault.Tracking.CenturyCargoGoal.Definition(); var account = workspace.Manifest!.Accounts["steam"];
        var source = new PixelVaultNative.TrackerSourceBinding { TrackerId = definition.Id, Account = account,
            AdapterId = "death-stranding-dc.fabrication", AdapterVersion = "1", Location = fixture.Root };
        repo.SaveSource(source);
        void Observe(int kg)
        {
            var snapshot = new PixelVaultNative.AchievementTrackerSnapshot { TrackerId = definition.Id, Provider = "steam", GameId = "1850570", Account = account,
                ObservedUtcTicks = DateTime.UtcNow.AddMinutes(-1).Ticks + kg, Source = "Delivered cargo",
                Items = new() { new() { ItemId = PixelVault.Tracking.CenturyCargoGoal.ItemId, Current = kg } } };
            var attempt = repo.StartRefresh(source); repo.FinishAttempt(source, attempt, PixelVaultNative.TrackerRefreshOutcome.AwaitingReview, "Ready");
            repo.ApplyRefresh(repo.Preview(JsonSerializer.Serialize(snapshot), definition.Id, account, source.AdapterId, "1",
                new string(kg >= 50000 ? 'B' : 'A', 64), source.Id, attempt), false, [], CancellationToken.None);
        }
        var id = PixelVault.Tracking.CenturyAchievementCatalog.DeathStrandingCargoId;
        var path = "/api/v1/achievements/steam/1850570/" + id;
        Observe(49999);
        var detail = (await owner.GetFromJsonAsync<JsonElement>(path)).GetProperty("achievement");
        Assert.Equal(49999, detail.GetProperty("counterCurrent").GetInt32()); Assert.Equal(50000, detail.GetProperty("counterTarget").GetInt32());
        Assert.False(detail.GetProperty("unlocked").GetBoolean());
        Assert.Equal("The Weight of a Nation", detail.GetProperty("title").GetString());
        var cargoTracker = await owner.GetFromJsonAsync<JsonElement>("/api/v1/trackers/" + definition.Id);
        Assert.Equal(50000, cargoTracker.GetProperty("totals").GetProperty("total").GetInt32());
        Assert.Equal(1, cargoTracker.GetProperty("totals").GetProperty("aggregateTotal").GetInt32());
        Assert.Equal(0, cargoTracker.GetProperty("totals").GetProperty("aggregateCompleted").GetInt32());
        var gameTotals = await owner.GetFromJsonAsync<JsonElement>("/api/v1/games/steam/1850570");
        Assert.Equal(808, gameTotals.GetProperty("total").GetInt32());
        Assert.Equal(0, gameTotals.GetProperty("completed").GetInt32());
        Assert.DoesNotContain("save", detail.GetProperty("description").GetString()!, StringComparison.OrdinalIgnoreCase);
        Observe(50001);
        detail = (await owner.GetFromJsonAsync<JsonElement>(path)).GetProperty("achievement");
        Assert.True(detail.GetProperty("unlocked").GetBoolean()); Assert.Equal(50000, detail.GetProperty("counterCurrent").GetInt32());
        cargoTracker = await owner.GetFromJsonAsync<JsonElement>("/api/v1/trackers/" + definition.Id);
        Assert.Equal(1, cargoTracker.GetProperty("totals").GetProperty("aggregateCompleted").GetInt32());
        Assert.Equal(100, repo.Century("steam", "1850570", id, account).EarnedPoints);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(path + "/unlocked-icon")).StatusCode);
        Assert.All(repo.Load(PixelVault.Tracking.CenturyLegendsCatalogue.TrackerId, account), row => Assert.Null(row.Current));
    }

    [Fact]
    public async Task CenturyLegendsHasItsOwn540OrderTrackerAndRequiresEveryLll()
    {
        using var fixture=new WorkspaceCase();fixture.Prepare();using var host=new Host(false,root:fixture.Root);using var owner=host.Client();await Setup(host,owner);
        var workspace=host.Services.GetRequiredService<TrackerWorkspace>();var repo=workspace.Repository;
        var definition=PixelVault.Tracking.CenturyLegendsCatalogue.Definition();
        var account=workspace.Manifest!.Accounts["steam"];
        var source=new PixelVaultNative.TrackerSourceBinding{TrackerId=definition.Id,Account=account,AdapterId="death-stranding-dc.fabrication",AdapterVersion="1",Location=fixture.Root};
        repo.SaveSource(source);
        void Observe(int count)
        {
            var snapshot=new PixelVaultNative.AchievementTrackerSnapshot{TrackerId=definition.Id,Provider="steam",GameId="1850570",Account=account,
                ObservedUtcTicks=DateTime.UtcNow.AddMinutes(-1).Ticks+count,Source="Standard orders",
                Items=definition.Items.Select((i,n)=>new PixelVaultNative.AchievementTrackerProgress{ItemId=i.Id,Current=n<count?1:0}).ToList()};
            var attempt=repo.StartRefresh(source);repo.FinishAttempt(source,attempt,PixelVaultNative.TrackerRefreshOutcome.AwaitingReview,"Ready");
            repo.ApplyRefresh(repo.Preview(JsonSerializer.Serialize(snapshot,PixelVaultNative.AchievementTrackingCatalog.Json),definition.Id,account,source.AdapterId,"1",new string(count==540?'B':'A',64),source.Id,attempt),false,[],CancellationToken.None);
        }
        Observe(539);
        var path="/api/v1/achievements/steam/1850570/"+PixelVault.Tracking.CenturyAchievementCatalog.DeathStrandingLegendsId;
        var detail=await owner.GetFromJsonAsync<JsonElement>(path);
        Assert.Equal("Nothing Left to Deliver",detail.GetProperty("achievement").GetProperty("title").GetString());
        Assert.Equal(539,detail.GetProperty("achievement").GetProperty("counterCurrent").GetInt32());
        Assert.Equal(540,detail.GetProperty("achievement").GetProperty("counterTarget").GetInt32());
        Assert.False(detail.GetProperty("achievement").GetProperty("unlocked").GetBoolean());
        Assert.Equal(PixelVault.Tracking.CenturyAchievementCatalog.DeathStrandingLegendsId,repo.Definitions().Single(d=>d.Id==definition.Id).AchievementId);
        Assert.Equal(HttpStatusCode.OK,(await owner.GetAsync(path+"/unlocked-icon")).StatusCode);
        Observe(540);
        detail=await owner.GetFromJsonAsync<JsonElement>(path);Assert.True(detail.GetProperty("achievement").GetProperty("unlocked").GetBoolean());
        Assert.Equal(100,repo.Century("steam","1850570",PixelVault.Tracking.CenturyAchievementCatalog.DeathStrandingLegendsId,account).EarnedPoints);
        Assert.Empty(host.Services.GetRequiredService<AchievementStore>().CompletionSummaries());
        var official=repo.Definitions().Single(d=>d.Id==PixelVaultNative.AchievementTrackingCatalog.DeathStrandingOrdersId);
        Assert.Equal("019",official.AchievementId);Assert.Equal(4,official.Items.Count);
        Assert.All(repo.Load(official.Id,account),s=>Assert.Null(s.Current));
    }

    [Fact]
    public async Task CenturyEmailUsesTheWholeCatalogueAndKeepsUnknownEntryIncomplete()
    {
        using var fixture=new WorkspaceCase();fixture.Prepare();using var host=new Host(false,root:fixture.Root);using var owner=host.Client();await Setup(host,owner);
        var workspace=host.Services.GetRequiredService<TrackerWorkspace>();var repo=workspace.Repository;
        const string id=PixelVault.Tracking.CenturyAchievementCatalog.DeathStrandingMailsId;
        var definition=new PixelVaultNative.AchievementTrackerDefinition {Id=PixelVault.Tracking.CenturyMailCatalogue.TrackerId,Provider="steam",GameId="1850570",GameTitle="Death Stranding Director's Cut",Title="Email catalogue",
            Items=PixelVault.Tracking.CenturyMailCatalogue.ItemIds.Select(i=>new PixelVaultNative.AchievementTrackerItem {Id=i,Name=i,Target=1}).ToList()};
        repo.AddDefinition(definition);repo.SetAchievementLink(definition.Id,id);var account=workspace.Manifest!.Accounts["steam"];
        var binding=new PixelVaultNative.TrackerSourceBinding {TrackerId=definition.Id,Account=account,AdapterId="death-stranding-dc.fabrication",AdapterVersion="1",Location=fixture.Root};repo.SaveSource(binding);
        var count=0;var snapshot=new PixelVaultNative.AchievementTrackerSnapshot {TrackerId=definition.Id,Provider="steam",GameId="1850570",Account=account,ObservedUtcTicks=DateTime.UtcNow.AddMinutes(-1).Ticks,Source="Complete mail catalogue",
            Items=definition.Items.Select(i=>new PixelVaultNative.AchievementTrackerProgress {ItemId=i.Id,Current=i.Id==PixelVault.Tracking.CenturyMailCatalogue.UnmappedId?null:count++<144?1:0}).ToList()};
        var attempt=repo.StartRefresh(binding);repo.FinishAttempt(binding,attempt,PixelVaultNative.TrackerRefreshOutcome.AwaitingReview,"Ready");
        repo.ApplyRefresh(repo.Preview(JsonSerializer.Serialize(snapshot,PixelVaultNative.AchievementTrackingCatalog.Json),definition.Id,account,binding.AdapterId,"1",new string('A',64),binding.Id,attempt),false,[],CancellationToken.None);
        var game=await owner.GetFromJsonAsync<JsonElement>("/api/v1/achievements/steam/1850570");
        var mail=game.GetProperty("century").GetProperty("definitions").EnumerateArray().Single(d=>d.GetProperty("id").GetString()==id);
        Assert.True(mail.GetProperty("progressKnown").GetBoolean());Assert.False(mail.GetProperty("unlocked").GetBoolean());
        Assert.Equal(140,mail.GetProperty("counterCurrent").GetInt32());Assert.Equal(236,mail.GetProperty("counterTarget").GetInt32());
        Assert.Equal(0,mail.GetProperty("earnedPoints").GetInt32());Assert.Empty(game.GetProperty("rows").EnumerateArray());
        Assert.Equal(1,repo.Century("steam","1850570",id,account).Unknown);
        Assert.Empty(host.Services.GetRequiredService<AchievementStore>().CompletionSummaries());
        var detail=await owner.GetFromJsonAsync<JsonElement>("/api/v1/achievements/steam/1850570/"+id);
        Assert.Equal(236,detail.GetProperty("achievement").GetProperty("counterTarget").GetInt32());
        Assert.Equal(HttpStatusCode.OK,(await owner.GetAsync("/api/v1/achievements/steam/1850570/"+id+"/unlocked-icon")).StatusCode);
    }

    [Fact]
    public async Task CenturyRoadProgressUsesOnlyVerifiedAccountObservationsAndEarnedArtwork()
    {
        using var fixture = new WorkspaceCase(); fixture.Prepare();
        using var host = new Host(false, root: fixture.Root); using var owner = host.Client(); await Setup(host, owner);
        var workspace = host.Services.GetRequiredService<TrackerWorkspace>(); var repo = workspace.Repository;
        const string id = "century.death-stranding-dc.all-roads";
        var definition = new PixelVaultNative.AchievementTrackerDefinition { Id = "custom.verified-roads", Provider = "steam", GameId = "1850570", GameTitle = "Death Stranding Director's Cut", Title = "Roads",
            Items = Enumerable.Range(1,18).Select(i=>"UC Route 23-"+i).Concat(Enumerable.Range(1,33).Select(i=>"UC Route 41-"+i))
                .Select((name,i)=>new PixelVaultNative.AchievementTrackerItem {Id="road-"+i,Name=name,Target=1}).ToList() };
        repo.AddDefinition(definition);
        var account=workspace.Manifest!.Accounts["steam"];
        var binding=new PixelVaultNative.TrackerSourceBinding {TrackerId=definition.Id,Account=account,AdapterId="death-stranding-dc.fabrication",AdapterVersion="1",Location=fixture.Root};
        repo.SaveSource(binding);
        var observed=DateTime.UtcNow.AddDays(-1).Ticks;
        void Observe(int count)
        {
            var snapshot=new PixelVaultNative.AchievementTrackerSnapshot {TrackerId=definition.Id,Provider="steam",GameId="1850570",Account=account,ObservedUtcTicks=observed++,Source="Verified save",
                Items=definition.Items.Select((item,i)=>new PixelVaultNative.AchievementTrackerProgress {ItemId=item.Id,Current=i<count?1:0}).ToList()};
            var attempt=repo.StartRefresh(binding);repo.FinishAttempt(binding,attempt,PixelVaultNative.TrackerRefreshOutcome.AwaitingReview,"Ready");
            var plan=repo.Preview(JsonSerializer.Serialize(snapshot,PixelVaultNative.AchievementTrackingCatalog.Json),definition.Id,account,binding.AdapterId,binding.AdapterVersion,new string(count==51?'B':'A',64),binding.Id,attempt);
            repo.ApplyRefresh(plan,false,[],CancellationToken.None);
        }
        Observe(28);
        const string path="/api/v1/achievements/steam/1850570";
        var game=await owner.GetFromJsonAsync<JsonElement>(path);
        var road=game.GetProperty("century").GetProperty("definitions").EnumerateArray().Single(d=>d.GetProperty("id").GetString()==id);
        Assert.True(road.GetProperty("progressKnown").GetBoolean()); Assert.False(road.GetProperty("unlocked").GetBoolean());
        Assert.Equal(28,road.GetProperty("counterCurrent").GetInt32());Assert.Equal(51,road.GetProperty("counterTarget").GetInt32());
        Assert.Empty(game.GetProperty("rows").EnumerateArray());
        var detail=await owner.GetFromJsonAsync<JsonElement>(path+"/"+id);
        Assert.Equal(28,detail.GetProperty("achievement").GetProperty("counterCurrent").GetInt32());
        Observe(51);
        detail=await owner.GetFromJsonAsync<JsonElement>(path+"/"+id);
        Assert.True(detail.GetProperty("achievement").GetProperty("unlocked").GetBoolean());Assert.True(detail.GetProperty("achievement").GetProperty("unlockUtc").GetInt64()>0);
        Assert.Equal(100,repo.Century("steam","1850570",id,account).EarnedPoints);
        Assert.Equal(await owner.GetByteArrayAsync(path+"/"+id+"/unlocked-icon"),await owner.GetByteArrayAsync(path+"/"+id+"/icon"));
        Assert.Empty(host.Services.GetRequiredService<AchievementStore>().CompletionSummaries());
    }

    [Fact]
    public async Task CenturyRoadDefinitionIsAccountScopedAndDoesNotInventProviderProgress()
    {
        using var fixture = new WorkspaceCase(); fixture.Prepare();
        using var host = new Host(false, root: fixture.Root); using var owner = host.Client(); await Setup(host, owner);
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client().GetAsync("/api/v1/achievements/steam/1850570")).StatusCode);
        var before = host.Services.GetRequiredService<AchievementStore>().CompletionSummaries();
        var game = await owner.GetFromJsonAsync<JsonElement>("/api/v1/achievements/steam/1850570");
        Assert.Equal(0, game.GetProperty("rows").GetArrayLength());
        var century = game.GetProperty("century");
        Assert.Equal("Centurian Achievements", century.GetProperty("group").GetString());
        Assert.Equal(5, century.GetProperty("planned").GetInt32());
        Assert.Equal("Awaiting save connection", century.GetProperty("status").GetString());
        var definitions = century.GetProperty("definitions").EnumerateArray().ToArray(); Assert.Equal(5, definitions.Length);
        var roads = definitions.Single(d => d.GetProperty("id").GetString() == "century.death-stranding-dc.all-roads");
        var interviews = definitions.Single(d => d.GetProperty("id").GetString() == "century.death-stranding-dc.all-interviews");
        Assert.Equal("The Whole Story", interviews.GetProperty("title").GetString()); Assert.Equal(100, interviews.GetProperty("points").GetInt32());
        Assert.Equal("century.death-stranding-dc.all-roads", roads.GetProperty("id").GetString());
        Assert.Equal("All Roads Lead Home", roads.GetProperty("title").GetString());
        Assert.Equal(100, roads.GetProperty("points").GetInt32());
        Assert.False(roads.GetProperty("progressKnown").GetBoolean()); Assert.False(roads.GetProperty("unlocked").GetBoolean());
        Assert.Equal(before.Count, host.Services.GetRequiredService<AchievementStore>().CompletionSummaries().Count);
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync("/api/v1/achievements/steam/1850570/century.death-stranding-dc.all-roads")).StatusCode);
        var other = await owner.GetFromJsonAsync<JsonElement>("/api/v1/achievements/steam/42");
        Assert.Empty(other.GetProperty("century").GetProperty("definitions").EnumerateArray());
        const string art = "/api/v1/century/steam/1850570/century.death-stranding-dc.all-roads/icon";
        Assert.Equal(HttpStatusCode.Unauthorized, (await host.Client().GetAsync(art + "?state=locked")).StatusCode);
        var locked = await owner.GetAsync(art + "?state=locked"); var unlocked = await owner.GetAsync(art + "?state=unlocked");
        Assert.Equal("image/png", locked.Content.Headers.ContentType!.MediaType);
        var lockedBytes = await locked.Content.ReadAsByteArrayAsync(); var unlockedBytes = await unlocked.Content.ReadAsByteArrayAsync();
        Assert.False(lockedBytes.SequenceEqual(unlockedBytes));
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(art.Replace("1850570", "42") + "?state=locked")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(art + "?state=other")).StatusCode);
        const string normal = "/api/v1/achievements/steam/1850570/century.death-stranding-dc.all-roads";
        var standardLocked = await owner.GetByteArrayAsync(normal + "/icon");
        var trackerArt = await owner.GetByteArrayAsync(normal + "/unlocked-icon");
        Assert.True(lockedBytes.SequenceEqual(standardLocked)); Assert.True(unlockedBytes.SequenceEqual(trackerArt));
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync(normal.Replace("1850570", "42") + "/unlocked-icon")).StatusCode);
        const string interviewsPath = "/api/v1/achievements/steam/1850570/century.death-stranding-dc.all-interviews";
        Assert.Equal(HttpStatusCode.OK, (await owner.GetAsync(interviewsPath)).StatusCode);
        var interviewsLocked = await owner.GetByteArrayAsync(interviewsPath + "/icon");
        Assert.True(lockedBytes.SequenceEqual(interviewsLocked));
        var interviewsUnlocked = await owner.GetByteArrayAsync(interviewsPath + "/unlocked-icon");
        Assert.False(interviewsUnlocked.SequenceEqual(unlockedBytes));
        host.Services.GetRequiredService<ConnectorSyncState>().EnableLibraryAndAutomatic();
        var reserved = AchievementFixture(host.Services.GetRequiredService<TrackerWorkspace>().Identity);
        Assert.Throws<InvalidDataException>(() => host.Services.GetRequiredService<AchievementStore>().Receive(reserved with
        { Rows = reserved.Rows.Take(1).Select(r => r with { Id = "century.death-stranding-dc.all-roads" }).ToArray() }));
    }
}
