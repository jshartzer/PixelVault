using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Data.Sqlite;
using PixelVaultNative;
using Xunit;
using static PixelVaultNative.Tests.AchievementCollectionAccessTests;

namespace PixelVaultNative.Tests;

public sealed class AchievementInlineTrackerTests : IDisposable
{
    readonly string dir=Path.Combine(Path.GetTempPath(),"PV-Inline-"+Guid.NewGuid().ToString("N"));
    const string Account="76561197960265729";
    const string Chips=AchievementTrackingCatalog.DeathStrandingChipsId;
    AchievementTrackingStore Store()=>new(dir);
    static AchievementTrackerDefinition Custom(string id="custom.inline",string achievement="ONE",int target=1)=>new(){Id=id,Provider="steam",GameId="123",GameTitle="Fixture",Title=id,AchievementId=achievement,Items=new(){new(){Id="a",Name="First item",Target=target,Detail="Useful detail"},new(){Id="b",Name="Second item"}}};
    sealed class Native : ITrackerDataSource,ITrackerGameFileSource
    {
        public string Id=>DeathStrandingTrackerSource.AdapterId;
        public string Version=>"1";
        public TrackerSourceCapabilities Capabilities=>TrackerSourceCapabilities.Refresh|TrackerSourceCapabilities.ExternalObservationTime;
        internal int Completed,Reads;long observed=DateTime.UtcNow.Ticks;
        public bool Supports(AchievementTrackerDefinition d)=>true;
        public string Coverage(AchievementTrackerDefinition d)=>"Fixture game files";
        public void Validate(TrackerSourceBinding b,AchievementTrackerDefinition d){}
        public Task<TrackerSourceObservation> ObserveAsync(TrackerSourceBinding b,AchievementTrackerDefinition d,CancellationToken token)
        {
            Reads++;var snapshot=new AchievementTrackerSnapshot{TrackerId=d.Id,Provider=d.Provider,GameId=d.GameId,Account=b.Account,ObservedUtcTicks=++observed,Source="Fixture game",Evidence="Fixture reader",Items=d.Items.Select((item,i)=>new AchievementTrackerProgress{ItemId=item.Id,Current=i<Completed?1:0}).ToList()};
            var json=JsonSerializer.Serialize(snapshot,AchievementTrackingCatalog.Json);
            return Task.FromResult(new TrackerSourceObservation{SnapshotJson=json,Fingerprint=Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json)))});
        }
    }
    TrackerRefreshService NativeService(AchievementTrackingStore store,Native reader,string tracker=Chips)
    {
        var service=new TrackerRefreshService(store,new[]{reader});service.Configure(tracker,Account,reader.Id,"1",Path.GetFullPath(dir),gameDirectory:Path.GetFullPath(dir));return service;
    }
    static void Observe(TrackerRefreshService service,string tracker=Chips)=>service.Apply(service.PrepareAsync(tracker,Account,default).GetAwaiter().GetResult(),false,null!);
    static AchievementTrackerState First(AchievementTrackingStore store)=>store.Load(Chips,Account)[0];
    [Fact]
    public void ManualCheckoffsUseTheTargetAndRetainNotesAccountsAndUndo()
    {
        var store=Store();store.AddDefinition(Custom(target:5));store.SaveNote("custom.inline",Account,"a","Keep this note");
        store.SetInlineCompletion("custom.inline",Account,"a",true);var item=store.Load("custom.inline",Account)[0];Assert.Equal(5,item.Current);Assert.Equal("Keep this note",item.Note);
        var history=store.History("custom.inline",Account).First();store.SetInlineCompletion("custom.inline",Account,"a",false);Assert.Equal(0,store.Load("custom.inline",Account)[0].Current);
        store.Undo("custom.inline",Account,store.History("custom.inline",Account).First().Id);Assert.Equal(5,store.Load("custom.inline",Account)[0].Current);
        Assert.All(store.Load("custom.inline","other"),s=>Assert.Null(s.Current));Assert.Throws<InvalidDataException>(()=>store.SetInlineCompletion("custom.inline","","a",true));
        Assert.Equal("Keep this note",store.Load("custom.inline",Account)[0].Note);
    }
    [Fact]
    public void NativeCompletionIsProtectedAfterNotesManualOverridesAndSourceRemoval()
    {
        var store=Store();var reader=new Native{Completed=1};Observe(NativeService(store,reader));var item=First(store);
        for(int i=0;i<105;i++)store.SaveNote(Chips,Account,item.Item.Id,"Note "+i);
        store.SetManualProgress(Chips,Account,item.Item.Id,0);store.RemoveSource(Chips,Account);
        Assert.Contains(item.Item.Id,store.InlineGameCompletions(Chips,Account,store.Load(Chips,Account)));
        Assert.Throws<InvalidOperationException>(()=>store.SetInlineCompletion(Chips,Account,item.Item.Id,false));
        store.SetInlineCompletion(Chips,Account,item.Item.Id,true);var restored=First(store);Assert.True(restored.Complete);Assert.Null(restored.ManualCurrent);Assert.Equal("Note 104",restored.Note);
        int count=store.History(Chips,Account).Count;store.SetInlineCompletion(Chips,Account,item.Item.Id,true);Assert.Equal(count,store.History(Chips,Account).Count);
        Assert.Throws<InvalidOperationException>(()=>store.SetInlineCompletion(Chips,Account,item.Item.Id,false));
    }
    [Fact]
    public void ExternalImportsCannotClaimNativeProvenanceAndUndoRestoresThePreviousOrigin()
    {
        var store=Store();var reader=new Native{Completed=1};Observe(NativeService(store,reader));var first=First(store);
        var snapshot=new AchievementTrackerSnapshot{TrackerId=Chips,Provider="steam",GameId="1850570",Account=Account,Source="Fixture game",Evidence="Fixture reader",ObservedUtcTicks=DateTime.UtcNow.AddSeconds(1).Ticks,Items=new(){new(){ItemId=first.Item.Id,Current=1}}};
        store.Import(JsonSerializer.Serialize(snapshot,AchievementTrackingCatalog.Json),Account,Chips);
        Assert.DoesNotContain(first.Item.Id,store.InlineGameCompletions(Chips,Account,store.Load(Chips,Account)));
        var imported=store.History(Chips,Account).First(h=>h.Kind=="import");
        store.SetInlineCompletion(Chips,Account,first.Item.Id,false);Assert.Equal(0,First(store).Current);
        store.Undo(Chips,Account,imported.Id);Assert.Contains(first.Item.Id,store.InlineGameCompletions(Chips,Account,store.Load(Chips,Account)));
        Assert.Throws<InvalidOperationException>(()=>store.SetInlineCompletion(Chips,Account,first.Item.Id,false));
    }
    [Fact]
    public void CollapsedListsAreScopedReadOnlyAndMilestonesUseCompactIndependentCheckboxes()
    {
        OnSta(()=>{
            var store=Store();var definition=Custom();definition.Items=TrackerMilestones.Expand(definition.Items,new[]{"Found","Perfect"});definition.Milestones=new(){"Found","Perfect"};store.AddDefinition(definition);store.AddDefinition(Custom("other","TWO"));
            var control=new AchievementInlineTrackers();control.Bind(store,"steam","123","ONE",Account);Assert.Single(control.Children);var expand=(Expander)control.Children[0];Assert.False(expand.IsExpanded);
            Assert.All(store.Load(definition.Id,Account),s=>Assert.Null(s.Current));Assert.Empty(store.History(definition.Id,Account));
            expand.IsExpanded=true;Layout(control,600);var list=Descendants(control).OfType<ListBox>().Single();Assert.Equal(2,list.Items.Count);
            var boxes=Descendants(control).OfType<CheckBox>().ToList();Assert.Equal(4,boxes.Count);Assert.All(boxes,b=>Assert.True(b.FontSize<=12.5));
            var found=boxes.Single(b=>Equals(b.Tag,definition.Items[0].Id));Click(found,true);Layout(control,600);
            Assert.Equal(1,store.Load(definition.Id,Account)[0].Current);Assert.Null(store.Load(definition.Id,Account)[1].Current);
            Click(Descendants(control).OfType<CheckBox>().Single(b=>Equals(b.Tag,definition.Items[0].Id)),false);Assert.Equal(0,store.Load(definition.Id,Account)[0].Current);
            control.Bind(store,"steam","123","TWO",Account);Assert.Single(control.Children);Assert.False(((Expander)control.Children[0]).IsExpanded);
            control.Bind(store,"steam","456","ONE",Account);Assert.Empty(control.Children);Assert.Equal(Visibility.Collapsed,control.Visibility);
        });
    }
    [Fact]
    public void StaleCheckboxCannotUncheckAnObservationThatArrivedFromGameData()
    {
        OnSta(()=>{
            var store=Store();var reader=new Native();var service=NativeService(store,reader);Observe(service);var item=First(store);store.SetInlineCompletion(Chips,Account,item.Item.Id,true);
            var control=new AchievementInlineTrackers();control.Bind(store,"steam","1850570","035",Account);((Expander)control.Children[0]).IsExpanded=true;Layout(control,650);
            var old=Descendants(control).OfType<CheckBox>().Single(b=>Equals(b.Tag,item.Item.Id));Assert.True(old.IsEnabled);Assert.True(old.IsChecked);
            int reads=reader.Reads;reader.Completed=1;Observe(service);Click(old,false);Layout(control,650);
            var current=Descendants(control).OfType<CheckBox>().Single(b=>Equals(b.Tag,item.Item.Id));Assert.True(current.IsChecked);Assert.False(current.IsEnabled);Assert.True(First(store).Complete);
            Assert.Equal(reads+1,reader.Reads); // UI reads saved observations, never the game source.
        });
    }
    [Fact]
    public void GuideSelectionResetsTheLinkedSectionAndPreservesUnsavedGuideWork()
    {
        OnSta(()=>{
            var store=Store();store.AddDefinition(Custom());store.AddDefinition(Custom("second","TWO"));
            var guides=new AchievementGuideService(dir);var rows=new List<GameAchievementsFetchService.AchievementRow>{new(){Provider="steam",ProviderGameId="123",ProviderAchievementId="ONE",Title="One"},new(){Provider="steam",ProviderGameId="123",ProviderAchievementId="TWO",Title="Two"}};
            var window=Guide(guides,rows,"ONE");
            try{
                window.AttachTracking(store,Account,"");var control=Field<AchievementInlineTrackers>(window,"_linkedTrackers");Assert.Single(control.Children);Assert.False(((Expander)control.Children[0]).IsExpanded);
                ((Expander)control.Children[0]).IsExpanded=true;
                var list=Field<ListBox>(window,"_achievementList");list.SelectedItem=list.Items.OfType<FrameworkElement>().Last(e=>e.Tag!=null);
                Assert.Single(control.Children);Assert.False(((Expander)control.Children[0]).IsExpanded);
                Field<Button>(window,"_edit").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Field<TextBox>(window,"_guideText").Text="Keep my draft";
                control.Reload();Assert.Equal("Keep my draft",Field<TextBox>(window,"_guideText").Text);Field<Button>(window,"_cancelEdit").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }finally{window.Close();}
        });
    }
    [Fact]
    public void BothAchievementScreensShowLinkedNativeRowsBelowTheGuideAndRender()
    {
        OnSta(()=>{
            var store=Store();var native=new Native{Completed=22};Observe(NativeService(store,native));
            var guides=new AchievementGuideService(dir);var row=new GameAchievementsFetchService.AchievementRow{Provider="steam",ProviderGameId="1850570",ProviderAchievementId="035",Title="Fount of Knowledge",Description="Restore all memory chips.",ProgressKnown=true};
            var rows=new List<GameAchievementsFetchService.AchievementRow>{row};var entry=guides.SyncDefinitionsAndLoadGuides("fixture",rows).Single();guides.SaveGuide(new(){AchievementId=entry.AchievementId,GuideText="Recover and submit all 56 memory chips.",SourceTitle="Memory chip guide",SourceUrl="https://example.com/guide"});
            var window=Guide(guides,rows,"035");
            try{
                window.AttachTracking(store,Account,"");var control=Field<AchievementInlineTrackers>(window,"_linkedTrackers");Assert.Same(Field<StackPanel>(window,"_reader"),control.Parent);
                Render(window,"achievement-guide-tracker-collapsed",1240,900);var expand=(Expander)control.Children[0];Assert.False(expand.IsExpanded);expand.IsExpanded=true;Render(window,"achievement-guide-tracker-expanded",1240,900);
                var boxes=Descendants(control).OfType<CheckBox>().ToList();Assert.NotEmpty(boxes);Assert.All(boxes.Where(b=>b.IsChecked==true),b=>Assert.False(b.IsEnabled));Assert.Equal(1,native.Reads);
                Render(window,"achievement-guide-tracker-narrow",900,760);
            }finally{window.Close();}
            var detail=new AchievementDetailWindow(null!,new(){Provider="steam",Id="1850570",Title="Death Stranding Director's Cut"},row,rows,0,guides,trackingStore:store,trackingAccount:Account);
            try{Render(detail,"achievement-detail-tracker-collapsed",640,900);var control=Descendants((DependencyObject)detail.Content).OfType<AchievementInlineTrackers>().Single();Assert.Single(control.Children);Assert.False(((Expander)control.Children[0]).IsExpanded);((Expander)control.Children[0]).IsExpanded=true;Render(detail,"achievement-detail-tracker",640,900);}finally{detail.Close();}
        });
    }
    [Fact]
    public void MultipleLinksMissingAccountsArchivesAndProvidersRemainIsolated()
    {
        OnSta(()=>{
            var store=Store();var first=Custom();first.Items[1].Archived=true;store.AddDefinition(first);store.AddDefinition(Custom("second"));
            var retro=Custom("retro");retro.Provider="retroachievements";store.AddDefinition(retro);var unlinked=Custom("unlinked");unlinked.AchievementId=null;store.AddDefinition(unlinked);
            var control=new AchievementInlineTrackers();control.Bind(store,"steam","123","ONE","");Assert.Equal(2,control.Children.Count);
            foreach(Expander section in control.Children){Assert.False(section.IsExpanded);Assert.Contains("Connect account",((TextBlock)section.Header).Text);section.IsExpanded=true;}
            Layout(control,620);Assert.All(Descendants(control).OfType<CheckBox>(),box=>Assert.False(box.IsEnabled));
            Assert.Equal(3,Descendants(control).OfType<ListBox>().Sum(list=>list.Items.Count));
            Assert.Throws<InvalidDataException>(()=>store.SetInlineCompletion(first.Id,Account,"b",true));
            Assert.All(store.Load(first.Id,Account),state=>Assert.Null(state.Current));
        });
    }
    [Fact]
    public void NativeOrderRanksUseIndependentSmallBoxesAndVirtualizeTheLongList()
    {
        OnSta(()=>{
            const string id=AchievementTrackingCatalog.DeathStrandingOrdersId;var store=Store();var native=new Native{Completed=1};Observe(NativeService(store,native,id),id);
            var control=new AchievementInlineTrackers();control.Bind(store,"steam","1850570","019",Account);((Expander)control.Children[0]).IsExpanded=true;Layout(control,650);
            Assert.Contains("1 / 80",((TextBlock)((Expander)control.Children[0]).Header).Text);
            var list=Descendants(control).OfType<ListBox>().Single();Assert.Equal(540,list.Items.Count);
            var boxes=Descendants(control).OfType<CheckBox>().ToList();Assert.InRange(boxes.Count,2,100);
            var done=boxes.Single(box=>box.IsChecked==true);Assert.Equal("LL",done.Content);Assert.False(done.IsEnabled);
            Assert.Contains(boxes,box=>Equals(box.Content,"LLL")&&box.IsEnabled);Assert.All(boxes,box=>Assert.True(box.FontSize<=12.5));
            var ll=store.Load(id,Account).First();Assert.Throws<InvalidOperationException>(()=>store.SetInlineCompletion(id,Account,ll.Item.Id,false));
            Render(new Window{Content=control},"achievement-inline-order-ranks",650,430);
        });
    }
    [Fact]
    public void ManualCompletionOverAnIncompleteNativeObservationCanBeUnchecked()
    {
        var store=Store();Observe(NativeService(store,new Native()));var first=First(store);
        store.SetInlineCompletion(Chips,Account,first.Item.Id,true);Assert.True(First(store).Complete);
        store.SetInlineCompletion(Chips,Account,first.Item.Id,false);Assert.False(First(store).Complete);Assert.Equal(0,First(store).ReportedCurrent);
    }
    static AchievementGuideWindow Guide(AchievementGuideService guides,List<GameAchievementsFetchService.AchievementRow> rows,string preferred)=>(AchievementGuideWindow)Activator.CreateInstance(typeof(AchievementGuideWindow),BindingFlags.Instance|BindingFlags.NonPublic,null,new object?[]{null,guides,"Death Stranding Director's Cut","fixture",rows,preferred,new Func<MessageBoxResult>(()=>MessageBoxResult.Cancel)},null)!;
    static T Field<T>(object obj,string name)=>(T)obj.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic)!.GetValue(obj)!;
    static IEnumerable<DependencyObject> Descendants(DependencyObject root){yield return root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Descendants(VisualTreeHelper.GetChild(root,i)))yield return child;}
    static void Click(CheckBox box,bool value){box.IsChecked=value;box.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));}
    static void Layout(FrameworkElement root,double width){root.Measure(new Size(width,700));root.Arrange(new Rect(0,0,width,700));root.UpdateLayout();}
    static void Render(Window window,string name,int width,int height)
    {
        var root=(FrameworkElement)window.Content;root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();
        var folder=Environment.GetEnvironmentVariable("PIXELVAULT_INLINE_TRACKER_QA");if(string.IsNullOrWhiteSpace(folder))return;Directory.CreateDirectory(folder);
        var bitmap=new RenderTargetBitmap(width,height,96,96,PixelFormats.Pbgra32);bitmap.Render(root);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var output=File.Create(Path.Combine(folder,name+".png"));png.Save(output);
    }
    public void Dispose(){SqliteConnection.ClearAllPools();if(!Directory.Exists(dir))return;var full=Path.GetFullPath(dir);Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),full,StringComparison.OrdinalIgnoreCase);for(int i=0;Directory.Exists(full);i++){try{Directory.Delete(full,true);}catch(IOException)when(i<5){Thread.Sleep(50);}}}
}
