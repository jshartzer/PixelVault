using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        List<AchievementHubGame> BuildTrackerLibraryGames(LibraryBrowserWorkingSet ws)
        {
            return BuildLibraryBrowserFolderViews(ws.Folders,"console").Select(BuildTrackerLibraryGame)
                .Where(g=>g!=null).GroupBy(g=>g.Key).Select(g=>g.First()).ToList();
        }
        AchievementHubGame BuildTrackerLibraryGame(LibraryBrowserFolderView view)
        {
            if(view==null||IsLibraryBrowserTimeProjectionView(view))return null;
            var folder=ResolveLibraryBrowserAchievementLookupFolder(view);
            if(!GameAchievementsFetchService.TryPickAchievementsBackend(NormalizeConsoleLabel(folder?.PlatformLabel),folder,out var steam))return null;
            var id=steam?folder.SteamAppId:folder.RetroAchievementsGameId;
            if(!int.TryParse(id,out var numeric)||numeric<=0)return null;
            return new AchievementHubGame{Provider=steam?"steam":"retroachievements",Id=numeric.ToString(System.Globalization.CultureInfo.InvariantCulture),Title=view.Name,Folder=folder,ArtPath=GetLibraryArtPathForDisplayOnly(BuildLibraryBrowserDisplayFolder(view))};
        }
        internal void LibraryBrowserShowGameTrackers(Window owner,LibraryBrowserFolderView view)
        {
            if(view==null||IsLibraryBrowserTimeProjectionView(view))return;
            var game=BuildTrackerLibraryGame(view);
            if(game==null)
            {
                MessageBox.Show(owner??this,"Set a Steam App ID or RetroAchievements game ID in Edit metadata to open this game's trackers.","Game trackers",MessageBoxButton.OK,MessageBoxImage.Information);
                return;
            }
            AchievementHubSession Session()=>new(){LibraryRoot=libraryRoot,SteamAccount=CurrentSteamUserId64(),RetroAccount=CurrentRetroAchievementsUsername(),Games=new(){game}};
            var navigation=new TrackerNavigation(achievementTrackingStore,Session,TrackerGameCover);
            var trackers=achievementTrackingStore.Definitions().Where(d=>d.Provider==game.Provider&&d.GameId==game.Id).ToList();
            if(trackers.Count==1)navigation.OpenTracker(owner,game,trackers[0].Id);
            else navigation.OpenGame(owner,game);
        }
        FrameworkElement TrackerGameCover(AchievementHubGame game,double width,double height)=>CreateAsyncImageTile(game.ArtPath,
            (int)Math.Ceiling(width*2.5),width,height,Stretch.UniformToFill,game.Title,Brushes.White,new Thickness(0),new Thickness(0),Brushes.Transparent,new CornerRadius(8),Brushes.Transparent,new Thickness(0));
        void ShowTrackerDashboard(Window owner,LibraryBrowserWorkingSet ws)
        {
            AchievementHubSession Session()=>new(){LibraryRoot=libraryRoot,SteamAccount=CurrentSteamUserId64(),RetroAccount=CurrentRetroAchievementsUsername(),Games=BuildTrackerLibraryGames(ws)};
            TrackerDashboardWindow.ShowFor(owner,new TrackerNavigation(achievementTrackingStore,Session,TrackerGameCover));
        }
        internal static void AddLibraryTrackerButton(Grid toolbar,LibraryBrowserPaneRefs panes)
        {
            panes.TrackersButton=LibraryButtonChrome.Btn("Trackers",null,"#20343A",Brushes.White);
            panes.TrackersButton.MinWidth=96;panes.TrackersButton.Height=34;panes.TrackersButton.FontSize=11.5;panes.TrackersButton.Padding=new Thickness(10,0,10,0);
            panes.TrackersButton.Margin=new Thickness(8,0,8,0);panes.TrackersButton.HorizontalAlignment=HorizontalAlignment.Center;
            panes.TrackersButton.ToolTip="Open the tracker dashboard";AutomationProperties.SetName(panes.TrackersButton,"Open tracker dashboard");
            if(!SystemParameters.HighContrast)panes.TrackersButton.Style=LibraryButtonChrome.LibraryToolbarButtonStyle("#232B35","#33424D","#2A3440","#182028");
            AccessibilityUi.TryApplyFocusVisualStyle(panes.TrackersButton);
            panes.SortFilterMenuButton.Margin=new Thickness(0);
            Grid.SetColumn(panes.TrackersButton,1);toolbar.Children.Add(panes.TrackersButton);
        }
    }
}
