using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Data.Sqlite;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class TrackerBrowsingTests : IDisposable
{
    readonly string dir=Path.Combine(Path.GetTempPath(),"PV-TrackerBrowsing-"+Guid.NewGuid().ToString("N"));
    static T Field<T>(object target,string name)=>(T)target.GetType().GetField(name,BindingFlags.NonPublic|BindingFlags.Instance)!.GetValue(target)!;
    static void Pump(Task task)
    {
        var frame=new DispatcherFrame();var dispatcher=Dispatcher.CurrentDispatcher;
        _=task.ContinueWith(_=>dispatcher.BeginInvoke(new Action(()=>frame.Continue=false)),TaskScheduler.Default);
        if(!task.IsCompleted)Dispatcher.PushFrame(frame);task.GetAwaiter().GetResult();
    }
    static void Park(Window window){window.WindowStartupLocation=WindowStartupLocation.Manual;window.Left=-30000;window.Top=-30000;window.ShowActivated=false;window.ShowInTaskbar=false;}
    static void PumpUntil(Func<bool> ready)
    {
        var completion=new TaskCompletionSource();var deadline=DateTime.UtcNow.AddSeconds(5);var timer=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(10)};
        timer.Tick+=(_,__)=>{if(ready()){timer.Stop();completion.SetResult();}else if(DateTime.UtcNow>=deadline){timer.Stop();completion.SetException(new TimeoutException("Tracker did not load."));}};
        timer.Start();try{Pump(completion.Task);}finally{timer.Stop();}
    }
    static string Subtotal(ListBox list,string category)
    {
        var group=((ListCollectionView)list.ItemsSource).Groups!.Cast<CollectionViewGroup>().Single(g=>Equals(g.Name,category));
        var host=new ContentControl{Content=group,ContentTemplate=list.GroupStyle[0].HeaderTemplate};
        host.Measure(new Size(800,100));host.Arrange(new Rect(0,0,800,100));host.UpdateLayout();
        return Visuals(host).OfType<TextBlock>().Last().Text;
    }
    static IEnumerable<DependencyObject> Visuals(DependencyObject root){yield return root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)foreach(var child in Visuals(VisualTreeHelper.GetChild(root,i)))yield return child;}
    public void Dispose()
    {
        var full=Path.GetFullPath(dir);Assert.StartsWith(Path.GetFullPath(Path.GetTempPath()),full,StringComparison.OrdinalIgnoreCase);
        for(int i=0;Directory.Exists(full);i++){SqliteConnection.ClearAllPools();try{Directory.Delete(full,true);}catch(IOException)when(i<5){Thread.Sleep(50);}}
    }
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void EarlierBrowsingWindowsCanCloseWhileLaterWindowsRemainUsable(bool withRoot)
    {
        var store=new AchievementTrackingStore(dir);store.AddDefinition(new(){Id="custom.rooms",Provider="steam",GameId="42",GameTitle="Room quest",Title="Explore rooms",Items=new(){new(){Id="first",Name="First room"}}});
        AchievementCollectionAccessTests.OnSta(()=>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var game=new AchievementHubGame{Provider="steam",Id="42",Title="Room quest"};
            var navigation=new TrackerNavigation(store,()=>new(){SteamAccount="player",Games=new(){game}});
            Window? root=withRoot?new Window{Width=100,Height=100}:null;TrackerDashboardWindow? dashboard=null;TrackerGameWindow? picker=null;AchievementTrackerWindow? tracker=null;
            bool modal=false;
            // Run the real default show actions. A guard closes a regressed modal
            // window so a failing test reports the problem instead of deadlocking.
            void Show(Window window,Action action)
            {
                Park(window);bool returned=false;var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};
                timer.Tick+=(_,__)=>{timer.Stop();if(!returned){modal=true;window.Close();}};
                timer.Start();try{action();returned=true;}finally{timer.Stop();}
            }
            var showGame=navigation.ShowGame;navigation.ShowGame=w=>{picker=w;Show(w,()=>showGame(w));};
            var showTracker=navigation.ShowTracker;navigation.ShowTracker=w=>{tracker=w;Show(w,()=>showTracker(w));};
            try
            {
                if(root!=null){Park(root);root.Show();}
                dashboard=new TrackerDashboardWindow(root!,navigation);Park(dashboard);dashboard.Show();
                navigation.OpenGame(dashboard,game);Assert.False(modal);Assert.NotNull(picker);
                navigation.OpenTracker(picker!,game,"custom.rooms");Assert.False(modal);Assert.NotNull(tracker);PumpUntil(()=>Field<ListBox>(tracker!,"list").Items.Count==1);
                Assert.Same(root,picker!.Owner);Assert.Same(root,tracker.Owner);
                dashboard.Close();Assert.True(picker.IsVisible);Assert.True(tracker.IsVisible);
                picker.Close();Assert.True(tracker.IsVisible);Assert.True(tracker.IsEnabled);
                Assert.Single(Field<ListBox>(tracker,"list").Items);Field<TextBox>(tracker,"notes").Text="Still editable after closing pickers";
                Assert.Equal("Still editable after closing pickers",Field<TextBox>(tracker,"notes").Text);
                if(root!=null){root.Close();Assert.False(tracker.IsVisible);}
            }
            finally{tracker?.Close();picker?.Close();dashboard?.Close();root?.Close();}
        });
    }
    [Theory]
    [InlineData(732)]
    [InlineData(1172)]
    public void OrderGroupingTogglesDeliveryTypesWithoutChangingRanksFiltersOrNoteDrafts(int width)
    {
        var store=new AchievementTrackingStore(dir);const string id=AchievementTrackingCatalog.DeathStrandingOrdersId;
        store.SetManualProgress(id,"player","212002:LLL",1);int history=store.History(id,"player").Count;
        AchievementCollectionAccessTests.OnSta(()=>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
            var window=new AchievementTrackerWindow(null!,store,"player","",provider:"steam",gameId:"1850570",trackerId:id);
            try
            {
                Pump(window.LoadDefinitionsAsync());var list=Field<ListBox>(window,"list");var option=Field<CheckBox>(window,"groupByCategory");
                Assert.Equal(Visibility.Visible,option.Visibility);Assert.True(option.IsChecked);Assert.Equal(540,list.Items.Count);
                var view=Assert.IsType<ListCollectionView>(list.ItemsSource);var groups=view.Groups!.Cast<CollectionViewGroup>().ToDictionary(g=>(string)g.Name,g=>g.ItemCount);
                Assert.Equal(223,groups["Condition"]);Assert.Equal(186,groups["Quantity"]);Assert.Equal(66,groups["Time"]);Assert.Equal(65,groups["Miscellaneous"]);
                Assert.Equal("1 / 66 complete · Goal 1 / 20",Subtotal(list,"Time"));Assert.Equal("0 / 223 complete · Goal 0 / 20",Subtotal(list,"Condition"));
                var summary=Field<TextBlock>(window,"summary").Text;var selected=Field<AchievementTrackerState>(window,"selectedItem").Item.Id;
                Field<TextBox>(window,"notes").Text="Unsaved route note";
                option.IsChecked=false;Assert.Empty(((ListCollectionView)list.ItemsSource).GroupDescriptions);Assert.Equal(540,list.Items.Count);
                Assert.Equal(selected,Field<AchievementTrackerState>(window,"selectedItem").Item.Id);Assert.Equal("Unsaved route note",Field<TextBox>(window,"notes").Text);
                option.IsChecked=true;Assert.Equal(4,((ListCollectionView)list.ItemsSource).Groups!.Count);Assert.Equal("Unsaved route note",Field<TextBox>(window,"notes").Text);
                Field<ComboBox>(window,"category").SelectedItem="Time";Assert.Equal(66,list.Items.Count);
                Field<ComboBox>(window,"filter").SelectedIndex=3;Assert.Single(list.Items);Assert.Equal(summary,Field<TextBlock>(window,"summary").Text);Assert.Equal("1 / 66 complete · Goal 1 / 20",Subtotal(list,"Time"));
                Field<ComboBox>(window,"category").SelectedIndex=0;Field<ComboBox>(window,"filter").SelectedIndex=0;
                var root=(FrameworkElement)window.Content;root.Measure(new Size(width,742));root.Arrange(new Rect(0,0,width,742));root.UpdateLayout();
                Assert.True(option.ActualHeight>0);Assert.InRange(option.TranslatePoint(new Point(),root).Y,0,root.ActualHeight);
                var output=Environment.GetEnvironmentVariable("PIXELVAULT_TRACKING_REVIEW_OUTPUT");
                if(!string.IsNullOrWhiteSpace(output))
                {
                    Directory.CreateDirectory(output);var bitmap=new RenderTargetBitmap(width,742,96,96,PixelFormats.Pbgra32);bitmap.Render(root);
                    var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using var file=File.Create(Path.Combine(output,"tracker-order-grouping-"+width+".png"));encoder.Save(file);
                }
            }
            finally{window.Close();}
        });
        Assert.Equal(history,store.History(id,"player").Count);Assert.True(store.Load(id,"player").Single(s=>s.Item.Id=="212002:LLL").Complete);
        Assert.Null(store.Load(id,"player").Single(s=>s.Item.Id=="212002:LL").Current);Assert.All(store.Load(id,"player"),s=>Assert.Empty(s.Note));
    }
    [Fact]
    public void CategoryGroupingIsSharedWithOtherGamesAndHiddenWithoutCategoryMetadata()
    {
        var store=new AchievementTrackingStore(dir);var d=new AchievementTrackerDefinition{Id="custom.race",Provider="retroachievements",GameId="race-7",GameTitle="Circuit tour",Title="Courses",Items=new(){new(){Id="one",Name="Coast",Category="Sprints",Target=5},new(){Id="two",Name="Summit",Category="Circuits"}}};store.AddDefinition(d);
        AchievementCollectionAccessTests.OnSta(()=>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));var window=new AchievementTrackerWindow(null!,store,"","player",provider:d.Provider,gameId:d.GameId);
            try
            {
                Pump(window.LoadDefinitionsAsync());var option=Field<CheckBox>(window,"groupByCategory");var list=Field<ListBox>(window,"list");Assert.Equal(Visibility.Visible,option.Visibility);
                Field<TextBox>(window,"current").Text="3";option.IsChecked=false;Assert.Equal("3",Field<TextBox>(window,"current").Text);Assert.Equal(2,list.Items.Count);Assert.Empty(((ListCollectionView)list.ItemsSource).GroupDescriptions);
                option.IsChecked=true;Assert.Equal(2,((ListCollectionView)list.ItemsSource).Groups!.Count);
            }
            finally{window.Close();}
            d.Id="custom.plain";d.Items.ForEach(i=>i.Category=null!);store.AddDefinition(d);
            var plain=new AchievementTrackerWindow(null!,store,"","player",provider:d.Provider,gameId:d.GameId,trackerId:d.Id);
            try{Pump(plain.LoadDefinitionsAsync());Assert.Equal(Visibility.Collapsed,Field<CheckBox>(plain,"groupByCategory").Visibility);}
            finally{plain.Close();}
        });
    }
    [Fact]
    public void CategorySubtotalsCountEachMilestoneRecordOnceAndKeepGoalCapsSeparateFromFullTotals()
    {
        var store=new AchievementTrackingStore(dir);
        var d=new AchievementTrackerDefinition{Id="custom.secrets",Provider="steam",GameId="77",GameTitle="Mountain tour",Title="Find secrets",Milestones=new(){"Found","Perfect"},CategoryTargets=new(){{"Courses",1}}};
        d.Items=TrackerMilestones.Expand(new[]{new AchievementTrackerItem{Id="one",Name="Coast",Category="Courses"},new AchievementTrackerItem{Id="two",Name="Summit",Category="Courses"},new AchievementTrackerItem{Id="three",Name="Cabin"}},d.Milestones);store.AddDefinition(d);
        foreach(var item in d.Items.Where(i=>i.GroupId is "one" or "two"))store.SetManualProgress(d.Id,"player",item.Id,1);
        AchievementCollectionAccessTests.OnSta(()=>
        {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));var window=new AchievementTrackerWindow(null!,store,"player","",provider:d.Provider,gameId:d.GameId);
            try
            {
                Pump(window.LoadDefinitionsAsync());var list=Field<ListBox>(window,"list");Assert.Equal(3,list.Items.Count);
                Assert.Equal("2 / 2 complete · Goal 1 / 1",Subtotal(list,"Courses"));Assert.Equal("0 / 1 complete",Subtotal(list,"Other"));
                Field<TextBox>(window,"search").Text="Summit";Assert.Single(list.Items);Assert.Equal("2 / 2 complete · Goal 1 / 1",Subtotal(list,"Courses"));
            }
            finally{window.Close();}
        });
    }
}
