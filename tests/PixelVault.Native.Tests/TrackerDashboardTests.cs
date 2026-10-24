using System.IO;
using System.Reflection;
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

public sealed class TrackerDashboardTests : IDisposable
{
    readonly string dir=Path.Combine(Path.GetTempPath(),"PV-TrackerDashboard-"+Guid.NewGuid().ToString("N"));
    const string Account="76561197960265729";
    public TrackerDashboardTests()=>Directory.CreateDirectory(dir);
    public void Dispose(){SqliteConnection.ClearAllPools();Directory.Delete(dir,true);}
    AchievementTrackingStore Store()=>new(dir);
    static AchievementTrackerDefinition Definition(string id="custom.one",string provider="steam",string game="42",string achievement="A")=>new(){Id=id,Provider=provider,GameId=game,GameTitle="Puzzle journey",Title=id=="custom.one"?"Solve every puzzle":"Find every secret",AchievementId=achievement,Items=new(){new(){Id="first",Name="First room"},new(){Id="second",Name="Second room"}}};
    static AchievementHubGame Game(string provider="steam",string id="42")=>new(){Provider=provider,Id=id,Title="Puzzle journey"};
    static AchievementHubSession Session(params AchievementHubGame[] games)=>new(){Games=games.ToList(),SteamAccount=Account,RetroAccount="player"};
    static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(target)!;
    static IEnumerable<DependencyObject> Children(DependencyObject root){yield return root;foreach(var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())foreach(var nested in Children(child))yield return nested;}
    static void Pump(Task task){var frame=new DispatcherFrame();var dispatcher=Dispatcher.CurrentDispatcher;_=task.ContinueWith(_=>dispatcher.BeginInvoke(new Action(()=>frame.Continue=false)),TaskScheduler.Default);if(!task.IsCompleted)Dispatcher.PushFrame(frame);task.GetAwaiter().GetResult();}
    static void Render(Window window,string file,int width=980,int height=700)
    {
        var root=(FrameworkElement)window.Content;root.Measure(new Size(width,height));root.Arrange(new Rect(0,0,width,height));root.UpdateLayout();
        var output=Environment.GetEnvironmentVariable("PIXELVAULT_TRACKING_REVIEW_OUTPUT");if(string.IsNullOrWhiteSpace(output))return;Directory.CreateDirectory(output);
        var bitmap=new RenderTargetBitmap(width+48,height+48,96,96,PixelFormats.Pbgra32);bitmap.Render(root);var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var stream=File.Create(Path.Combine(output,file+".png"));encoder.Save(stream);
    }
    [Fact]
    public void DashboardShowsTrackedGamesUsesLibraryArtAndKeepsProviderIdentitiesDistinct()
    {
        var store=Store();store.AddDefinition(Definition());store.AddDefinition(Definition("custom.two"));store.AddDefinition(Definition("custom.retro","retroachievements"));
        var steam=Game();steam.ArtPath="local-cover.jpg";var empty=Game(id:"99");empty.Title="No tracker";
        var entries=TrackerDashboard.Load(store,Session(steam,Game("retroachievements"),empty));
        Assert.Equal(2,entries.Count);Assert.Equal("local-cover.jpg",entries.Single(e=>e.Game.Provider=="steam").Game.ArtPath);
        Assert.Equal(2,entries.Single(e=>e.Game.Provider=="steam").Trackers.Count);Assert.Single(entries.Single(e=>e.Game.Provider=="retroachievements").Trackers);
        Assert.DoesNotContain(entries,e=>e.Game.Id=="1850570"||e.Game.Id=="99");
        var orphan=TrackerDashboard.Load(store,new());Assert.Equal(2,orphan.Count);Assert.All(orphan,e=>Assert.False(e.Trackers[0].AccountConnected));
        Assert.Empty(TrackerDashboard.Load(new AchievementTrackingStore(Path.Combine(dir,"empty")),new()));
    }
    [Fact]
    public void ProgressIsAccountScopedAndDashboardNeverReadsGameFilesOrChangesManualState()
    {
        var store=Store();var d=Definition();store.AddDefinition(d);store.SetManualProgress(d.Id,Account,"first",1);store.SaveNote(d.Id,Account,"first","Keep this route");
        var state=store.Load(d.Id,Account).Select(s=>(s.Item.Id,s.Current,s.Note)).ToList();var history=store.History(d.Id,Account).Count;
        var entries=TrackerDashboard.Load(store,Session(Game()));var tracker=Assert.Single(Assert.Single(entries).Trackers);Assert.Equal(1,tracker.Completed);Assert.Equal(2,tracker.Total);Assert.Equal(1,tracker.Unconfirmed);
        var other=Session(Game());other.SteamAccount="another-player";Assert.Equal(0,TrackerDashboard.Load(store,other)[0].Trackers[0].Completed);
        Assert.Equal(state,store.Load(d.Id,Account).Select(s=>(s.Item.Id,s.Current,s.Note)).ToList());Assert.Equal(history,store.History(d.Id,Account).Count);
    }
    [Fact]
    public void GameCompletionIncludesPartialTrackersAndWeightsTheirActiveGoals()
    {
        var game=new TrackerDashboardGame{Trackers=new(){new(){Completed=1,Total=2,AccountConnected=true},new(){Completed=3,Total=4,AccountConnected=true}}};
        Assert.Equal(100.0*4/6,game.Percent);Assert.Contains("0 complete",game.Summary);
        game.Trackers.Add(new(){Completed=0,Total=0,AccountConnected=true});Assert.Equal(100.0*4/6,game.Percent);
        game.Trackers[1].AccountConnected=false;Assert.Null(game.Percent);
        Assert.Null(new TrackerDashboardGame().Percent);
    }
    [Fact]
    public void CategoryGoalsCountEachQualifyingRecordOnceAndIgnoreArchives()
    {
        var store=Store();var d=Definition();d.Milestones=new(){"Found","Perfect"};d.CategoryTargets=new(){{"Puzzles",1}};
        d.Items=TrackerMilestones.Expand(new[]{new AchievementTrackerItem{Id="room",Name="Room",Category="Puzzles"}},d.Milestones);store.AddDefinition(d);
        foreach(var item in d.Items)store.SetManualProgress(d.Id,Account,item.Id,1);
        var game=TrackerDashboard.Load(store,Session(Game()))[0];var tracker=game.Trackers[0];Assert.Equal(1,tracker.Completed);Assert.Equal(1,tracker.Total);Assert.True(tracker.Complete);Assert.Equal(100,game.Percent);
        var archived=Definition("custom.archived");archived.Items[1].Archived=true;store.AddDefinition(archived);Assert.Equal(1,TrackerDashboard.Load(store,Session(Game()))[0].Trackers.Single(t=>t.Definition.Id==archived.Id).Total);
    }
    [Fact]
    public void SourceIndicatorsDistinguishGameConnectionCapabilityOptInPauseAndUnsupportedReaders()
    {
        var store=Store();var service=new TrackerRefreshService(store);var d=AchievementTrackingCatalog.DeathStranding();
        var absent=service.Connection(d,null!,new(),new());Assert.False(absent.GameFilesConnected);Assert.Contains("Not connected",absent.Label);
        service.Configure(d.Id,Account,DeathStrandingTrackerSource.AdapterId,"1",Path.GetFullPath(dir),gameDirectory:Path.GetFullPath(dir));var binding=store.Source(d.Id,Account);
        var native=service.Connection(d,binding,new(),new());Assert.True(native.GameFilesConnected);Assert.True(native.AutomaticAvailable);Assert.False(native.AutomaticEnabled);Assert.Contains("Off",native.Label);
        var settings=new TrackerAutomationSettings{Mode=TrackerAutomationMode.SaveChanges};Assert.True(service.Connection(d,binding,settings,new()).AutomaticEnabled);
        settings.Paused=true;Assert.Contains("paused",service.Connection(d,binding,settings,new()).Label);
        settings.Paused=false;Assert.Contains("Review needed",service.Connection(d,binding,settings,new(){ReviewPending=true}).Label);
        var orders=store.Definitions().Single(t=>t.Id==AchievementTrackingCatalog.DeathStrandingOrdersId);
        Assert.Contains("Not connected",service.Connection(orders,null!,new(),new()).Label);
        service.Configure(orders.Id,Account,DeathStrandingTrackerSource.AdapterId,"1",Path.GetFullPath(dir),gameDirectory:Path.GetFullPath(dir));
        var orderConnection=service.Connection(orders,store.Source(orders.Id,Account),new(),new());Assert.True(orderConnection.GameFilesConnected);Assert.True(orderConnection.AutomaticAvailable);
        foreach(var id in new[]{AchievementTrackingCatalog.DeathStrandingChipsId})
        {var manual=service.Connection(store.Definitions().Single(t=>t.Id==id),null!,new(),new());Assert.False(manual.GameFilesConnected);Assert.False(manual.AutomaticEnabled);Assert.Contains("Not connected",manual.Label);}
        {var manual=service.Connection(Definition(),null!,new(),new());Assert.False(manual.GameFilesConnected);Assert.False(manual.AutomaticAvailable);Assert.Contains("reader unavailable",manual.Label);}
        var json=new TrackerSourceBinding{AdapterId=JsonFileTrackerSource.AdapterId,AdapterVersion="1"};var imported=service.Connection(d,json,new(),new());Assert.False(imported.GameFilesConnected);Assert.True(imported.AutomaticAvailable);Assert.Contains("Progress file",imported.Label);
    }
    [Theory]
    [InlineData(720)]
    [InlineData(1100)]
    public void LibraryButtonIsCenteredEquidistantBetweenSortAndAll(double width)
    {
        AchievementCollectionAccessTests.OnSta(()=>
        {
            var panes=new MainWindow.LibraryBrowserPaneRefs{SortFilterMenuButton=new Button{Content="Sort / filter",Width=118},GroupAllButton=new Button{Content="All",Width=58,Margin=new Thickness(0,0,8,0)}};
            var grid=new Grid();grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});grid.Children.Add(panes.SortFilterMenuButton);Grid.SetColumn(panes.GroupAllButton,2);grid.Children.Add(panes.GroupAllButton);MainWindow.AddLibraryTrackerButton(grid,panes);
            grid.Measure(new Size(width,40));grid.Arrange(new Rect(0,0,width,40));grid.UpdateLayout();
            var sort=panes.SortFilterMenuButton.TranslatePoint(new Point(),grid);var tracker=panes.TrackersButton.TranslatePoint(new Point(),grid);var all=panes.GroupAllButton.TranslatePoint(new Point(),grid);
            var left=tracker.X-(sort.X+panes.SortFilterMenuButton.ActualWidth);var right=all.X-(tracker.X+panes.TrackersButton.ActualWidth);Assert.InRange(Math.Abs(left-right),0,.1);Assert.True(left>=8);Assert.Equal(34,panes.TrackersButton.ActualHeight);
        });
    }
    [Fact]
    public void DashboardGameTrackerNavigationSelectsTheChosenTrackerAndKeepsTheGameSwitcher()
    {
        var store=Store();store.AddDefinition(Definition());store.AddDefinition(Definition("custom.two",achievement:"B"));store.AddDefinition(Definition("custom.other",game:"99"));
        AchievementCollectionAccessTests.OnSta(()=>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var coverSizes=new List<(double,double)>();var navigation=new TrackerNavigation(store,()=>Session(Game()),(g,w,h)=>{coverSizes.Add((w,h));return new Border{Width=w,Height=h,Background=Brushes.DarkSlateBlue};});
            var dashboard=new TrackerDashboardWindow(null!,navigation);var opened=false;
            navigation.ShowTracker=tracker=>{try{Pump(tracker.LoadDefinitionsAsync());Assert.Equal("custom.two",Field<AchievementTrackerDefinition>(tracker,"selected").Id);Assert.Equal(2,Field<ComboBox>(tracker,"trackers").Items.Count);Assert.All(Field<ComboBox>(tracker,"trackers").Items.OfType<AchievementTrackerDefinition>(),d=>Assert.Equal("42",d.GameId));opened=true;}finally{tracker.Close();}};
            navigation.ShowGame=game=>
            {
                try{Pump(game.ReloadAsync());Render(game,"tracker-game-list");var chosen=Children((DependencyObject)game.Content).OfType<Button>().Single(b=>b.Tag is TrackerDashboardItem t&&t.Definition.Id=="custom.two");chosen.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump(game.PendingNavigation);}finally{game.Close();}
            };
            try
            {
                Pump(dashboard.ReloadAsync());Render(dashboard,"tracker-dashboard");Assert.All(coverSizes,s=>{Assert.Equal(132,s.Item1);Assert.Equal(192,s.Item2);});
                var card=Children((DependencyObject)dashboard.Content).OfType<Button>().Single(b=>b.Tag is TrackerDashboardGame e&&e.Game.Id=="42");card.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump(dashboard.PendingNavigation);Assert.True(opened);
                Render(dashboard,"tracker-dashboard-narrow",512,460);
            }
            finally{dashboard.Close();}
        });
    }
    [Fact]
    public void AchievementShortcutOpensExactLinkedTrackerAndMultipleLinksOfferAChoice()
    {
        var store=Store();store.AddDefinition(Definition());store.AddDefinition(Definition("custom.two",achievement:"B"));
        AchievementCollectionAccessTests.OnSta(()=>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var row=new GameAchievementsFetchService.AchievementRow{Provider="steam",ProviderGameId="42",ProviderAchievementId="A",Title="Puzzle mastery"};
            var detail=new AchievementDetailWindow(null!,Game(),row,new[]{row},0,null!,trackingStore:store,trackingAccount:Account);
            var opened=false;detail.TrackingNavigation.ShowTracker=window=>{try{Pump(window.LoadDefinitionsAsync());Assert.Equal("custom.one",Field<AchievementTrackerDefinition>(window,"selected").Id);Assert.Equal(2,Field<ComboBox>(window,"trackers").Items.Count);opened=true;}finally{window.Close();}};
            try{var button=Children((DependencyObject)detail.Content).OfType<Button>().Single(b=>Equals(b.Content,"Open tracker"));button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Assert.True(opened);Render(detail,"achievement-linked-tracker",600,730);}finally{detail.Close();}
            store.SetAchievementLink("custom.two",row);
            var navigation=new TrackerNavigation(store,()=>Session(Game()));bool choice=false;navigation.ShowTracker=_=>throw new Exception("Do not choose an arbitrary linked tracker");navigation.ShowGame=window=>{try{Pump(window.ReloadAsync());Assert.Equal(2,Children((DependencyObject)window.Content).OfType<Button>().Count(b=>b.Tag is TrackerDashboardItem));choice=true;}finally{window.Close();}};
            navigation.OpenAchievement(null!,Game(),"A");Assert.True(choice);
        });
    }
    [Fact]
    public void IndividualTrackerIndicatorUpdatesWhenAutomaticRefreshChanges()
    {
        var store=Store();var id=AchievementTrackingCatalog.DeathStrandingId;var service=new TrackerRefreshService(store);service.Configure(id,Account,DeathStrandingTrackerSource.AdapterId,"1",dir,gameDirectory:dir);
        AchievementCollectionAccessTests.OnSta(()=>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));var window=new AchievementTrackerWindow(null!,store,Account,"",provider:"steam",gameId:"1850570",trackerId:id,browseGameTrackers:true);
            try
            {
                Pump(window.LoadDefinitionsAsync());Assert.Contains("Game files connected",Field<TextBlock>(window,"connectionIndicator").Text);Assert.Contains("Off",Field<TextBlock>(window,"connectionIndicator").Text);
                store.SaveAutomation(id,Account,store.Source(id,Account).Id,TrackerAutomationMode.Periodic,5,false);Pump(window.ReloadAsync());Assert.Contains("Automatic on",Field<TextBlock>(window,"connectionIndicator").Text);
            }
            finally{window.Close();}
        });
    }
    [Fact]
    public void NativeDashboardShowsConnectedAndAvailableTrackersWithCompactCoverArt()
    {
        var store=Store();var service=new TrackerRefreshService(store);var nativeIds=new[]{AchievementTrackingCatalog.DeathStrandingId,AchievementTrackingCatalog.DeathStrandingBuildId,AchievementTrackingCatalog.DeathStrandingUpgradeId};
        foreach(var id in nativeIds){service.Configure(id,Account,DeathStrandingTrackerSource.AdapterId,"1",dir,gameDirectory:dir);var definition=store.Definitions().Single(d=>d.Id==id);store.SetManualProgress(id,Account,definition.Items[0].Id,1);}
        var auto=AchievementTrackingCatalog.DeathStrandingId;store.SaveAutomation(auto,Account,store.Source(auto,Account).Id,TrackerAutomationMode.SaveChanges,5,false);
        var game=new AchievementHubGame{Provider="steam",Id="1850570",Title="Death Stranding Director's Cut"};var session=Session(game);var data=TrackerDashboard.Load(store,session);
        Assert.Single(data);Assert.Equal(5,data[0].Trackers.Count);Assert.Equal(3,data[0].Trackers.Count(t=>t.Connection.GameFilesConnected));Assert.Single(data[0].Trackers,t=>t.Connection.AutomaticEnabled);
        AchievementCollectionAccessTests.OnSta(()=>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var cover=Environment.GetEnvironmentVariable("PIXELVAULT_TRACKER_COVER_REVIEW");
            FrameworkElement Art(AchievementHubGame g,double w,double h)
            {
                if(string.IsNullOrEmpty(cover)||!File.Exists(cover))return TrackerDashboardWindow.Placeholder(g,w,h);
                var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;bitmap.UriSource=new Uri(cover);bitmap.EndInit();bitmap.Freeze();
                return new Border{Width=w,Height=h,CornerRadius=new CornerRadius(8),Child=new Image{Source=bitmap,Width=w,Height=h,Stretch=Stretch.UniformToFill}};
            }
            var navigation=new TrackerNavigation(store,()=>session,Art);var dashboard=new TrackerDashboardWindow(null!,navigation);var list=new TrackerGameWindow(null!,navigation,game);
            try
            {
                Pump(dashboard.ReloadAsync());Render(dashboard,"tracker-dashboard-game-cover");Pump(list.ReloadAsync());Render(list,"tracker-game-save-connections",760,680);Render(list,"tracker-game-save-connections-narrow",472,430);
                Assert.Contains(Children((DependencyObject)list.Content).OfType<TextBlock>(),t=>t.Text.Contains("Game files connected"));Assert.Contains(Children((DependencyObject)list.Content).OfType<TextBlock>(),t=>t.Text.Contains("Not connected"));
                var create=Children((DependencyObject)list.Content).OfType<Button>().Single(b=>Equals(b.Content,"New checklist"));Assert.True(create.ActualHeight>=36);
            }
            finally{dashboard.Close();list.Close();}
        });
    }
    [Fact]
    public void DashboardDefinitionImportKeepsGameContextAndRequiresNoPersonalAccount()
    {
        var store=Store();var d=Definition("custom.imported");var path=Path.Combine(dir,"definition.json");File.WriteAllText(path,TrackerDefinitionTransfer.Serialize(d));
        AchievementCollectionAccessTests.OnSta(()=>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));var navigation=new TrackerNavigation(store,()=>new());
            navigation.ShowDefinitionImport=dialog=>false;Pump(navigation.ImportDefinitionAsync(null!,Game(),"origin",path));Assert.DoesNotContain(store.Definitions(),t=>t.Id==d.Id);
            navigation.ShowDefinitionImport=dialog=>{Assert.Equal("origin",dialog.BuildDefinition().AchievementId);return true;};Pump(navigation.ImportDefinitionAsync(null!,Game(),"origin",path));
            var saved=store.Definitions().Single(t=>t.Id==d.Id);Assert.Equal("origin",saved.AchievementId);Assert.All(store.Load(d.Id,""),s=>Assert.Null(s.Current));
            var dashboard=new TrackerDashboardWindow(null!,navigation);try{Render(dashboard,"tracker-dashboard-empty",512,460);var menu=Children((DependencyObject)dashboard.Content).OfType<Button>().Single(b=>Equals(b.Content,"Import tracker…")).ContextMenu;Assert.Contains(menu.Items.OfType<MenuItem>(),i=>Equals(i.Header,"Definition JSON…"));}finally{dashboard.Close();}
        });
    }
    [Fact]
    public async Task DashboardCreationOffersCachedAchievementChoicesAndPrefillsItsOrigin()
    {
        var store=Store();var row=new GameAchievementsFetchService.AchievementRow{Provider="steam",ProviderGameId="42",ProviderAchievementId="A",Title="Puzzle mastery",ProgressKnown=true};
        var hub=new AchievementHubStore(Path.Combine(dir,"achievement-hub"));await hub.SaveGameAsync("steam",Account,"42",new GameAchievementsFetchService.FetchResult{Rows=new(){row}},default);
        AchievementCollectionAccessTests.OnSta(()=>
        {
            var navigation=new TrackerNavigation(store,()=>Session(Game()));navigation.ReportError=(_,_,message)=>throw new Exception(message);var seen=false;navigation.ShowEditor=editor=>
            {
                var picker=Children((DependencyObject)editor.Content).OfType<TrackerAchievementPicker>().Single();Assert.Equal("A",picker.Achievement.ProviderAchievementId);Assert.Equal("Puzzle mastery",picker.Achievement.Title);seen=true;return false;
            };
            var count=store.Definitions().Count;navigation.Create(null!,game:Game(),achievement:"A");Assert.True(seen);Assert.Equal(count,store.Definitions().Count);
        });
    }
}
