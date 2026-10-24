using System.Buffers.Binary;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class DeathStrandingWorldReaderTests
{
    static byte[] Payload(bool built = true, bool upgraded = true, bool includeChiral = true)
    {
        var facts = DeathStrandingWorldReader.References().Where(f => includeChiral || f.Name != "ldfp_builded_crl_bridge").ToList();
        // Add an unrelated persistent fact so omitted/default target tests retain minimum coverage/count.
        facts.Add(new DeathStrandingWorldReader.Fact { Id = "01234567-89ab-cdef-0123-456789abcdef", Name = "unrelated" });
        var data = Enumerable.Repeat((byte)0xCC, 29 + facts.Count * 17 + 24).ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(25,4), (uint)facts.Count);
        for (int i = 0; i < facts.Count; i++)
        {
            var fact = facts[i]; Guid.Parse(fact.Id).ToByteArray().CopyTo(data,29+i*17);
            data[29+i*17+16] = (byte)((fact.Name == "ldfp_builded_post" && built || fact.Name == "ldfp_lvmax_post" && upgraded || fact.Name == "ldfp_builded_crl_bridge" && built) ? 1 : 0);
        }
        data.AsSpan(29+facts.Count*17,8).Clear(); return data;
    }
    static AchievementTrackerSnapshot Read(byte[] data, string id) => DeathStrandingWorldReader.Read(data,id,"player",DateTime.UtcNow.Ticks,"Fixture","Validated copy.");
    [Fact]
    public void CountedMapSeparatesConstructionUpgradeAndKnownAbsentDefaults()
    {
        var before=Read(Payload(true,false,false),AchievementTrackingCatalog.DeathStrandingBuildId);
        Assert.Equal(13,before.Items.Count); Assert.Single(before.Items,i=>i.Current==1);
        var after=Read(Payload(),AchievementTrackingCatalog.DeathStrandingBuildId); Assert.Equal(2,after.Items.Count(i=>i.Current==1));
        var upgrade=Read(Payload(),AchievementTrackingCatalog.DeathStrandingUpgradeId);Assert.Equal(10,upgrade.Items.Count);Assert.Single(upgrade.Items,i=>i.Current==1);
        Assert.All(upgrade.Items,i=>Assert.Equal(TrackerAvailability.Unknown,i.Availability));
        Assert.Equal(0,Read(Payload(false,false),AchievementTrackingCatalog.DeathStrandingUpgradeId).Items.Count(i=>i.Current==1));
        Assert.Throws<NotSupportedException>(()=>Read(Payload(),AchievementTrackingCatalog.DeathStrandingChipsId));
        Assert.Throws<NotSupportedException>(()=>Read(Payload(),AchievementTrackingCatalog.DeathStrandingOrdersId));
    }
    [Theory]
    [InlineData("count")][InlineData("short-count")][InlineData("boolean")][InlineData("duplicate")][InlineData("marker")][InlineData("truncated")][InlineData("anchor")][InlineData("duplicate-anchor")]
    public void MalformedOrUnsupportedFactTablesNeverProduceObservations(string mutation)
    {
        var data=Payload();int n=(int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(25,4));
        switch(mutation)
        {
            case "count": BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(25,4),uint.MaxValue);break;
            case "short-count": BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(25,4),(uint)n-1);break;
            case "boolean": data[29+16]=2;break;
            case "duplicate": data.AsSpan(29,16).CopyTo(data.AsSpan(29+17,16));break;
            case "marker": data[29+n*17]=1;break;
            case "truncated": data=data[..(29+n*17-1)];break;
            case "anchor":
                var anchor=Guid.Parse(DeathStrandingWorldReader.References().Single(f=>f.Name=="ldfp_builded_post").Id).ToByteArray();
                var pos=data.AsSpan().IndexOf(anchor);data[pos]^=1;break;
            case "duplicate-anchor":
                data=data.Concat(Guid.Parse(DeathStrandingWorldReader.References().Single(f=>f.Name=="ldfp_builded_post").Id).ToByteArray()).ToArray();break;
        }
        Assert.Throws<NotSupportedException>(()=>Read(data,AchievementTrackingCatalog.DeathStrandingBuildId));
    }
    [Fact]
    public void DebugAndRoadFactsCannotAwardConstructionOrUpgradeTargets()
    {
        var data=Payload();var facts=DeathStrandingWorldReader.References();
        foreach(var fact in facts.Where(f=>f.Name.EndsWith("_sample")||f.Name.EndsWith("_road")))
        {var pos=data.AsSpan().IndexOf(Guid.Parse(fact.Id).ToByteArray());Assert.True(pos>=0);data[pos+16]=1;}
        Assert.Equal(2,Read(data,AchievementTrackingCatalog.DeathStrandingBuildId).Items.Count(i=>i.Current==1));
        Assert.Single(Read(data,AchievementTrackingCatalog.DeathStrandingUpgradeId).Items,i=>i.Current==1);
    }
    [Fact]
    public void UnrelatedMapWithoutKnownCoverageIsRejected()
    {
        var data=Payload();var anchor=DeathStrandingWorldReader.References().Single(f=>f.Name=="ldfp_builded_post").Id;
        for(int i=0;i<DeathStrandingWorldReader.References().Count;i++) if(new Guid(data.AsSpan(29+i*17,16))!=Guid.Parse(anchor)) Guid.NewGuid().ToByteArray().CopyTo(data,29+i*17);
        Assert.Throws<NotSupportedException>(()=>Read(data,AchievementTrackingCatalog.DeathStrandingBuildId));
    }
    [Fact]
    public async Task OptionalRealCopiesVerifyAllBoundariesAndWorkerReconciliation()
    {
        var manifest=Environment.GetEnvironmentVariable("PIXELVAULT_DS_WORLD_REVIEW");var game=Environment.GetEnvironmentVariable("PIXELVAULT_DS_REVIEW_GAME");
        if(string.IsNullOrWhiteSpace(manifest)||string.IsNullOrWhiteSpace(game))return;
        var rows=JsonDocument.Parse(File.ReadAllText(manifest)).RootElement.EnumerateArray().ToList();Assert.True(rows.Count>=20);
        foreach(var row in rows)
        {
            var data=File.ReadAllBytes(row.GetProperty("Save").GetString()!);
            Assert.Equal(row.GetProperty("Construction").GetInt32(),Read(data,AchievementTrackingCatalog.DeathStrandingBuildId).Items.Count(i=>i.Current==1));
            Assert.Equal(row.GetProperty("MaximumUpgrade").GetInt32(),Read(data,AchievementTrackingCatalog.DeathStrandingUpgradeId).Items.Count(i=>i.Current==1));
        }
        var validation=Path.GetDirectoryName(manifest)!;var capture=JsonDocument.Parse(File.ReadAllText(Path.Combine(validation,"source-manifest.json"))).RootElement.EnumerateArray().Single(r=>r.GetProperty("Slot").GetString()=="manualsave19");
        var original=capture.GetProperty("Copy").GetString()!;var digest=SHA256.HashData(File.ReadAllBytes(original));
        var seed=Environment.GetEnvironmentVariable("PIXELVAULT_TRACKING_REVIEW_SEED");Assert.False(string.IsNullOrWhiteSpace(seed));
        var account=JsonDocument.Parse(File.ReadAllText(seed!)).RootElement.GetProperty("Account").GetString()!;
        var dir=Path.Combine(Path.GetTempPath(),"PV-WorldReader-"+Guid.NewGuid().ToString("N"));
        var saves=Path.Combine(dir,"relocated","manualsave1");Directory.CreateDirectory(saves);
        var copied=Path.Combine(saves,"checkpoint.dat");File.Copy(original,copied);File.SetLastWriteTimeUtc(copied,File.GetLastWriteTimeUtc(original));
        try
        {
            var store=new AchievementTrackingStore(dir);var source=new DeathStrandingTrackerSource(Path.Combine(dir,"achievement-tracking"));
            var service=new TrackerRefreshService(store,new[]{source});
            foreach(var pair in new[]{(AchievementTrackingCatalog.DeathStrandingBuildId,13,10),(AchievementTrackingCatalog.DeathStrandingUpgradeId,10,1)})
            {
                service.Configure(pair.Item1,account,source.Id,"1",Path.GetDirectoryName(saves)!,"Copied world fixture",game);
                var plan=await service.PrepareAsync(pair.Item1,account,default);Assert.Equal(pair.Item2,plan.Rows.Count);Assert.Equal(pair.Item3,plan.Rows.Count(r=>r.Incoming==1));
                Assert.Equal(pair.Item2,service.Apply(plan,false,null!));
                Assert.Equal(pair.Item3,store.Load(pair.Item1,account).Count(s=>s.Complete));
                Assert.Equal(0,service.Apply(await service.PrepareAsync(pair.Item1,account,default),false,null!));
                var completed=store.Load(pair.Item1,account).First(s=>s.Complete);store.SetManualProgress(pair.Item1,account,completed.Item.Id,0);store.SaveNote(pair.Item1,account,completed.Item.Id,"Keep my note");
                service.Apply(await service.PrepareAsync(pair.Item1,account,default),false,null!);
                var saved=store.Load(pair.Item1,account).Single(s=>s.Item.Id==completed.Item.Id);Assert.Equal(0,saved.Current);Assert.Equal("Keep my note",saved.Note);
                Assert.All(store.Load(pair.Item1,"other"),s=>Assert.Null(s.Current));
            }
            Assert.Equal(digest,SHA256.HashData(File.ReadAllBytes(original)));
            Assert.All(store.Load(AchievementTrackingCatalog.DeathStrandingChipsId,account),s=>Assert.Null(s.Current));
        }
        finally
        {
            var full=Path.GetFullPath(dir);Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),full,StringComparison.OrdinalIgnoreCase);
            for(int i=0;Directory.Exists(full);i++){SqliteConnection.ClearAllPools();try{Directory.Delete(full,true);}catch(IOException)when(i<5){Thread.Sleep(50);}}
        }
    }
}
