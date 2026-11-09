using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using PixelVault.Tracking;
using Microsoft.Data.Sqlite;

namespace PixelVaultNative
{
    /// <summary>Verified Centurion collection with a selectable five-icon rail.</summary>
    sealed class GameProfileCenturyShelf : Border
    {
        const double BadgeWidth = 108;
        const double AchievementSize = 112;
        const double TileInset = 4;
        const double HeadingWidth = 260;

        readonly string provider, game;
        readonly Func<string> account;
        readonly AchievementTrackingStore store;
        readonly Action<GameAchievementsFetchService.AchievementRow, long> open;
        readonly StackPanel badgeSlot = new StackPanel();
        readonly UniformGrid slots = new UniformGrid
        {
            Columns = CenturyAchievementCatalog.PlannedPerGame,
            HorizontalAlignment = HorizontalAlignment.Left,
            MaxWidth = CenturyAchievementCatalog.PlannedPerGame * (AchievementSize + 2 * TileInset)
        };
        readonly TextBlock earned = new TextBlock { FontSize = 12, Margin = new Thickness(0,7,0,0) };
        readonly Grid layout = new Grid();
        readonly Grid heading = new Grid { VerticalAlignment = VerticalAlignment.Top };
        readonly StackPanel rail = new StackPanel();
        readonly Button selectedTitle = new Button { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0), HorizontalAlignment = HorizontalAlignment.Left, HorizontalContentAlignment = HorizontalAlignment.Left };
        readonly TextBlock requirement = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0,5,0,0) };
        readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        string signature, renderedAccount, selectedId;
        bool? compact;
        Dictionary<string, CenturyProgress> savedObservations = new Dictionary<string, CenturyProgress>();
        internal IReadOnlyList<GameAchievementsFetchService.AchievementRow> Rows { get; private set; } = Array.Empty<GameAchievementsFetchService.AchievementRow>();
        internal string SelectedId => selectedId;

        internal GameProfileCenturyShelf(string provider, string game, Func<string> account,
            AchievementTrackingStore store, Action<GameAchievementsFetchService.AchievementRow, long> open)
        {
            this.provider=provider;this.game=game;this.account=account;this.store=store;this.open=open;
            Background=Ink("#101A22",SystemColors.WindowBrush);CornerRadius=new CornerRadius(14);
            Padding=new Thickness(20,18,20,18);Margin=new Thickness(0,0,0,16);Tag="GameProfileCenturyShelf";
            layout.ColumnDefinitions.Add(new ColumnDefinition());layout.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
            layout.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });layout.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
            heading.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(BadgeWidth + 16) });heading.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(1,GridUnitType.Star) });
            heading.Children.Add(badgeSlot);
            var labels=new StackPanel { VerticalAlignment=VerticalAlignment.Center };
            labels.Children.Add(new TextBlock { Text="Centurion\nAchievements", FontSize=16, FontWeight=FontWeights.SemiBold, Foreground=Ink("#A3DCE5",SystemColors.WindowTextBrush) });
            earned.Foreground=Ink("#A6BBC8",SystemColors.WindowTextBrush);labels.Children.Add(earned);Grid.SetColumn(labels,1);heading.Children.Add(labels);
            rail.Children.Add(slots);
            var details=new StackPanel();details.Children.Add(selectedTitle);details.Children.Add(requirement);
            selectedTitle.Foreground=Ink("#EDF1F2",SystemColors.WindowTextBrush);selectedTitle.Cursor=System.Windows.Input.Cursors.Hand;
            AccessibilityUi.TryApplyFocusVisualStyle(selectedTitle);
            selectedTitle.Click+=(_,__)=>{Refresh();var row=Rows.FirstOrDefault(r=>r.CenturyDefinitionId==selectedId);if(row!=null){savedObservations.TryGetValue(selectedId,out var progress);open?.Invoke(row,progress?.VerifiedUtc??0);}};
            requirement.Foreground=Ink("#A6BBC8",SystemColors.WindowTextBrush);
            rail.Children.Add(new Border { BorderBrush=Ink("#33434E",SystemColors.WindowTextBrush),BorderThickness=new Thickness(0,1,0,0),Margin=new Thickness(4,17,4,0),Padding=new Thickness(0,13,0,0),Child=details });
            layout.Children.Add(heading);layout.Children.Add(rail);Child=layout;
            AutomationProperties.SetName(this,CenturyAchievementCatalog.GroupName);
            timer.Tick+=(_,__)=>Refresh();Loaded+=(_,__)=>{Refresh();if(IsVisible)timer.Start();};Unloaded+=(_,__)=>timer.Stop();
            IsVisibleChanged+=(_,__)=>{if(IsLoaded&&IsVisible)timer.Start();else timer.Stop();};Refresh();
        }
        protected override Size MeasureOverride(Size constraint)
        {
            // Stack the heading above the rail before it crowds the five tiles.
            var narrow = constraint.Width < HeadingWidth + slots.MaxWidth;
            if(compact!=narrow)
            {
                compact=narrow;layout.ColumnDefinitions[0].Width=new GridLength(narrow?0:HeadingWidth);
                Grid.SetColumnSpan(heading,narrow?2:1);heading.Margin=new Thickness(0,0,narrow?0:24,narrow?20:0);
                Grid.SetColumn(rail,narrow?0:1);Grid.SetColumnSpan(rail,narrow?2:1);Grid.SetRow(rail,narrow?1:0);
            }
            return base.MeasureOverride(constraint);
        }
        static Brush Ink(string color,Brush highContrast)=>SystemParameters.HighContrast?highContrast:UiBrushHelper.FromHex(color);
        internal void Refresh()
        {
            var definitions=CenturyAchievementCatalog.ForGame(provider,game);
            if(definitions.Count==0){Visibility=Visibility.Collapsed;Rows=Array.Empty<GameAchievementsFetchService.AchievementRow>();return;}
            Visibility=Visibility.Visible;
            var currentAccount=account?.Invoke();Dictionary<string,CenturyProgress> observations;
            try{observations=definitions.ToDictionary(d=>d.Id,d=>store?.Century(provider,game,d.Id,currentAccount));}
            catch(Exception ex) when(ex is IOException||ex is UnauthorizedAccessException||ex is SqliteException||ex is System.Text.Json.JsonException)
            {if(signature!=null&&renderedAccount==currentAccount)return;observations=definitions.ToDictionary(d=>d.Id,d=>(CenturyProgress)null);}
            var nextRows=definitions.Select(d=>AchievementsInfoWindow.CenturyPresentation(d,observations[d.Id])).ToArray();
            var nextSignature=currentAccount+"\n"+string.Join("\n",nextRows.Select(r=>r.CenturyDefinitionId+":"+r.ProgressKnown+":"+r.Unlocked+":"+r.UnlockUtcTicks+":"+r.CounterCurrent+":"+r.CounterTarget));
            if(signature==nextSignature)return;
            signature=nextSignature;renderedAccount=currentAccount;savedObservations=observations;Rows=nextRows;
            earned.Text=nextRows.Count(r=>r.ProgressKnown&&r.Unlocked).ToString(CultureInfo.CurrentCulture)+" / 5";
            RenderBadge(nextRows.Length==5&&nextRows.All(r=>r.ProgressKnown&&r.Unlocked));slots.Children.Clear();
            foreach(var definition in definitions)
            {
                var row=Rows.Single(r=>r.CenturyDefinitionId==definition.Id);
                var button=new Button { Background=Brushes.Transparent,BorderThickness=new Thickness(0),Padding=new Thickness(0),Margin=new Thickness(TileInset,0,TileInset,0),HorizontalContentAlignment=HorizontalAlignment.Stretch,Tag=row,Cursor=System.Windows.Input.Cursors.Hand,Content=Content(row,false) };
                if(!SystemParameters.HighContrast){var template=LibraryButtonChrome.BuildRoundedTileButtonTemplate();template.VisualTree.SetValue(Border.BackgroundProperty,Brushes.Transparent);template.VisualTree.SetValue(Border.BorderThicknessProperty,new Thickness(0));button.Template=template;}
                button.ToolTip=definition.Title+"\n"+definition.Requirement+"\n100 PV pts";
                AutomationProperties.SetName(button,definition.Title+", "+(row.ProgressKnown&&row.Unlocked?"earned":"not earned")+". Show requirements.");
                AccessibilityUi.TryApplyFocusVisualStyle(button);button.Click+=(_,__)=>Select(definition.Id);slots.Children.Add(button);
            }
            for(var i=definitions.Count;i<CenturyAchievementCatalog.PlannedPerGame;i++)
            {var placeholder=new Border {Padding=new Thickness(TileInset,0,TileInset,0),Child=Content(null,true),Tag="CenturyPlaceholder"};AutomationProperties.SetName(placeholder,"Unannounced Centurion achievement");slots.Children.Add(placeholder);}
            Select(definitions.Any(d=>d.Id==selectedId)?selectedId:definitions[0].Id);
        }
        internal void Select(string id)
        {
            var definition=CenturyAchievementCatalog.Find(provider,game,id);if(definition==null)return;selectedId=id;
            selectedTitle.Content=new TextBlock { Text=definition.Title+"  ↗",FontSize=14,FontWeight=FontWeights.SemiBold,TextWrapping=TextWrapping.Wrap };
            AutomationProperties.SetName(selectedTitle,"Open achievement details: "+definition.Title);requirement.Text=definition.Requirement;
            foreach(var button in slots.Children.OfType<Button>())
            {
                var row=(GameAchievementsFetchService.AchievementRow)button.Tag;var selected=row.CenturyDefinitionId==id;
                var frame=(Border)((Viewbox)button.Content).Child;frame.BorderBrush=Ink(selected?"#A3DCE5":row.ProgressKnown&&row.Unlocked?"#7BABB5":"#33434E",selected?SystemColors.HighlightBrush:SystemColors.WindowTextBrush);frame.BorderThickness=new Thickness(selected?2:1);
                AutomationProperties.SetHelpText(button,selected?"Selected":"Select to read requirements");
            }
        }
        void RenderBadge(bool unlocked)
        {
            const double strokeWidth = 2;
            double radius = (BadgeWidth - strokeWidth) / Math.Sqrt(3);
            double badgeHeight = 2 * radius + strokeWidth;
            badgeSlot.Children.Clear();
            var host = new Grid { Width = BadgeWidth, Height = badgeHeight, HorizontalAlignment = HorizontalAlignment.Left };
            if(unlocked&&CenturionRewards.HasBadge(provider,game))host.Children.Add(new Image {Stretch=Stretch.Uniform,Source=CenturionRewardWindow.Artwork(CenturionRewards.BadgeArtwork(provider,game))});
            else
            {
                var vertices = new PointCollection(Enumerable.Range(0, 6).Select(i =>
                    new Point(BadgeWidth / 2 + radius * Math.Sin(i * Math.PI / 3),
                        badgeHeight / 2 - radius * Math.Cos(i * Math.PI / 3))));
                host.Children.Add(new Polygon {Points=vertices,Stroke=Ink("#3C515D",SystemColors.WindowTextBrush),StrokeThickness=strokeWidth,Fill=Ink("#091219",SystemColors.WindowBrush)});
                host.Children.Add(new TextBlock {Text="◇",FontSize=18,Foreground=Ink("#8DA5B3",SystemColors.WindowTextBrush),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center});
            }
            AutomationProperties.SetName(host,unlocked?"Centurion badge earned":"Empty hexagonal badge slot. Collect all five achievements.");badgeSlot.Children.Add(host);
        }
        static FrameworkElement Content(GameAchievementsFetchService.AchievementRow row,bool placeholder)
        {
            var unlocked=row?.ProgressKnown==true&&row.Unlocked;
            var face=new Grid {ClipToBounds=true};var frame=new Border {Width=AchievementSize,Height=AchievementSize,CornerRadius=new CornerRadius(9),BorderThickness=new Thickness(1),Background=Ink("#091219",SystemColors.WindowBrush),Child=face};
            if(unlocked){var image=new Image {Stretch=Stretch.Uniform,Margin=new Thickness(3)};AchievementsInfoWindow.StartAchievementBadgeDownload(image,row,"PixelVault/Profile",256);face.Children.Add(image);}
            else
            {
                face.Children.Add(new Rectangle {Margin=new Thickness(6),RadiusX=5,RadiusY=5,Stroke=Ink("#273C49",SystemColors.WindowTextBrush),StrokeThickness=1,StrokeDashArray=new DoubleCollection {3,3}});
                var definition=row==null?null:CenturyAchievementCatalog.Find(row.Provider,row.ProviderGameId,row.CenturyDefinitionId);
                face.Children.Add(new TextBlock {Text=placeholder?"?":(definition?.Order??1).ToString("00"),FontSize=22,Foreground=Ink("#718995",SystemColors.WindowTextBrush),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center});
            }
            return new Viewbox {Child=frame,StretchDirection=StretchDirection.DownOnly,HorizontalAlignment=HorizontalAlignment.Center,MaxWidth=AchievementSize,MaxHeight=AchievementSize};
        }
    }

    public sealed partial class MainWindow
    {
        GameProfileCenturyShelf BuildLibraryGameProfileCenturyShelf(Window owner, LibraryFolderInfo folder)
        {
            string provider = null, gameId = null;
            if (GameAchievementsFetchService.TryPickAchievementsBackend(NormalizeConsoleLabel(folder?.PlatformLabel), folder, out var steam))
            { provider = steam ? "steam" : "retroachievements"; gameId = (steam ? folder?.SteamAppId : folder?.RetroAchievementsGameId)?.Trim(); }
            string Account() => provider == "steam" ? CurrentSteamUserId64() : provider == "retroachievements" ? CurrentRetroAchievementsUsername() : "";
            GameProfileCenturyShelf shelf = null;
            shelf = new GameProfileCenturyShelf(provider, gameId, Account, achievementTrackingStore, (row, savedUtc) =>
            {
                var game = new AchievementHubGame { Provider = row.Provider, Id = row.ProviderGameId, Title = folder?.Name, Folder = folder };
                new AchievementDetailWindow(owner, game, row, shelf.Rows, savedUtc, null,
                    trackingStore: achievementTrackingStore, trackingAccount: Account()).Show();
            });
            EventHandler activated = (_, __) => shelf.Refresh();
            owner.Activated += activated;
            owner.Closed += (_, __) => owner.Activated -= activated;
            return shelf;
        }
    }
}
