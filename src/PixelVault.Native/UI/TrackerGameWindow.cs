using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace PixelVaultNative
{
    sealed class TrackerGameWindow : Window
    {
        readonly TrackerNavigation navigation;
        readonly AchievementHubGame game;
        readonly string achievement;
        readonly StackPanel rows=new();
        readonly TextBlock status=AchievementTrackerWindow.Text("Loading trackers…",15);
        int generation;bool closed;
        internal Task PendingNavigation { get; private set; }=Task.CompletedTask;
        internal TrackerGameWindow(Window owner,TrackerNavigation navigation,AchievementHubGame game,string achievement=null)
        {
            this.navigation=navigation;this.game=game;this.achievement=achievement;owner=TrackerNavigation.BrowseOwner(owner);if(owner?.IsVisible==true)Owner=owner;
            Title=game.Title+" — Trackers";Width=820;Height=720;MinWidth=520;MinHeight=420;WindowStartupLocation=Owner==null?WindowStartupLocation.CenterScreen:WindowStartupLocation.CenterOwner;
            Background=SystemParameters.HighContrast?SystemColors.WindowBrush:AchievementTrackerWindow.Color("#11181D");
            var root=new DockPanel{Margin=new Thickness(24),Background=Background};Content=root;
            var header=new StackPanel();DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
            header.Children.Add(AchievementTrackerWindow.Text(game.Title,26,true));
            header.Children.Add(AchievementTrackerWindow.Text(achievement==null?"Choose a tracker. You can switch between this game's trackers after opening one.":"Trackers linked to this achievement.",16));
            var actions=new WrapPanel{Margin=new Thickness(0,12,0,8)};header.Children.Add(actions);
            var create=AchievementTrackerWindow.Button("New checklist","Create a tracker for this game");actions.Children.Add(create);create.Click+=async(_,__)=>{navigation.Create(this,game:game,achievement:achievement);await ReloadAsync();};
            actions.Children.Add(navigation.ImportButton(this,ReloadAsync,game,achievement));
            var footer=new DockPanel{Margin=new Thickness(0,12,0,0)};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
            var back=AchievementTrackerWindow.Button("Back","Return to the previous view");back.Click+=(_,__)=>Close();DockPanel.SetDock(back,Dock.Right);footer.Children.Add(back);footer.Children.Add(status);
            root.Children.Add(new ScrollViewer{Content=rows,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
            Loaded+=async(_,__)=>await ReloadAsync();Activated+=async(_,__)=>{if(IsLoaded&&!closed)await ReloadAsync();};Closed+=(_,__)=>{closed=true;generation++;};PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape)Close();};
            TrackerFormStyle.Apply(this);AchievementCollectionUi.ApplyHighContrast(this);
        }
        internal async Task ReloadAsync()
        {
            int run=++generation;var session=navigation.Session();
            // Keep this game's trackers accessible even if its library folder was removed after opening the dashboard.
            session=new AchievementHubSession{SteamAccount=session.SteamAccount,RetroAccount=session.RetroAccount,Games=(session.Games??new()).Concat(new[]{game}).GroupBy(g=>g.Key).Select(g=>g.First()).ToList()};
            try
            {
                var entries=await Task.Run(()=>TrackerDashboard.Load(navigation.Store,session,game));if(closed||run!=generation)return;
                var trackers=entries.FirstOrDefault(e=>e.Game.Key==game.Key)?.Trackers.Where(t=>achievement==null||t.Definition.AchievementId==achievement).ToList()??new();rows.Children.Clear();
                foreach(var tracker in trackers)
                {
                    var body=new StackPanel();body.Children.Add(AchievementTrackerWindow.Text(tracker.Definition.Title,19,true));
                    var link=AchievementTrackerWindow.Text(string.IsNullOrEmpty(tracker.Definition.AchievementId)?"Game-wide tracker":tracker.AchievementTitle??"Achievement linked",14);link.ToolTip=tracker.Definition.AchievementId;link.Margin=new Thickness(0,4,0,8);body.Children.Add(link);
                    body.Children.Add(AchievementTrackerWindow.Text(tracker.Progress,16,true));
                    body.Children.Add(new ProgressBar{Minimum=0,Maximum=100,Value=tracker.Percent,Height=6,Margin=new Thickness(0,7,0,8),Foreground=AchievementTrackerWindow.Color("#72BFC8"),Background=AchievementTrackerWindow.Color("#263C48")});
                    var source=AchievementTrackerWindow.Text(tracker.Connection.Label,14);source.ToolTip=tracker.Connection.Detail;body.Children.Add(source);
                    var button=AchievementTrackerWindow.Button(tracker.Definition.Title,"Open tracker: "+tracker.Definition.Title);button.Content=body;button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.Margin=new Thickness(0,0,0,10);button.Padding=new Thickness(16,12,16,12);button.Tag=tracker;
                    AutomationProperties.SetHelpText(button,tracker.Progress+". "+tracker.Connection.Label);
                    button.Click+=async(_,__)=>await(PendingNavigation=OpenTrackerAsync(tracker.Definition.Id));rows.Children.Add(button);
                }
                if(trackers.Count==0)rows.Children.Add(AchievementTrackerWindow.Text(achievement==null?"No trackers for this game yet.":"No trackers are linked to this achievement yet. Create one or link an existing tracker.",18));
                status.Text=trackers.Count+" tracker"+(trackers.Count==1?"":"s");
            }
            catch(Exception ex){if(!closed&&run==generation)status.Text="Could not load trackers: "+ex.Message;}
        }
        async Task OpenTrackerAsync(string id){navigation.OpenTracker(this,game,id,achievement);await ReloadAsync();}
    }
}
