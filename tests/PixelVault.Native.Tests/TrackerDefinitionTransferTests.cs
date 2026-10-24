using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class TrackerDefinitionTransferTests : IDisposable
{
    readonly string root=Path.Combine(Path.GetTempPath(),"PV-Definition-"+Guid.NewGuid().ToString("N"));
    public TrackerDefinitionTransferTests()=>Directory.CreateDirectory(root);
    public void Dispose()
    {
        Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),Path.GetFullPath(root),StringComparison.OrdinalIgnoreCase);
        for(int i=0;Directory.Exists(root);i++){SqliteConnection.ClearAllPools();try{Directory.Delete(root,true);}catch(IOException)when(i<5){Thread.Sleep(50);}}
    }
    AchievementTrackingStore Store()=>new(root);
    static AchievementTrackerDefinition Definition()=>new(){Id="custom.portable",Provider="steam",GameId="42",GameTitle="Fixture game",Title="Delivery ranks",AchievementId="rank-achievement",
        Milestones=new(){"LL","LLL"},CategoryTargets=new(){{"Time",2}},Items=new(){
            new(){Id="one:LL",GroupId="one",Milestone="LL",Name="Delivery one",Category="Time",Order=0,Detail="Route notes are shared metadata.",Identifiers=new(){{"Mission","one"}}},
            new(){Id="one:LLL",GroupId="one",Milestone="LLL",Name="Delivery one",Category="Time",Order=1},
            new(){Id="two:LL",GroupId="two",Milestone="LL",Name="Delivery two",Category="Time",Order=2},
            new(){Id="two:LLL",GroupId="two",Milestone="LLL",Name="Delivery two",Category="Time",Order=3}}};
    static GameAchievementsFetchService.AchievementRow Achievement(string id="rank-achievement")=>new(){Provider="steam",ProviderGameId="42",ProviderAchievementId=id,Title="Achievement "+id};
    [Fact]
    public void VersionedAndLegacyDefinitionsRoundTripEveryMetadataFieldWithoutPersonalState()
    {
        foreach(var d in AchievementTrackingCatalog.Builtins().Concat(new[]{Definition()}))
        {
            var json=TrackerDefinitionTransfer.Serialize(d);var read=TrackerDefinitionTransfer.Parse(json);
            Assert.Equal(JsonSerializer.Serialize(d),JsonSerializer.Serialize(read));
            Assert.Equal(JsonSerializer.Serialize(d),JsonSerializer.Serialize(TrackerDefinitionTransfer.Parse(JsonSerializer.Serialize(d))));
            Assert.Contains("PixelVault.TrackerDefinition",json);Assert.DoesNotContain("Account",json);Assert.DoesNotContain("ObservedUtc",json);
        }
    }
    [Fact]
    public void FreshStoreImportPreservesTargetsGroupsLinksAndStartsUnknown()
    {
        var d=TrackerDefinitionTransfer.Parse(TrackerDefinitionTransfer.Serialize(Definition()));var store=Store();
        store.AddDefinition(TrackerDefinitionTransfer.Prepare(d,store.Definitions().Select(x=>x.Id),false,d.Title,d.AchievementId));
        var saved=Store().Definitions().Single(x=>x.Id==d.Id);Assert.Equal("rank-achievement",saved.AchievementId);Assert.Equal(2,saved.CategoryTargets["Time"]);
        Assert.Equal(new[]{"LL","LLL"},saved.Milestones);Assert.All(store.Load(d.Id,"account"),s=>{Assert.Null(s.Current);Assert.Equal("",s.Note);});
        Assert.Null(store.Source(d.Id,"account"));Assert.Empty(store.History(d.Id,"account"));
        store.SetManualProgress(d.Id,"account","one:LLL",1);Assert.Equal(1,AchievementTrackingCatalog.QualifyingRows(store.Load(d.Id,"account"))["Time"]);
    }
    [Fact]
    public void ExplicitCopyPreservesItemIdentitiesWhileOriginalProgressAndNotesStayIntact()
    {
        var d=Definition();var store=Store();store.AddDefinition(d);store.SetManualProgress(d.Id,"account","one:LL",1);store.SaveNote(d.Id,"account","one:LL","Private route");
        Assert.Throws<InvalidDataException>(()=>TrackerDefinitionTransfer.Prepare(d,new[]{d.Id},false,d.Title,d.AchievementId));
        var copy=TrackerDefinitionTransfer.Prepare(d,new[]{d.Id},true,"A separate tracker",null!);store.AddDefinition(copy);
        Assert.NotEqual(d.Id,copy.Id);Assert.Equal(d.Items.Select(x=>x.Id),copy.Items.Select(x=>x.Id));Assert.Null(copy.AchievementId);Assert.Equal("rank-achievement",d.AchievementId);
        Assert.Equal("Private route",store.Load(d.Id,"account")[0].Note);Assert.True(store.Load(d.Id,"account")[0].Complete);
        Assert.All(store.Load(copy.Id,"account"),s=>Assert.Null(s.Current));Assert.Equal("",store.Load(copy.Id,"account")[0].Note);
        Assert.DoesNotContain("Private route",TrackerDefinitionTransfer.Serialize(store.Definitions().Single(x=>x.Id==d.Id)));
    }
    [Theory]
    [InlineData("{\"Format\":\"PixelVault.TrackerDefinition\",\"Version\":2,\"Definition\":null}")]
    [InlineData("{\"Format\":\"Other.Format\",\"Version\":1,\"Definition\":null}")]
    [InlineData("{\"Id\":\"one\",\"id\":\"two\"}")]
    [InlineData("{\"Account\":\"private\",\"TrackerId\":\"custom.portable\",\"Items\":[]}")]
    [InlineData("[]")]
    [InlineData("{unfinished")]
    public void InvalidOrAmbiguousDocumentsCannotCreateATracker(string json)
    {
        Assert.Throws<InvalidDataException>(()=>TrackerDefinitionTransfer.Parse(json));Assert.False(File.Exists(Path.Combine(root,"achievement-tracking","pixelvault-tracking.sqlite")));
    }
    [Fact]
    public void SourceSettingsPersonalItemFieldsInvalidGroupsAndOversizedFilesAreRejected()
    {
        var json=JsonSerializer.Serialize(Definition());Assert.Throws<InvalidDataException>(()=>TrackerDefinitionTransfer.Parse(json.Replace("\"Title\":","\"Source\":\"run a command\",\"Title\":")));
        Assert.Throws<InvalidDataException>(()=>TrackerDefinitionTransfer.Parse(json.Replace("\"Name\":","\"Current\":1,\"Name\":")));
        var d=Definition();d.Items[0].Milestone="wrong";Assert.Throws<InvalidDataException>(()=>TrackerDefinitionTransfer.Serialize(d));
        var path=Path.Combine(root,"oversized.json");using(var file=File.Create(path))file.SetLength(4*1024*1024+1);
        Assert.Throws<InvalidDataException>(()=>TrackerDefinitionTransfer.Read(path));Assert.Throws<InvalidDataException>(()=>TrackerDefinitionTransfer.Parse(new string(' ',4*1024*1024+1)));
        d=Definition();d.Items[0].Name=new string('x',1001);Assert.Throws<InvalidDataException>(()=>TrackerDefinitionTransfer.Serialize(d));
    }
    [Fact]
    public void CountTargetsAndGameScopeSurviveAndCrossGameImportIsBlocked()
    {
        var d=Definition();d.Milestones.Clear();d.CategoryTargets.Clear();d.Items=new(){new(){Id="count",Name="Build five",Target=5}};
        var read=TrackerDefinitionTransfer.Parse(TrackerDefinitionTransfer.Serialize(d));Assert.Equal(5,read.Items.Single().Target);
        TrackerDefinitionTransfer.ValidateContext(read,"steam","42");TrackerDefinitionTransfer.ValidateContext(read,null!,null!);
        Assert.Throws<InvalidDataException>(()=>TrackerDefinitionTransfer.ValidateContext(read,"steam","43"));
        Assert.Throws<InvalidDataException>(()=>TrackerDefinitionTransfer.ValidateContext(read,"retroachievements","42"));
    }
    static IEnumerable<DependencyObject> Children(DependencyObject node)
    {yield return node;foreach(var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())foreach(var found in Children(child))yield return found;}
    static T Field<T>(object o,string name)=>(T)o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(o)!;
    static void Pump(Task task)
    {
        var frame=new DispatcherFrame();var dispatcher=Dispatcher.CurrentDispatcher;
        _=task.ContinueWith(_=>dispatcher.BeginInvoke(new Action(()=>frame.Continue=false)),TaskScheduler.Default);
        if(!task.IsCompleted)Dispatcher.PushFrame(frame);task.GetAwaiter().GetResult();
    }
    static void Render(Window window,string name,int width,int height)
    {
        var output=Environment.GetEnvironmentVariable("PIXELVAULT_TRACKING_REVIEW_OUTPUT");if(string.IsNullOrWhiteSpace(output))return;
        Directory.CreateDirectory(output);var content=(FrameworkElement)window.Content;content.Measure(new Size(width,height));content.Arrange(new Rect(0,0,width,height));content.UpdateLayout();
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(content);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));
        using var file=File.Create(Path.Combine(output,name+".png"));png.Save(file);
    }
    [Fact]
    public void PreviewPrefillsAchievementAndRequiresExplicitCopyWithoutMutatingStore()
    {
        AchievementCollectionAccessTests.OnSta(()=>
        {
            var d=Definition();var dialog=new TrackerDefinitionImportWindow(null!,d,new[]{d.Id},new[]{Achievement(),Achievement("origin")},"steam","42","origin");
            try
            {
                var nodes=Children((DependencyObject)dialog.Content).ToList();var link=nodes.OfType<TrackerAchievementPicker>().Single();Assert.Equal("origin",link.Achievement.ProviderAchievementId);
                var save=nodes.OfType<Button>().Single(b=>Equals(b.Content,"Import definition"));Assert.False(save.IsEnabled);
                Assert.Throws<InvalidDataException>(()=>dialog.BuildDefinition());nodes.OfType<CheckBox>().Single().IsChecked=true;Assert.True(save.IsEnabled);
                var result=dialog.BuildDefinition();Assert.NotEqual(d.Id,result.Id);Assert.Equal("origin",result.AchievementId);Assert.Equal("rank-achievement",d.AchievementId);
                link.SelectedIndex=0;Assert.Null(dialog.BuildDefinition().AchievementId);
                Render(dialog,"tracker-definition-import",712,670);Render(dialog,"tracker-definition-import-narrow",492,450);
                Assert.False(File.Exists(Path.Combine(root,"achievement-tracking","pixelvault-tracking.sqlite")));
            }
            finally{dialog.Close();}
        });
    }
    [Fact]
    public void SharedImportUiCancelsThenCreatesAccountFreeDefinitionAndPreservesDrafts()
    {
        var path=Path.Combine(root,"definition.json");File.WriteAllText(path,TrackerDefinitionTransfer.Serialize(Definition()));var store=Store();
        AchievementCollectionAccessTests.OnSta(()=>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var window=new AchievementTrackerWindow(null!,store,"", "",provider:"steam",gameId:"42",achievementId:"origin",achievements:new[]{Achievement("origin")});
            try
            {
                Pump(window.LoadDefinitionsAsync());var count=store.Definitions().Count;window.ShowDefinitionImport=_=>false;
                Pump(window.ImportDefinitionAsync(path));Assert.Equal(count,store.Definitions().Count);
                window.ShowDefinitionImport=dialog=>{Assert.Equal("origin",dialog.BuildDefinition().AchievementId);return true;};
                Pump(window.ImportDefinitionAsync(path));var saved=store.Definitions().Single(d=>d.Id==Definition().Id);Assert.Equal("origin",saved.AchievementId);
                Assert.All(store.Load(saved.Id,""),s=>Assert.Null(s.Current));Assert.Null(store.Source(saved.Id,""));
                var menu=Children((DependencyObject)window.Content).OfType<Button>().Single(b=>Equals(b.Content,"Import tracker…")).ContextMenu;
                Assert.Contains(menu.Items.OfType<MenuItem>(),i=>Equals(i.Header,"Definition JSON…"));
                Render(window,"tracker-definition-imported-780",732,592);
                Field<TextBox>(window,"notes").Text="An unsaved route";window.ShowDefinitionImport=_=>throw new Exception("Do not open over unsaved draft");
                Pump(window.ImportDefinitionAsync(path));Assert.Equal("An unsaved route",Field<TextBox>(window,"notes").Text);
                Assert.Equal(count+1,store.Definitions().Count);
            }
            finally{window.Close();}
        });
    }
}
