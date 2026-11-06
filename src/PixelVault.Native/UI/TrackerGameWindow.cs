using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Markup;

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
        internal Task PendingIcon { get; private set; }=Task.CompletedTask;
        internal Func<TrackerIconPicker,bool?> ShowIconPicker=dialog=>dialog.ShowDialog();
        internal Task PendingNavigation { get; private set; }=Task.CompletedTask;
        internal Task PendingDelete { get; private set; }=Task.CompletedTask;
        internal Func<TrackerDeletionPreview,bool> ConfirmDeletion;
        internal TrackerGameWindow(Window owner,TrackerNavigation navigation,AchievementHubGame game,string achievement=null)
        {
            this.navigation=navigation;this.game=game;this.achievement=achievement;owner=TrackerNavigation.BrowseOwner(owner);if(owner?.IsVisible==true)Owner=owner;
            ConfirmDeletion=preview=>TrackerDeletionUi.Confirm(this,preview);
            Title=game.Title+" — Trackers";Width=820;Height=720;MinWidth=520;MinHeight=420;WindowStartupLocation=Owner==null?WindowStartupLocation.CenterScreen:WindowStartupLocation.CenterOwner;
            Background=SystemParameters.HighContrast?SystemColors.WindowBrush:AchievementTrackerWindow.Color("#11181D");
            var root=new DockPanel{Margin=new Thickness(24),Background=Background};Content=root;
            var header=new StackPanel();DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
            header.Children.Add(AchievementTrackerWindow.Text(game.Title,26,true));
            header.Children.Add(AchievementTrackerWindow.Text(achievement==null?"Choose a tracker to open its workspace.":"Trackers linked to this achievement.",16));
            var actions=new WrapPanel{Margin=new Thickness(0,12,0,8)};header.Children.Add(actions);
            var create=AchievementTrackerWindow.Button("New checklist","Create a tracker for this game");actions.Children.Add(create);create.Click+=async(_,__)=>{navigation.Create(this,game:game,achievement:achievement);await ReloadAsync();};
            actions.Children.Add(navigation.ImportButton(this,ReloadAsync,game,achievement));
            var orderButton=AchievementTrackerWindow.Button("Order groups…","Choose the shared tracker group order for PC and web");actions.Children.Add(orderButton);
            var orderMenu=new ContextMenu();orderButton.Click+=(_,__)=>{orderMenu.PlacementTarget=orderButton;orderMenu.IsOpen=true;};
            orderMenu.Opened+=(_,__)=>
            {
                orderMenu.Items.Clear();var order=navigation.Store.TrackerGroupOrder();
                foreach(var id in order)
                {
                    var groupMenu=new MenuItem{Header=TrackerRepository.TrackerGroupTitle(id)};orderMenu.Items.Add(groupMenu);
                    foreach(var direction in new[]{-1,1})
                    {
                        var move=new MenuItem{Header=direction<0?"Move up":"Move down",IsEnabled=direction<0?id!=order[0]:id!=order[order.Count-1]};groupMenu.Items.Add(move);
                        move.Click+=async(_,__)=>{try{await Task.Run(()=>navigation.Store.MoveTrackerGroup(id,direction));await ReloadAsync();}catch(Exception ex){status.Text="Could not save group order: "+ex.Message;}};
                    }
                }
            };

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
                var groupOrder=(await Task.Run(()=>navigation.Store.TrackerGroupOrder())).ToList();
                var entries=await Task.Run(()=>TrackerDashboard.Load(navigation.Store,session,game));if(closed||run!=generation)return;
                var trackers=entries.FirstOrDefault(e=>e.Game.Key==game.Key)?.Trackers.Where(t=>achievement==null||t.Definition.AchievementId==achievement)
                    .OrderBy(t=>groupOrder.IndexOf(TrackerRepository.TrackerGroup(t.Definition))).ThenBy(t=>t.Complete).ThenBy(t=>t.Definition.Title,StringComparer.CurrentCultureIgnoreCase).ThenBy(t=>t.Definition.Id,StringComparer.Ordinal).ToList()??new();rows.Children.Clear();
                int completed=trackers.Count(t=>t.Complete);bool completedHeading=false;string currentSection=null;
                foreach(var tracker in trackers)
                {
                    var group=TrackerRepository.TrackerGroup(tracker.Definition);
                    if(currentSection!=group)
                    {
                        currentSection=group;completedHeading=false;
                        {
                            var section=AchievementTrackerWindow.Text(TrackerRepository.TrackerGroupTitle(group)+" · "+trackers.Count(t=>TrackerRepository.TrackerGroup(t.Definition)==group),18,true);
                            section.Margin=new Thickness(0,rows.Children.Count==0?0:20,0,12);
                            section.Tag=group+"TrackerSection";
                            if(group!="manual"&&!SystemParameters.HighContrast)section.Foreground=AchievementTrackerWindow.Color("#A3DCE5");
                            AutomationProperties.SetHeadingLevel(section,AutomationHeadingLevel.Level2);rows.Children.Add(section);
                        }
                    }
                    if(tracker.Complete&&!completedHeading)
                    {
                        var heading=AchievementTrackerWindow.Text("✓ Completed · "+trackers.Count(t=>TrackerRepository.TrackerGroup(t.Definition)==group&&t.Complete),16,true);heading.Margin=new Thickness(0,12,0,10);
                        if(!SystemParameters.HighContrast)heading.Foreground=AchievementTrackerWindow.Color("#C1E8B3");
                        AutomationProperties.SetHeadingLevel(heading,AutomationHeadingLevel.Level3);rows.Children.Add(heading);completedHeading=true;
                    }
                    var body=new StackPanel();var title=AchievementTrackerWindow.Text(tracker.Definition.Title,19,true);title.MaxHeight=48;title.TextTrimming=TextTrimming.CharacterEllipsis;title.ToolTip=tracker.Definition.Title;body.Children.Add(title);
                    if(tracker.Complete&&!SystemParameters.HighContrast)title.Foreground=AchievementTrackerWindow.Color("#C8EAC0");
                    var link=AchievementTrackerWindow.Text(string.IsNullOrEmpty(tracker.Definition.AchievementId)?"Game-wide tracker":tracker.AchievementTitle??"Achievement linked",14);link.ToolTip=link.Text;link.TextWrapping=TextWrapping.NoWrap;link.TextTrimming=TextTrimming.CharacterEllipsis;link.Margin=new Thickness(0,4,0,8);body.Children.Add(link);
                    var progressText=AchievementTrackerWindow.Text(tracker.Progress+(tracker.Complete?"  ✓":""),16,true);progressText.TextWrapping=TextWrapping.NoWrap;progressText.TextTrimming=TextTrimming.CharacterEllipsis;progressText.ToolTip=tracker.Progress;body.Children.Add(progressText);
                    if(tracker.Complete&&!SystemParameters.HighContrast)progressText.Foreground=AchievementTrackerWindow.Color("#C1E8B3");
                    body.Children.Add(new ProgressBar{Minimum=0,Maximum=100,Value=tracker.Percent,Height=6,Margin=new Thickness(0,7,0,8),Foreground=SystemParameters.HighContrast?SystemColors.HighlightBrush:AchievementTrackerWindow.Color(tracker.Complete?"#C1E8B3":"#72BFC8"),Background=SystemParameters.HighContrast?SystemColors.ControlBrush:AchievementTrackerWindow.Color("#263C48")});
                    var source=AchievementTrackerWindow.Text(tracker.Connection.Label,14);source.ToolTip=tracker.Connection.Detail;source.MaxHeight=38;source.TextTrimming=TextTrimming.CharacterEllipsis;body.Children.Add(source);
                    var card=new Grid();card.ColumnDefinitions.Add(new ColumnDefinition{Width=GridLength.Auto});card.ColumnDefinitions.Add(new ColumnDefinition());
                    var icon=TrackerIconVisual.Create(tracker.Definition.IconId,tracker.Achievement,System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(navigation.Store.DatabasePath)));icon.Width=148;card.Children.Add(icon);
                    body.Margin=new Thickness(16,12,16,12);Grid.SetColumn(body,1);card.Children.Add(body);
                    body.SizeChanged+=(_,e)=>icon.Width=e.NewSize.Height+24;
                    var button=AchievementTrackerWindow.Button(tracker.Definition.Title,"Open tracker: "+tracker.Definition.Title);button.Content=card;button.VerticalContentAlignment=VerticalAlignment.Stretch;button.HorizontalContentAlignment=HorizontalAlignment.Stretch;button.Margin=new Thickness(0,0,0,10);button.Padding=new Thickness(0);button.Tag=tracker;
                    if(tracker.Complete&&!SystemParameters.HighContrast)
                    {
                        button.Background=AchievementTrackerWindow.Color("#203526");button.BorderBrush=AchievementTrackerWindow.Color("#76976B");
                        button.Style=LibraryButtonChrome.LibraryToolbarButtonStyle("#203526","#76976B","#2B4632","#17291D");
                        var template=LibraryButtonChrome.BuildLibraryToolbarButtonTemplate("#203526","#76976B","#2B4632","#17291D");
                        template.VisualTree.FirstChild.SetValue(FrameworkElement.MarginProperty,new TemplateBindingExtension(Control.PaddingProperty));
                        template.VisualTree.FirstChild.SetValue(FrameworkElement.HorizontalAlignmentProperty,new TemplateBindingExtension(Control.HorizontalContentAlignmentProperty));button.Template=template;
                    }
                    button.ContextMenu=TrackerIconVisual.Menu(()=>PendingIcon=SelectIconAsync(tracker));
                    if(navigation.Store.CanEditCatalogue(tracker.Definition.Id))
                    {
                        var delete=new MenuItem{Header="Delete tracker…"};button.ContextMenu.Items.Add(delete);
                        delete.Click+=(_,__)=>PendingDelete=DeleteTrackerAsync(tracker.Definition.Id);
                        button.ContextMenu.Opened+=(_,__)=>delete.IsEnabled=PendingDelete.IsCompleted;
                    }
                    AutomationProperties.SetHelpText(button,(tracker.Complete?"Completed. ":"In progress. ")+tracker.Progress+". "+tracker.Connection.Label);
                    button.Click+=async(_,__)=>await(PendingNavigation=OpenTrackerAsync(tracker.Definition.Id));rows.Children.Add(button);
                }
                if(trackers.Count==0)rows.Children.Add(AchievementTrackerWindow.Text(achievement==null?"No trackers for this game yet.":"No trackers are linked to this achievement yet. Create one or link an existing tracker.",18));
                status.Text=trackers.Count+" tracker"+(trackers.Count==1?"":"s")+(completed>0?" · "+completed+" completed":"");
            }
            catch(Exception ex){if(!closed&&run==generation)status.Text="Could not load trackers: "+ex.Message;}
        }
        async Task SelectIconAsync(TrackerDashboardItem tracker)
        {
            var dialog=new TrackerIconPicker(this,tracker.Definition.IconId,tracker.Achievement);
            try
            {
                if(ShowIconPicker(dialog)!=true)return;
                await Task.Run(()=>navigation.Store.SetTrackerIcon(tracker.Definition.Id,dialog.SelectedIconId));await ReloadAsync();
            }
            catch(Exception ex){status.Text="Could not save tracker image: "+ex.Message;}
            finally{dialog.Close();}
        }
        async Task OpenTrackerAsync(string id){navigation.OpenTracker(this,game,id,achievement);await ReloadAsync();}
        internal async Task DeleteTrackerAsync(string id)
        {
            try
            {
                var preview=await Task.Run(()=>navigation.Store.PrepareDeletion(id));
                if(closed||!ConfirmDeletion(preview))return;
                var backup=await Task.Run(()=>navigation.Store.DeleteCustomTracker(preview));
                if(closed)return;await ReloadAsync();status.Text="Tracker deleted. Recovery copy saved.";status.ToolTip=backup;
            }
            catch(Exception ex){if(!closed)status.Text="Could not delete tracker: "+ex.Message;}
        }
    }
}
