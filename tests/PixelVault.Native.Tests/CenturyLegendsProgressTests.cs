using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PixelVault.Tracking;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class CenturyLegendsProgressTests : IDisposable
{
    readonly string root=Path.Combine(Path.GetTempPath(),"PV-Century-Legends-"+Guid.NewGuid().ToString("N"));
    readonly AchievementTrackingStore store;
    readonly AchievementTrackerDefinition definition=CenturyLegendsCatalogue.Definition();
    readonly TrackerSourceBinding binding;
    readonly long date=DateTime.UtcNow.AddDays(-2).Ticks;
    const string Account="76561197960265729";
    public CenturyLegendsProgressTests()
    {
        store=new(root);
        binding=new(){TrackerId=definition.Id,Account=Account,AdapterId=DeathStrandingTrackerSource.AdapterId,AdapterVersion="1",Location=root};
        store.SaveSource(binding);
    }
    CenturyProgress? Progress=>store.Century("steam","1850570",CenturyAchievementCatalog.DeathStrandingLegendsId,Account);
    TrackerRefreshPlan Plan(int count,long? observed=null,bool bound=true,bool unknown=false,string adapter=DeathStrandingTrackerSource.AdapterId)
    {
        var snapshot=new AchievementTrackerSnapshot{TrackerId=definition.Id,Provider="steam",GameId="1850570",Account=Account,
            ObservedUtcTicks=observed??date,Source="Validated orders",Evidence="Complete independent LLL ranks",
            Items=definition.Items.Select((i,n)=>new AchievementTrackerProgress{ItemId=i.Id,Current=unknown&&n==0?null:n<count?1:0}).ToList()};
        string? attempt=null;
        if(bound){attempt=store.StartRefresh(binding);store.FinishAttempt(binding,attempt,TrackerRefreshOutcome.AwaitingReview,"Ready");}
        var json=JsonSerializer.Serialize(snapshot,AchievementTrackingCatalog.Json);
        var fingerprint=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        return store.Preview(json,definition.Id,Account,adapter,"1",fingerprint,bound?binding.Id:null,attempt);
    }
    void Apply(TrackerRefreshPlan plan,bool older=false)=>store.ApplyRefresh(plan,older,[],CancellationToken.None);
    [Fact] public void CompleteCanonicalCatalogueIsLinkedWithoutChangingTheOfficialTracker()
    {
        var actual=store.Definitions().Single(d=>d.Id==definition.Id);
        Assert.True(CenturyLegendsCatalogue.Matches(actual));Assert.Equal(CenturyAchievementCatalog.DeathStrandingLegendsId,actual.AchievementId);
        var official=store.Definitions().Single(d=>d.Id==AchievementTrackingCatalog.DeathStrandingOrdersId);
        Assert.Equal("019",official.AchievementId);Assert.Empty(official.Milestones);
        Assert.Equal(4,official.Items.Count);Assert.All(official.Items,item=>Assert.Equal(20,item.Target));
        Assert.Equal(540,actual.Items.Count);Assert.DoesNotContain(actual.Items,i=>i.Id.EndsWith(":LL"));
        Assert.All(store.Load(actual.Id,Account),i=>Assert.Null(i.Current));Assert.Null(Progress);
    }
    [Fact] public void OnlyTheIncoming540LllStatesCanEarnCenturyCredit()
    {
        foreach(var item in definition.Items)store.SetManualProgress(definition.Id,Account,item.Id,1);
        store.SaveNote(definition.Id,Account,definition.Items[0].Id,"Keep my delivery note");
        Apply(Plan(539));Assert.Equal(539,Progress!.Current);Assert.Equal(540,Progress.Target);Assert.False(Progress.Unlocked);
        Assert.Equal(0,Progress.EarnedPoints);Assert.Equal(540,store.Load(definition.Id,Account).Count(i=>i.Complete));
        Apply(Plan(540,date+TimeSpan.TicksPerHour));var earned=Progress!;
        Assert.True(earned.Unlocked);Assert.Equal(100,earned.EarnedPoints);
        Assert.Equal("Keep my delivery note",store.Load(definition.Id,Account)[0].Note);
        Assert.Null(store.Century("steam","1850570",CenturyAchievementCatalog.DeathStrandingLegendsId,"other"));
        Assert.All(store.Load(AchievementTrackingCatalog.DeathStrandingOrdersId,Account),i=>Assert.Null(i.Current));
        Apply(Plan(500,date+TimeSpan.TicksPerDay));Assert.Equal(earned.UnlockUtc,Progress!.UnlockUtc);
        Apply(Plan(540,date-TimeSpan.TicksPerHour),true);Assert.Equal(500,Progress.Current);
    }
    [Fact] public void UnknownFailedUnboundAndStaleObservationsCannotInventCompletion()
    {
        Apply(Plan(540,bound:false,adapter:"json-import"));Assert.Null(Progress);
        Apply(Plan(100,date+TimeSpan.TicksPerHour));var prior=Progress;
        Apply(Plan(540,date+TimeSpan.TicksPerDay,unknown:true));Assert.Equal(prior,Progress);
        var attempt=store.StartRefresh(binding);store.FinishAttempt(binding,attempt,TrackerRefreshOutcome.Failed,"Unsupported orders");Assert.Equal(prior,Progress);
        var stale=Plan(540,date+2*TimeSpan.TicksPerDay);store.SaveSource(binding);
        Assert.Throws<InvalidOperationException>(()=>Apply(stale));Assert.Equal(prior,Progress);
    }
    [Fact] public void AnUnchangedAutomaticReadStillRecordsTheSeparateCenturyProgress()
    {
        Apply(Plan(48,bound:false,adapter:"json-import"));Assert.Null(Progress);
        store.SaveAutomation(definition.Id,Account,binding.Id,TrackerAutomationMode.SaveChanges,5,false);
        var plan=Plan(48);var settings=store.Automation(definition.Id,Account).Settings;
        Assert.True(store.RecordAutomatic(binding,settings.Id,new(){LastCheckUtc=DateTime.UtcNow.Ticks},plan,TrackerRefreshOutcome.Unchanged,acknowledge:true));
        Assert.Equal(48,Progress!.Current);Assert.Equal(540,Progress.Target);
    }
    public void Dispose(){SqliteConnection.ClearAllPools();Directory.Delete(root,true);}
}
