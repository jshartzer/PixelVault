using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class DeathStrandingOrderReaderTests
{
    const int Start=34;
    const string Id=AchievementTrackingCatalog.DeathStrandingOrdersId;
    static byte[] Payload()
    {
        var data=new byte[Start+DeathStrandingOrderReader.RecordCount*96+17];
        data[Start-3]=0x81;data[Start-2]=0x74;data[Start-1]=4;
        var ids=AchievementTrackingCatalog.Builtins().Single(d=>d.Id==Id).Items.Select(i=>ulong.Parse(i.GroupId)).Distinct().Reverse().ToArray();
        for(int i=0;i<1140;i++)
        {
            int p=Start+i*96;data[p]=8;data[p+2]=1;data[p+4]=1;data[p+24]=2;data[p+25]=255;
            BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(p+16,8),i<ids.Length?ids[i]:0);
        }
        Set(data,ids[0],0x80);Set(data,ids[1],0x100);Set(data,ids[2],0x188);
        return data;
    }
    static int Position(byte[] data,ulong id)=>Enumerable.Range(0,1140).Select(i=>Start+i*96).Single(p=>BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(p+16,8))==id);
    static void Set(byte[] data,ulong id,ulong rank)=>BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(Position(data,id)+88,8),rank);
    static AchievementTrackerSnapshot Read(byte[] data)=>DeathStrandingOrderReader.Read(data,"player",DateTime.UtcNow.Ticks,"Fixture","Checked copy.");
    [Fact]
    public void CountedTableReadsIndependentRanksAndMixedEarnedMasksWithoutUsingDeliveryCounters()
    {
        var data=Payload();var d=AchievementTrackingCatalog.Builtins().Single(d=>d.Id==Id);
        int first=Start;BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(first+26,2),40000);
        var result=Read(data);Assert.Equal(1080,result.Items.Count);Assert.Equal(4,result.Items.Count(i=>i.Current==1));
        Assert.Equal(2,result.Items.Count(i=>i.ItemId.EndsWith(":LL")&&i.Current==1));
        Assert.Equal(2,result.Items.Count(i=>i.ItemId.EndsWith(":LLL")&&i.Current==1));
        Assert.All(result.Items,i=>Assert.Equal(TrackerAvailability.Unknown,i.Availability));
        var llOnly=result.Items.First(i=>i.Current==1&&i.ItemId.EndsWith(":LL")&&result.Items.Single(j=>j.ItemId==i.ItemId+"L").Current==0);
        Assert.Equal(0,result.Items.Single(i=>i.ItemId==llOnly.ItemId+"L").Current);
        Assert.Equal(d.Items.Select(i=>i.Id),result.Items.Select(i=>i.ItemId));
    }
    [Theory]
    [InlineData("count")][InlineData("truncated")][InlineData("duplicate")][InlineData("missing")]
    [InlineData("sentinel")][InlineData("reserved")][InlineData("rank")][InlineData("empty-rank")][InlineData("prefix")][InlineData("id")][InlineData("state")][InlineData("ambiguous")]
    public void UnsupportedOrAmbiguousArraysNeverProduceProgress(string mutation)
    {
        var data=Payload();int empty=Start+1000*96;
        switch(mutation)
        {
            case "count":data[Start-2]--;break;
            case "truncated":data=data[..(Start+1140*96-1)];break;
            case "duplicate":data.AsSpan(Start+16,8).CopyTo(data.AsSpan(Start+96+16,8));break;
            case "missing":BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(Start+16,8),9000000);break;
            case "sentinel":data[empty+25]=0;break;
            case "reserved":data[empty+30]=1;break;
            case "rank":data[Start+89]=2;break;
            case "empty-rank":data[empty+88]=128;break;
            case "prefix":data[empty]=255;break;
            case "id":BinaryPrimitives.WriteUInt64LittleEndian(data.AsSpan(empty+16,8),ulong.MaxValue);break;
            case "state":data[empty+24]=8;break;
            case "ambiguous":data=data.Concat(data).ToArray();break;
        }
        Assert.Throws<NotSupportedException>(()=>Read(data));
    }
    [Fact]
    public void SourceSetupAndIndicatorsExposeTheOrderReaderAlongsideTheValidatedChipReader()
    {
        var source=new DeathStrandingTrackerSource(Path.GetTempPath());var definition=AchievementTrackingCatalog.Builtins().Single(d=>d.Id==Id);
        Assert.True(source.Supports(definition));Assert.True(source.Supports(AchievementTrackingCatalog.Builtins().Single(d=>d.Id==AchievementTrackingCatalog.DeathStrandingChipsId)));
        source.Validate(new(){AdapterId=source.Id,AdapterVersion="1",Account="76561197960265729",Location=Path.GetTempPath(),GameDirectory=Path.GetTempPath()},definition);
        AchievementCollectionAccessTests.OnSta(()=>
        {
            var dialog=new TrackerSourceWindow(null!,null!,null!,definition,"76561197960265729");
            try
            {
                var root=(System.Windows.FrameworkElement)dialog.Content;root.Measure(new System.Windows.Size(700,760));root.Arrange(new System.Windows.Rect(0,0,700,760));root.UpdateLayout();
                Assert.Contains(Descendants(root).OfType<System.Windows.Controls.ComboBox>().SelectMany(c=>c.Items.OfType<System.Windows.Controls.ComboBoxItem>()),i=>Equals(i.Tag,source.Id));
            }
            finally{dialog.Close();}
        });
    }
    static IEnumerable<System.Windows.DependencyObject> Descendants(System.Windows.DependencyObject root)
    {
        yield return root;for(int i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Descendants(System.Windows.Media.VisualTreeHelper.GetChild(root,i)))yield return child;
    }
    [Fact]
    public async Task OptionalPrivateControlsValidateFullTablesAndAutomaticSaveChangeReconciliation()
    {
        var manifest=Environment.GetEnvironmentVariable("PIXELVAULT_DS_ORDER_REVIEW");
        if(string.IsNullOrWhiteSpace(manifest))return;
        var rows=JsonDocument.Parse(File.ReadAllText(manifest)).RootElement.EnumerateArray().ToList();Assert.True(rows.Count>=24);
        foreach(var row in rows)
        {
            var result=Read(File.ReadAllBytes(row.GetProperty("Data").GetString()!));
            foreach(var rank in new[]{"LL","LLL"})
                Assert.Equal(row.GetProperty(rank).EnumerateArray().Select(v=>v.GetString()).Order(),result.Items.Where(i=>i.Current==1&&i.ItemId.EndsWith(":"+rank)).Select(i=>i.ItemId.Split(':')[0]).Order());
        }
        var controls=rows.Where(r=>r.GetProperty("Control").GetString() is "before" or "after").ToDictionary(r=>r.GetProperty("Control").GetString()!);
        var game=Environment.GetEnvironmentVariable("PIXELVAULT_DS_REVIEW_GAME")!;
        var seed=JsonDocument.Parse(File.ReadAllText(Environment.GetEnvironmentVariable("PIXELVAULT_TRACKING_REVIEW_SEED")!));
        var account=seed.RootElement.GetProperty("Account").GetString()!;
        var worker=Environment.GetEnvironmentVariable("PIXELVAULT_DS_ORDER_WORKER")!;
        var dir=Path.Combine(Path.GetTempPath(),"PV-Orders-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        var original=controls["after"].GetProperty("Encrypted").GetString()!;var hash=SHA256.HashData(File.ReadAllBytes(original));
        try
        {
            var slot=Path.Combine(dir,"saves","manualsave0");Directory.CreateDirectory(slot);var copy=Path.Combine(slot,"checkpoint.dat");
            void Copy(string control){var r=controls[control];File.Copy(r.GetProperty("Encrypted").GetString()!,copy,true);File.SetLastWriteTimeUtc(copy,DateTime.Parse(r.GetProperty("SourceUtc").GetString()!).ToUniversalTime());}
            Copy("before");var store=new AchievementTrackingStore(dir);var source=new DeathStrandingTrackerSource(Path.Combine(dir,"achievement-tracking"),worker);
            var service=new TrackerRefreshService(store,new[]{source});service.Configure(Id,account,source.Id,"1",Path.Combine(dir,"saves"),gameDirectory:game);
            var plan=await service.PrepareAsync(Id,account,default);Assert.Equal(1080,plan.Rows.Count);Assert.Equal(1080,service.Apply(plan,false,null!));
            store.SaveNote(Id,account,"205101:LL","Keep this note");
            store.SaveAutomation(Id,account,store.Source(Id,account).Id,TrackerAutomationMode.SaveChanges,5,false);
            var now=DateTime.UtcNow;using var monitor=new TrackerAutomationMonitor(store,service,()=>now);monitor.SetAccounts(account,"");
            await monitor.TickAsync();now=now.AddSeconds(5);await monitor.TickAsync();
            Copy("after");now=now.AddSeconds(5);await monitor.TickAsync();now=now.AddSeconds(5);await monitor.TickAsync();
            Assert.Equal(3,store.Load(Id,account).Count(s=>s.Complete&&s.Item.Milestone=="LL"));
            Assert.Equal(43,store.Load(Id,account).Count(s=>s.Complete&&s.Item.Milestone=="LLL"));
            Assert.Equal("Keep this note",store.Load(Id,account).Single(s=>s.Item.Id=="205101:LL").Note);
            int history=store.History(Id,account).Count;now=now.AddSeconds(5);await monitor.TickAsync();Assert.Equal(history,store.History(Id,account).Count);
            // A changed manual value creates a review barrier; automatic reads cannot replace it.
            store.SetManualProgress(Id,account,"205101:LL",0);File.SetLastWriteTimeUtc(copy,File.GetLastWriteTimeUtc(copy).AddSeconds(1));
            now=now.AddSeconds(5);await monitor.TickAsync();now=now.AddSeconds(5);await monitor.TickAsync();
            Assert.True(store.Automation(Id,account).State.ReviewPending);Assert.Equal(0,store.Load(Id,account).Single(s=>s.Item.Id=="205101:LL").Current);
            Assert.All(store.Load(Id,"other"),s=>Assert.Null(s.Current));Assert.Equal(hash,SHA256.HashData(File.ReadAllBytes(original)));
        }
        finally
        {
            var full=Path.GetFullPath(dir);Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),full,StringComparison.OrdinalIgnoreCase);
            for(int i=0;Directory.Exists(full);i++){SqliteConnection.ClearAllPools();try{Directory.Delete(full,true);}catch(IOException)when(i<5){Thread.Sleep(50);}}
        }
    }
}
