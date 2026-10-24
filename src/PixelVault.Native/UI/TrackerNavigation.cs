using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace PixelVaultNative
{
    /// <summary>Shared navigation for the library dashboard, Hub and achievement links.</summary>
    sealed class TrackerNavigation
    {
        internal readonly AchievementTrackingStore Store;
        readonly Func<AchievementHubSession> getSession;
        internal readonly Func<AchievementHubGame,double,double,FrameworkElement> Cover;
        internal Action<AchievementTrackerWindow> ShowTracker=window=>window.Show();
        internal Action<TrackerGameWindow> ShowGame=window=>window.Show();
        internal Func<AchievementTrackerEditWindow,bool?> ShowEditor=window=>window.ShowDialog();
        internal Func<TrackerDefinitionImportWindow,bool?> ShowDefinitionImport=window=>window.ShowDialog();
        internal Action<Window,string,string> ReportError=(owner,title,message)=>
        {if(owner?.IsVisible==true)MessageBox.Show(owner,message,title,MessageBoxButton.OK,MessageBoxImage.Information);else MessageBox.Show(message,title,MessageBoxButton.OK,MessageBoxImage.Information);};
        internal TrackerNavigation(AchievementTrackingStore store,Func<AchievementHubSession> getSession,Func<AchievementHubGame,double,double,FrameworkElement> cover=null)
        {Store=store;this.getSession=getSession;Cover=cover;}
        internal AchievementHubSession Session()=>getSession?.Invoke()??new();
        // Browsing windows are siblings of one another. Closing an intermediate
        // picker must not disable or destroy a later window in the navigation path.
        internal static Window BrowseOwner(Window origin)
        {
            var owner=origin;while(owner?.Owner!=null)owner=owner.Owner;
            return owner is TrackerDashboardWindow or TrackerGameWindow or AchievementTrackerWindow?null:owner;
        }
        internal List<AchievementTrackerDefinition> Linked(AchievementHubGame game,string achievement)=>Store.Definitions()
            .Where(d=>d.Provider==game.Provider&&d.GameId==game.Id&&!string.IsNullOrEmpty(achievement)&&d.AchievementId==achievement).OrderBy(d=>d.Title).ToList();
        internal void OpenTracker(Window owner,AchievementHubGame game,string trackerId,string achievement=null)
        {
            var session=Session();var window=new AchievementTrackerWindow(owner,Store,session.SteamAccount,session.RetroAccount,session.Games,
                game.Provider,game.Id,achievement,trackerId:trackerId,browseGameTrackers:true);
            ShowTracker(window);
        }
        internal void OpenGame(Window owner,AchievementHubGame game,string achievement=null)=>ShowGame(new TrackerGameWindow(owner,this,game,achievement));
        internal void OpenAchievement(Window owner,AchievementHubGame game,string achievement)
        {
            var linked=Linked(game,achievement);
            if(linked.Count==1)OpenTracker(owner,game,linked[0].Id,achievement);
            else OpenGame(owner,game,achievement);
        }
        internal void Create(Window owner,bool spreadsheet=false,AchievementHubGame game=null,string achievement=null)
        {
            var session=Session();var games=(session.Games??new()).ToList();if(game!=null&&!games.Any(g=>g.Key==game.Key))games.Add(game);
            var choices=Store.AchievementChoices(session.SteamAccount,session.RetroAccount);
            var dialog=new AchievementTrackerEditWindow(owner,games,new AchievementTrackerDefinition { Provider=game?.Provider,GameId=game?.Id },achievement,choices,spreadsheet:spreadsheet);
            try
            {
                if(ShowEditor(dialog)!=true||dialog.Definition==null)return;
                var account=Session().Account(dialog.Definition.Provider);
                Store.AddSpreadsheetDefinition(dialog.Definition,account,dialog.InitialProgress,dialog.InitialObservedUtc,dialog.InitialFingerprint);
            }
            catch(Exception ex){ReportError(owner,"Could not create tracker",ex.Message);}
            finally{dialog.Close();}
        }
        internal Button ImportButton(Window owner,Func<Task> reload,AchievementHubGame game=null,string achievement=null)
        {
            var button=AchievementTrackerWindow.Button("Import tracker…","Import a spreadsheet or portable tracker definition");var menu=new ContextMenu();button.ContextMenu=menu;
            var sheet=new MenuItem{Header="Spreadsheet…"};sheet.Click+=async(_,__)=>{Create(owner,true,game,achievement);await reload();};menu.Items.Add(sheet);
            var definition=new MenuItem{Header="Definition JSON…"};definition.Click+=async(_,__)=>{await ImportDefinitionAsync(owner,game,achievement);await reload();};menu.Items.Add(definition);
            button.Click+=(_,__)=>{menu.PlacementTarget=button;menu.IsOpen=true;};return button;
        }
        internal async Task ImportDefinitionAsync(Window owner,AchievementHubGame game=null,string achievement=null,string path=null)
        {
            if(path==null){var file=new OpenFileDialog{Title="Import tracker definition",Filter="Tracker definition (*.json)|*.json",CheckFileExists=true};if(file.ShowDialog(owner)!=true)return;path=file.FileName;}
            try
            {
                var session=Session();var data=await Task.Run(()=>(Definition:TrackerDefinitionTransfer.Read(path),Existing:Store.Definitions().Select(d=>d.Id).ToList(),Achievements:Store.AchievementChoices(session.SteamAccount,session.RetroAccount)));
                var dialog=new TrackerDefinitionImportWindow(owner,data.Definition,data.Existing,data.Achievements,game?.Provider,game?.Id,achievement);
                try{if(ShowDefinitionImport(dialog)==true){var imported=dialog.Definition??dialog.BuildDefinition();await Task.Run(()=>Store.AddDefinition(imported));}}
                finally{dialog.Close();}
            }
            catch(Exception ex){ReportError(owner,"Could not import tracker",ex.Message);}
        }
    }
}
