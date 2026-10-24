using System.Buffers.Binary;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class DeathStrandingChipReaderTests
{
    const int Start=34;
    const string Id=AchievementTrackingCatalog.DeathStrandingChipsId;
    const string Chip52="af6d917f-2d09-064b-a4a4-2809694ea0d6";
    static byte[] Payload()
    {
        var data=new byte[Start+190*12+17];
        Convert.FromHexString("81be0081600901").CopyTo(data,Start-7);
        var keys=DeathStrandingChipReader.References();
        for(int i=0;i<190;i++)
        {
            int p=Start+i*12;
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(p),keys[i]);
            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(p+4),uint.MaxValue);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(p+8),(ushort)i);
        }
        return data;
    }
    static AchievementTrackerSnapshot Read(byte[] data)=>DeathStrandingChipReader.Read(data,"player",DateTime.UtcNow.Ticks,"Fixture","Checked copy.");
    static int ChipPosition()=>Start+Array.IndexOf(DeathStrandingChipReader.References(),1202779670U)*12;
    [Theory]
    [InlineData(0,0)][InlineData(8,0)][InlineData(9,0)][InlineData(25,0)][InlineData(27,1)]
    public void OnlyTerminalRestorationCounts(int flags,int expected)
    {
        var data=Payload();data[ChipPosition()+10]=(byte)flags;
        // A raw hash/flag match outside the table must not change progress.
        var unrelated=new byte[12];BinaryPrimitives.WriteUInt32LittleEndian(unrelated,1202779670U);unrelated[10]=27;
        data=data.Concat(unrelated).ToArray();
        var result=Read(data);Assert.Equal(56,result.Items.Count);
        Assert.Equal(expected,result.Items.Single(i=>i.ItemId==Chip52).Current);
        Assert.Equal(expected,result.Items.Sum(i=>i.Current));
        Assert.All(result.Items,i=>Assert.Equal(TrackerAvailability.Unknown,i.Availability));
    }
    [Theory]
    [InlineData("count")][InlineData("prefix")][InlineData("truncated")][InlineData("duplicate")][InlineData("unknown")]
    [InlineData("sentinel")][InlineData("index")][InlineData("flags")][InlineData("restored-without-pickup")][InlineData("ambiguous")][InlineData("reordered")]
    public void UnsupportedTablesCannotGenerateFalseZeroes(string mutation)
    {
        var data=Payload();int p=Start+189*12;
        switch(mutation)
        {
            case "count":data[Start-6]--;break;
            case "prefix":data[Start-1]--;break;
            case "truncated":data=data[..(Start+190*12-1)];break;
            case "duplicate":data.AsSpan(Start,4).CopyTo(data.AsSpan(p,4));break;
            case "unknown":data[p]++;break;
            case "sentinel":data[p+4]=0;break;
            case "index":data[p+8]--;break;
            case "flags":data[p+11]=1;break;
            case "restored-without-pickup":data[p+10]=2;break;
            case "ambiguous":data=data.Concat(data).ToArray();break;
            case "reordered":var first=data.AsSpan(Start,12).ToArray();data.AsSpan(Start+12,12).CopyTo(data.AsSpan(Start,12));first.CopyTo(data,Start+12);break;
        }
        Assert.Throws<NotSupportedException>(()=>Read(data));
    }
    [Fact]
    public void ChipSourceSetupAndConnectionIndicatorExposeNativeAutomaticSupport()
    {
        var source=new DeathStrandingTrackerSource(Path.GetTempPath());var d=AchievementTrackingCatalog.Builtins().Single(d=>d.Id==Id);
        Assert.True(source.Supports(d));Assert.Contains("restoration",source.Coverage(d));
        source.Validate(new(){AdapterId=source.Id,AdapterVersion="1",Account="76561197960265729",Location=Path.GetTempPath(),GameDirectory=Path.GetTempPath()},d);
        AchievementCollectionAccessTests.OnSta(()=>
        {
            var window=new TrackerSourceWindow(null!,null!,null!,d,"76561197960265729");
            try
            {
                var root=(System.Windows.FrameworkElement)window.Content;root.Measure(new System.Windows.Size(700,760));root.Arrange(new System.Windows.Rect(0,0,700,760));root.UpdateLayout();
                Assert.Contains(Children(root).OfType<System.Windows.Controls.ComboBox>().SelectMany(c=>c.Items.OfType<System.Windows.Controls.ComboBoxItem>()),i=>Equals(i.Tag,source.Id));
            }
            finally { window.Close(); }
        });
    }
    static IEnumerable<System.Windows.DependencyObject> Children(System.Windows.DependencyObject root)
    {
        yield return root;for(int i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Children(System.Windows.Media.VisualTreeHelper.GetChild(root,i)))yield return child;
    }
    [Fact]
    public async Task OptionalPrivateControlsValidatePickupRestorationAndAutomaticReconciliation()
    {
        var manifest=Environment.GetEnvironmentVariable("PIXELVAULT_DS_CHIP_REVIEW");if(string.IsNullOrWhiteSpace(manifest))return;
        var rows=JsonDocument.Parse(File.ReadAllText(manifest)).RootElement.EnumerateArray().ToList();Assert.True(rows.Count>=58);
        foreach(var row in rows)
            Assert.Equal(row.GetProperty("Restored").EnumerateArray().Select(v=>v.GetString()).Order(),Read(File.ReadAllBytes(row.GetProperty("Data").GetString()!)).Items.Where(i=>i.Current==1).Select(i=>i.ItemId).Order());
        var controls=rows.Where(r=>r.GetProperty("Control").GetString()!="").ToDictionary(r=>r.GetProperty("Control").GetString()!);
        var game=Environment.GetEnvironmentVariable("PIXELVAULT_DS_REVIEW_GAME")!;
        var account=JsonDocument.Parse(File.ReadAllText(Environment.GetEnvironmentVariable("PIXELVAULT_TRACKING_REVIEW_SEED")!)).RootElement.GetProperty("Account").GetString()!;
        var worker=Environment.GetEnvironmentVariable("PIXELVAULT_DS_CHIP_WORKER")!;
        var dir=Path.Combine(Path.GetTempPath(),"PV-Chips-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
        var hashes=controls.Values.ToDictionary(r=>r.GetProperty("Encrypted").GetString()!,r=>SHA256.HashData(File.ReadAllBytes(r.GetProperty("Encrypted").GetString()!)));
        try
        {
            var slot=Path.Combine(dir,"saves","manualsave0");Directory.CreateDirectory(slot);var copy=Path.Combine(slot,"checkpoint.dat");
            void Copy(string control){var r=controls[control];File.Copy(r.GetProperty("Encrypted").GetString()!,copy,true);File.SetLastWriteTimeUtc(copy,DateTime.Parse(r.GetProperty("SourceUtc").GetString()!).ToUniversalTime());}
            Copy("before");var store=new AchievementTrackingStore(dir);var source=new DeathStrandingTrackerSource(Path.Combine(dir,"achievement-tracking"),worker);
            var service=new TrackerRefreshService(store,new[]{source});service.Configure(Id,account,source.Id,"1",Path.Combine(dir,"saves"),gameDirectory:game);
            var plan=await service.PrepareAsync(Id,account,default);Assert.Equal(56,plan.Rows.Count);Assert.Equal(56,service.Apply(plan,false,null!));
            store.SaveNote(Id,account,Chip52,"Keep this note");
            store.SaveAutomation(Id,account,store.Source(Id,account).Id,TrackerAutomationMode.SaveChanges,5,false);
            var now=DateTime.UtcNow;using var monitor=new TrackerAutomationMonitor(store,service,()=>now);monitor.SetAccounts(account,"");
            async Task Tick(){now=now.AddSeconds(5);await monitor.TickAsync();now=now.AddSeconds(5);await monitor.TickAsync();}
            await Tick();
            foreach(var control in new[]{"pickup","intermediate","after"})
            {
                Copy(control);await Tick();var states=store.Load(Id,account);
                Assert.Equal(control=="after"?22:21,states.Count(s=>s.Complete));
                Assert.Equal(control=="after"?1:0,states.Single(s=>s.Item.Id==Chip52).Current);
                Assert.Equal("Keep this note",states.Single(s=>s.Item.Id==Chip52).Note);
                Assert.False(store.Automation(Id,account).State.ReviewPending);
            }
            int history=store.History(Id,account).Count;await Tick();Assert.Equal(history,store.History(Id,account).Count);
            store.SetManualProgress(Id,account,Chip52,0);File.SetLastWriteTimeUtc(copy,File.GetLastWriteTimeUtc(copy).AddSeconds(1));await Tick();
            Assert.True(store.Automation(Id,account).State.ReviewPending);Assert.Equal(0,store.Load(Id,account).Single(s=>s.Item.Id==Chip52).Current);
            Assert.All(store.Load(Id,"other"),s=>Assert.Null(s.Current));
            foreach(var p in hashes)Assert.Equal(p.Value,SHA256.HashData(File.ReadAllBytes(p.Key)));
        }
        finally
        {
            var full=Path.GetFullPath(dir);Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),full,StringComparison.OrdinalIgnoreCase);
            for(int i=0;Directory.Exists(full);i++){SqliteConnection.ClearAllPools();try{Directory.Delete(full,true);}catch(IOException)when(i<5){Thread.Sleep(50);}}
        }
    }
}
