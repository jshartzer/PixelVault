using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class TrackerIconTests : IDisposable
{
    readonly string dir=Path.Combine(Path.GetTempPath(),"PV-TrackerIcons-"+Guid.NewGuid().ToString("N"));
    static AchievementTrackerDefinition Definition()=>new(){Id="icons.test",Provider="steam",GameId="42",GameTitle="Mountain journey",Title="Explore every region",Items=new(){new(){Id="one",Name="Northern peak",Target=10}}};
    static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(target)!;
    static IEnumerable<DependencyObject> Tree(DependencyObject root){yield return root;foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())foreach(var nested in Tree(child))yield return nested;}
    static void Pump(Task task){var frame=new DispatcherFrame();var dispatcher=Dispatcher.CurrentDispatcher;_=task.ContinueWith(_=>dispatcher.BeginInvoke(new Action(()=>frame.Continue=false)),TaskScheduler.Default);if(!task.IsCompleted)Dispatcher.PushFrame(frame);task.GetAwaiter().GetResult();}
    static void Render(Window window,string name,int width,int height)
    {
        var root=(FrameworkElement)window.Content;root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();
        var output=Environment.GetEnvironmentVariable("PIXELVAULT_TRACKING_REVIEW_OUTPUT");if(string.IsNullOrEmpty(output))return;
        Directory.CreateDirectory(output);var bitmap=new RenderTargetBitmap(width+48,height+48,96,96,PixelFormats.Pbgra32);bitmap.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(Path.Combine(output,name+".png"));encoder.Save(stream);
    }
    [Theory][InlineData(false)][InlineData(true)]
    public void IconsPersistForCustomAndBundledTrackersWithoutChangingProgress(bool bundled)
    {
        var store=new AchievementTrackingStore(dir);var d=bundled?AchievementTrackingCatalog.DeathStranding():Definition();if(!bundled)store.AddDefinition(d);
        var item=d.Items[0].Id;store.SetManualProgress(d.Id,"player",item,1);store.SaveNote(d.Id,"player",item,"Keep this note");var history=store.History(d.Id,"player").Count;
        store.SetTrackerIcon(d.Id,"exploration");var reopened=new AchievementTrackingStore(dir);var saved=reopened.Definitions().Single(t=>t.Id==d.Id);
        Assert.Equal("exploration",saved.IconId);Assert.Equal("exploration",TrackerDefinitionTransfer.Parse(TrackerDefinitionTransfer.Serialize(saved)).IconId);
        Assert.Equal(1,reopened.Load(d.Id,"player").Single(s=>s.Item.Id==item).Current);Assert.Equal("Keep this note",reopened.Load(d.Id,"player").Single(s=>s.Item.Id==item).Note);Assert.Equal(history,reopened.History(d.Id,"player").Count);
        Assert.Throws<InvalidDataException>(()=>store.SetTrackerIcon(d.Id,"unknown"));Assert.Throws<InvalidDataException>(()=>store.SetTrackerIcon("missing","checklist"));
        store.SetTrackerIcon(d.Id,null!);Assert.Null(new AchievementTrackingStore(dir).Definitions().Single(t=>t.Id==d.Id).IconId);
    }
    [Theory][InlineData(472)][InlineData(760)]
    public void ListIconsFillCardHeightAndPickerChangesAreSavedAndCanBeCancelled(int width)=>AchievementCollectionAccessTests.OnSta(()=>
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var store=new AchievementTrackingStore(dir);var d=Definition();store.AddDefinition(d);var game=new AchievementHubGame{Provider="steam",Id="42",Title=d.GameTitle};var nav=new TrackerNavigation(store,()=>new(){Games=new(){game},SteamAccount="player"});
        var window=new TrackerGameWindow(null!,nav,game);
        try
        {
            Pump(window.ReloadAsync());Render(window,"tracker-icons-list-"+width,width,600);
            Button Card()=>Tree((DependencyObject)window.Content).OfType<Button>().Single(b=>b.Tag is TrackerDashboardItem);
            var card=Card();var grid=(Grid)card.Content;var icon=(FrameworkElement)grid.Children[0];Assert.InRange(Math.Abs(icon.ActualHeight-icon.ActualWidth),0,.1);Assert.True(icon.ActualHeight>100);Assert.InRange(Math.Abs(icon.ActualHeight-grid.ActualHeight),0,.1);
            bool shown=false;window.ShowIconPicker=dialog=>
            {
                shown=true;Assert.Equal(dialog.Width,dialog.Height);Render(dialog,"tracker-icon-picker",500,470);
                Assert.Equal(TrackerIconIds.All.Count,Tree((DependencyObject)dialog.Content).OfType<Button>().Count(b=>b.Tag is string));
                typeof(TrackerIconPicker).GetProperty("SelectedIconId",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(dialog,"exploration");return true;
            };
            ((MenuItem)card.ContextMenu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Pump(window.PendingIcon);Assert.True(shown);Assert.Equal("exploration",store.Definitions().Single(t=>t.Id==d.Id).IconId);
            window.ShowIconPicker=_=>false;((MenuItem)Card().ContextMenu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Pump(window.PendingIcon);Assert.Equal("exploration",store.Definitions().Single(t=>t.Id==d.Id).IconId);
            Render(window,"tracker-icons-selected-"+width,width,600);
        }
        finally{window.Close();}
    });
    [Fact]
    public void CompletedTrackersUseGreenCardsAndMoveBelowUnfinishedWithoutEditingProgress()=>AchievementCollectionAccessTests.OnSta(()=>
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        var store=new AchievementTrackingStore(dir);
        var done=Definition();done.Id="cards.done";done.Title="A completed";store.AddDefinition(done);
        var active=Definition();active.Id="cards.active";active.Title="Z unfinished";store.AddDefinition(active);
        var zero=Definition();zero.Id="cards.zero";zero.Title="B zero goals";zero.Items[0].Archived=true;store.AddDefinition(zero);
        var unknown=Definition();unknown.Id="cards.unknown";unknown.Title="C unknown";store.AddDefinition(unknown);
        store.SetManualProgress(done.Id,"player","one",10);store.SaveNote(done.Id,"player","one","Keep my note");var history=store.History(done.Id,"player").Count;
        var game=new AchievementHubGame{Provider="steam",Id="42",Title=done.GameTitle};var nav=new TrackerNavigation(store,()=>new(){Games=new(){game},SteamAccount="player"});var window=new TrackerGameWindow(null!,nav,game);
        List<Button> Cards()=>Field<StackPanel>(window,"rows").Children.OfType<Button>().Where(b=>b.Tag is TrackerDashboardItem).ToList();
        try
        {
            Pump(window.ReloadAsync());Render(window,"tracker-completed-760",760,900);Render(window,"tracker-completed-520",520,900);
            Assert.Equal(new[]{zero.Id,unknown.Id,active.Id,done.Id},Cards().Select(b=>((TrackerDashboardItem)b.Tag).Definition.Id));
            Assert.Single(Field<StackPanel>(window,"rows").Children.OfType<TextBlock>().Where(t=>t.Text=="✓ Completed · 1"));
            var complete=Cards().Last();Assert.Contains("Completed.",System.Windows.Automation.AutomationProperties.GetHelpText(complete));
            Assert.Contains(Tree((DependencyObject)complete.Content).OfType<TextBlock>(),t=>t.Text.Contains("✓"));
            if(!SystemParameters.HighContrast)Assert.Equal(Color.FromRgb(0x20,0x35,0x26),Assert.IsType<SolidColorBrush>(complete.Background).Color);
            Assert.Equal(history,store.History(done.Id,"player").Count);Assert.Equal("Keep my note",store.Load(done.Id,"player").Single().Note);
            store.SetManualProgress(active.Id,"player","one",10);Pump(window.ReloadAsync());Assert.All(Cards().Take(2),b=>Assert.False(((TrackerDashboardItem)b.Tag).Complete));Assert.All(Cards().Skip(2),b=>Assert.True(((TrackerDashboardItem)b.Tag).Complete));
            store.SetManualProgress(done.Id,"player","one",0);Pump(window.ReloadAsync());Assert.Equal(done.Id,((TrackerDashboardItem)Cards().First().Tag).Definition.Id);Assert.False(((TrackerDashboardItem)Cards().First().Tag).Complete);
        }
        finally{window.Close();}
    });

    [Fact]
    public void IndividualWorkspaceKeepsItsIdentityAndDraftsWhenImageChanges()=>AchievementCollectionAccessTests.OnSta(()=>
    {
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));var store=new AchievementTrackingStore(dir);var d=Definition();store.AddDefinition(d);
        var other=Definition();other.Id="icons.other";store.AddDefinition(other);
        var window=new AchievementTrackerWindow(null!,store,"player","",provider:d.Provider,gameId:d.GameId,trackerId:d.Id,browseGameTrackers:true);
        try
        {
            Pump(window.LoadDefinitionsAsync());Assert.Single(Field<ComboBox>(window,"trackers").Items);Assert.Equal(d.Title,window.Title);Assert.Equal(d.Title,Field<TextBlock>(window,"trackerTitle").Text);
            Field<TextBox>(window,"notes").Text="Unsaved note";Field<TextBox>(window,"current").Text="7";
            window.ShowIconPicker=dialog=>{typeof(TrackerIconPicker).GetProperty("SelectedIconId",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(dialog,"collectibles");return true;};
            var image=Field<Border>(window,"trackerImage");((MenuItem)image.ContextMenu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));Pump(window.PendingIcon);
            Assert.Equal("Unsaved note",Field<TextBox>(window,"notes").Text);Assert.Equal("7",Field<TextBox>(window,"current").Text);Assert.Equal("collectibles",store.Definitions().Single(t=>t.Id==d.Id).IconId);
            Assert.DoesNotContain(Tree((DependencyObject)window.Content).OfType<Button>(),b=>Equals(b.Content,"New checklist"));
            Render(window,"tracker-individual-icon",1000,720);var root=(FrameworkElement)window.Content;Assert.True(image.TranslatePoint(new Point(),root).X<Field<TextBlock>(window,"trackerTitle").TranslatePoint(new Point(),root).X);
            Pump(window.LoadDefinitionsAsync());Assert.Equal(d.Id,Field<AchievementTrackerDefinition>(window,"selected").Id);
        }
        finally{window.Close();}
    });
    [Fact]
    public void AchievementArtLoadsInColorAndManualIconsOverrideIt()=>AchievementCollectionAccessTests.OnSta(()=>
    {
        var cache=(System.Collections.Concurrent.ConcurrentDictionary<string,byte[]>)typeof(AchievementsInfoWindow).GetField("AchievementIconCache",BindingFlags.Static|BindingFlags.NonPublic)!.GetValue(null)!;
        const string url="https://tracker-icon-test.invalid/art.png";
        var bitmap=BitmapSource.Create(1,1,96,96,PixelFormats.Bgra32,null,new byte[]{20,90,230,255},4);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=new MemoryStream();encoder.Save(stream);cache[url]=stream.ToArray();
        try
        {
            var row=new GameAchievementsFetchService.AchievementRow{IconUrlColor=url,ProgressKnown=true,Unlocked=false};
            var automatic=TrackerIconVisual.Create(null!,row);var images=Tree(automatic).OfType<Image>().ToList();Assert.Equal(2,images.Count);
            var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);
            Assert.IsAssignableFrom<BitmapSource>(images[1].Source);Assert.IsNotType<FormatConvertedBitmap>(images[1].Source);
            Assert.Single(Tree(TrackerIconVisual.Create("deliveries",row)).OfType<Image>());
            cache[url]=new byte[]{1,2,3};var failed=TrackerIconVisual.Create(null!,row);
            frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);
            var fallback=Tree(failed).OfType<Image>().ToList();Assert.NotNull(fallback[0].Source);Assert.Equal(Visibility.Collapsed,fallback[1].Visibility);
        }
        finally{cache.TryRemove(url,out _);}
    });
    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void LinkedTrackerUsesFullResolutionBundledArtAndManualSelectionStillWins(bool known,bool unlocked)=>AchievementCollectionAccessTests.OnSta(()=>
    {
        var row=new GameAchievementsFetchService.AchievementRow{IconUrlColor="https://shared.fastly.steamstatic.com/community_assets/images/apps/1850570/2cd0c3046f89413d00e733f015841acd7773fa3f.jpg",IconUrlGray="https://artwork-test.invalid/locked.png",ProgressKnown=known,Unlocked=unlocked};
        var automatic=Tree(TrackerIconVisual.Create(null!,row)).OfType<Image>().ToList();
        Assert.Equal(240,Assert.IsType<BitmapImage>(automatic[1].Source).PixelWidth);
        Assert.Single(Tree(TrackerIconVisual.Create("crafting",row)).OfType<Image>());
        Assert.Equal(known,row.ProgressKnown);Assert.Equal(unlocked,row.Unlocked);
    });
    [Fact]
    public void MissingArtworkAndUnknownIconsHaveAVectorFallback()=>AchievementCollectionAccessTests.OnSta(()=>
    {
        foreach(var id in TrackerIconIds.All.Concat(new[]{"unknown",null!}))
        {
            var visual=TrackerIconVisual.Create(id,new(){Title="No artwork"});visual.Measure(new Size(160,160));visual.Arrange(new Rect(0,0,160,160));
            Assert.NotNull(Assert.Single(Tree(visual).OfType<Image>()).Source);
        }
    });
    public void Dispose(){SqliteConnection.ClearAllPools();if(Directory.Exists(dir))Directory.Delete(dir,true);}
}
