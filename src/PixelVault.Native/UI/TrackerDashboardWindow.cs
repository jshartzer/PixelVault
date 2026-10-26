using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PixelVaultNative
{
    sealed class TrackerDashboardWindow : Window
    {
        readonly TrackerNavigation navigation;
        readonly WrapPanel games=new();
        readonly TextBlock feedback=AchievementTrackerWindow.Text("Loading tracked games…",15);
        readonly TextBox search=new(){Padding=new Thickness(10,7,10,7),Margin=new Thickness(0,8,0,16)};
        List<TrackerDashboardGame> entries=new();
        int generation;bool closed;
        internal IReadOnlyList<TrackerDashboardGame> Games=>entries;
        internal Task PendingNavigation { get; private set; }=Task.CompletedTask;
        internal static void ShowFor(Window owner,TrackerNavigation navigation)
        {
            owner=TrackerNavigation.BrowseOwner(owner);
            var existing=owner?.OwnedWindows.OfType<TrackerDashboardWindow>().FirstOrDefault();
            if(existing!=null){if(existing.WindowState==WindowState.Minimized)existing.WindowState=WindowState.Normal;existing.Activate();return;}
            new TrackerDashboardWindow(owner,navigation).Show();
        }
        internal TrackerDashboardWindow(Window owner,TrackerNavigation navigation)
        {
            this.navigation=navigation;owner=TrackerNavigation.BrowseOwner(owner);if(owner?.IsVisible==true)Owner=owner;
            Title="Tracker dashboard";Width=1040;Height=780;MinWidth=560;MinHeight=460;
            WindowStartupLocation=Owner==null?WindowStartupLocation.CenterScreen:WindowStartupLocation.CenterOwner;
            Background=SystemParameters.HighContrast?SystemColors.WindowBrush:AchievementTrackerWindow.Color("#11181D");
            var root=new DockPanel{Margin=new Thickness(24),Background=Background};Content=root;
            var header=new StackPanel();DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
            header.Children.Add(AchievementTrackerWindow.Text("Trackers",28,true));
            header.Children.Add(AchievementTrackerWindow.Text("Local desktop trackers · choose a game to see its trackers.",16));
            var actions=new WrapPanel{Margin=new Thickness(0,12,0,0)};header.Children.Add(actions);
            var create=AchievementTrackerWindow.Button("New checklist","Create a tracker for any game");actions.Children.Add(create);
            var import=navigation.ImportButton(this,ReloadAsync);actions.Children.Add(import);
            var reload=AchievementTrackerWindow.Button("Update dashboard","Reload saved tracker progress without reading game files");actions.Children.Add(reload);
            var companion=AchievementTrackerWindow.Button("Open Companion…","Sign into the prepared Companion workspace shared with your browser");actions.Children.Add(companion);
            companion.Click+=(_,__)=>new CompanionWindow(this,CompanionDesktopSettings.DefaultRoot).Show();
            create.Click+=async(_,__)=>{navigation.Create(this);await ReloadAsync();};reload.Click+=async(_,__)=>await ReloadAsync();
            AchievementTrackerWindow.DressEditor(search);search.ToolTip="Search tracked games";AutomationProperties.SetName(search,"Search tracked games");
            var searchHost=new Grid();searchHost.Children.Add(search);var hint=AchievementTrackerWindow.Text("Search games…",15);hint.Margin=new Thickness(12,0,0,8);hint.VerticalAlignment=VerticalAlignment.Center;hint.IsHitTestVisible=false;searchHost.Children.Add(hint);header.Children.Add(searchHost);
            search.TextChanged+=(_,__)=>{hint.Visibility=search.Text.Length==0?Visibility.Visible:Visibility.Collapsed;Render();};
            var footer=new DockPanel{Margin=new Thickness(0,12,0,0)};DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);
            var close=AchievementTrackerWindow.Button("Close","Close tracker dashboard");close.Click+=(_,__)=>Close();DockPanel.SetDock(close,Dock.Right);footer.Children.Add(close);footer.Children.Add(feedback);
            root.Children.Add(new ScrollViewer{Content=games,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
            Loaded+=async(_,__)=>await ReloadAsync();Activated+=async(_,__)=>{if(IsLoaded)await ReloadAsync();};Closed+=(_,__)=>{closed=true;generation++;};
            PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape)Close();if(e.Key==Key.F&&Keyboard.Modifiers==ModifierKeys.Control){search.Focus();search.SelectAll();e.Handled=true;}};
            TrackerFormStyle.Apply(this);AchievementCollectionUi.ApplyHighContrast(this);
        }
        internal async Task ReloadAsync()
        {
            int run=++generation;var session=navigation.Session();
            try{var loaded=await Task.Run(()=>TrackerDashboard.Load(navigation.Store,session));if(closed||run!=generation)return;entries=loaded;Render();}
            catch(Exception ex){if(!closed&&run==generation)feedback.Text="Could not load trackers: "+ex.Message;}
        }
        void Render()
        {
            games.Children.Clear();var visible=entries.Where(e=>(e.Game.Title??"").Contains(search.Text.Trim(),StringComparison.CurrentCultureIgnoreCase)).ToList();
            foreach(var entry in visible)
            {
                const double width=132,height=192;var body=new StackPanel{Width=width,Margin=new Thickness(10)};
                body.Children.Add(navigation.Cover?.Invoke(entry.Game,width,height)??Placeholder(entry.Game,width,height));
                var name=AchievementTrackerWindow.Text(entry.Game.Title,15,true);name.Height=42;name.TextTrimming=TextTrimming.CharacterEllipsis;name.Margin=new Thickness(0,8,0,4);body.Children.Add(name);
                body.Children.Add(AchievementTrackerWindow.Text(entry.Game.Provider=="steam"?"Steam":"RetroAchievements",14));
                var counts=AchievementTrackerWindow.Text(entry.Summary,14);counts.FontSize=12.5;counts.TextWrapping=TextWrapping.NoWrap;counts.TextTrimming=TextTrimming.CharacterEllipsis;counts.ToolTip=entry.Summary;counts.Margin=new Thickness(0,6,0,4);body.Children.Add(counts);
                var completion=AchievementTrackerWindow.Text(entry.Percent.HasValue?entry.Percent.Value.ToString("0.#",System.Globalization.CultureInfo.CurrentCulture)+"% completion":"Completion unavailable",14,true);
                completion.Margin=new Thickness(0,0,0,5);completion.ToolTip="Completed tracker goals divided by all active tracker goals for this game. Category goals retain their existing caps; unconfirmed progress is not counted as completed.";body.Children.Add(completion);
                var source=AchievementTrackerWindow.Text(entry.Sources,14);source.Height=56;body.Children.Add(source);
                var card=AchievementTrackerWindow.Button(entry.Game.Title,"Open "+entry.Game.Title+" trackers");card.Content=body;card.Padding=new Thickness(0);card.Margin=new Thickness(0,0,14,14);card.VerticalContentAlignment=VerticalAlignment.Top;card.Tag=entry;
                AutomationProperties.SetHelpText(card,entry.Summary+". "+completion.Text+". "+entry.Sources);
                card.Click+=async(_,__)=>await(PendingNavigation=OpenGameAsync(entry.Game));games.Children.Add(card);
            }
            if(visible.Count==0)games.Children.Add(AchievementTrackerWindow.Text(entries.Count==0?"No tracked games yet. Create or import a tracker to get started.":"No tracked games match your search.",18));
            feedback.Text=visible.Count+" game"+(visible.Count==1?"":"s")+" · "+visible.Sum(e=>e.Trackers.Count)+" trackers";
        }
        async Task OpenGameAsync(AchievementHubGame game){navigation.OpenGame(this,game);await ReloadAsync();}
        internal static FrameworkElement Placeholder(AchievementHubGame game,double width,double height)=>new Border{Width=width,Height=height,CornerRadius=new CornerRadius(8),Background=AchievementTrackerWindow.Color("#243640"),Child=new TextBlock{Text=game.Title,Foreground=SystemParameters.HighContrast?SystemColors.WindowTextBrush:Brushes.White,TextWrapping=TextWrapping.Wrap,Margin=new Thickness(12),VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center,FontSize=18}};
    }
}
