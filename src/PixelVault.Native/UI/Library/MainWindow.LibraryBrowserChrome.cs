using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        internal sealed class LibraryBrowserNavChrome
        {
            internal Border NavBar;
            internal Button ImportButton;
            internal Button ImportCommentsButton;
            internal Button ManualImportButton;
            internal Button SettingsButton;
            internal Button GameIndexButton;
            internal Button PhotoIndexButton;
            internal Button PhotographyGalleryButton;
            internal Button MyCoversButton;
            internal Button ExportStarredButton;
            internal Button RefreshButton;
            internal Button IntakeReviewButton;
            internal Border IntakeReviewBadge;
            internal TextBlock IntakeReviewBadgeText;
            internal ScrollViewer HeaderActionsScrollViewer;
            internal Border HeaderScrollLeftNudge;
            internal Border HeaderScrollRightNudge;
            internal DispatcherTimer HeaderScrollHoverTimer;
            internal int HeaderScrollHoverDirection;
        }

        Window GetOrCreateLibraryBrowserWindow(bool reuseMainWindow)
        {
            if (reuseMainWindow) return this;
            var libraryWindow = new Window
            {
                Title = "PixelVault " + AppVersion + " Library",
                Width = PreferredLibraryWindowWidth(),
                Height = PreferredLibraryWindowHeight(),
                MinWidth = 720,
                MinHeight = 520,
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Brush("#0F1519")
            };
            libraryWindow.Title = "PixelVault " + AppVersion + " Library";
            libraryWindow.Width = PreferredLibraryWindowWidth();
            libraryWindow.Height = PreferredLibraryWindowHeight();
            libraryWindow.MinWidth = 720;
            libraryWindow.MinHeight = 520;
            libraryWindow.Background = Brush("#0F1519");
            return libraryWindow;
        }

        LibraryBrowserNavChrome BuildLibraryBrowserNavChrome()
        {
            var chrome = new LibraryBrowserNavChrome();
            chrome.NavBar = new Border
            {
                Background = Brush("#161E24"),
                BorderBrush = Brush("#27313A"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(18, 14, 18, 14)
            };
            var navGrid = new Grid();
            navGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            navGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 0 });

            chrome.ImportButton = Btn("Import", null, "#2B7A52", Brushes.White);
            chrome.ImportButton.Width = 142;
            chrome.ImportButton.Height = 42;
            chrome.ImportButton.FontSize = 13.5;
            chrome.ImportButton.Margin = new Thickness(0, 0, 10, 0);
            chrome.ImportButton.ToolTip = "Move queued uploads into the library (quick path)";
            ApplyLibraryToolbarChrome(chrome.ImportButton, "#275742", "#2F6B53", "#2E654D", "#214D39");
            chrome.ImportCommentsButton = Btn("Import and Edit", null, "#355F93", Brushes.White);
            chrome.ImportCommentsButton.Width = 156;
            chrome.ImportCommentsButton.Height = 42;
            chrome.ImportCommentsButton.FontSize = 13.5;
            chrome.ImportCommentsButton.Margin = new Thickness(0, 0, 10, 0);
            chrome.ImportCommentsButton.ToolTip = "Review captures with comments, then import";
            ApplyLibraryToolbarChrome(chrome.ImportCommentsButton, "#274B68", "#315D80", "#31597A", "#203E57");
            chrome.ManualImportButton = Btn("Manual Import", null, "#7C5A34", Brushes.White);
            chrome.ManualImportButton.Width = 150;
            chrome.ManualImportButton.Height = 42;
            chrome.ManualImportButton.FontSize = 13.5;
            chrome.ManualImportButton.Margin = new Thickness(0, 0, 0, 0);
            chrome.ManualImportButton.ToolTip = "Open unmatched intake files for manual metadata";
            ApplyLibraryToolbarChrome(chrome.ManualImportButton, "#5F4528", "#7A5A35", "#735431", "#4E381F");
            var importActions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            importActions.Children.Add(chrome.ImportButton);
            importActions.Children.Add(chrome.ImportCommentsButton);
            importActions.Children.Add(chrome.ManualImportButton);
            Grid.SetColumn(importActions, 0);
            navGrid.Children.Add(importActions);
            chrome.SettingsButton = Btn("Settings", null, "#20343A", Brushes.White);
            chrome.SettingsButton.Width = 122;
            chrome.SettingsButton.Height = 42;
            chrome.SettingsButton.FontSize = 13;
            chrome.SettingsButton.Margin = new Thickness(0, 0, 12, 0);
            ApplyLibraryToolbarChrome(chrome.SettingsButton, "#18242B", "#24353F", "#22323C", "#131D23");
            chrome.SettingsButton.Content = BuildToolbarButtonContent("\uE713", "Settings");
            chrome.SettingsButton.ToolTip = "Paths, tools, library layout, and behavior";
            chrome.GameIndexButton = Btn("Game Index", null, "#20343A", Brushes.White);
            chrome.GameIndexButton.Width = 122;
            chrome.GameIndexButton.Height = 42;
            chrome.GameIndexButton.FontSize = 13;
            chrome.GameIndexButton.Margin = new Thickness(0, 0, 12, 0);
            ApplyLibraryToolbarChrome(chrome.GameIndexButton, "#18242B", "#24353F", "#22323C", "#131D23");
            chrome.GameIndexButton.ToolTip = "Edit master game rows (Steam App ID, Grid ID, etc.)";
            chrome.PhotoIndexButton = Btn("Photo Index", null, "#20343A", Brushes.White);
            chrome.PhotoIndexButton.Width = 122;
            chrome.PhotoIndexButton.Height = 42;
            chrome.PhotoIndexButton.FontSize = 13;
            chrome.PhotoIndexButton.Margin = new Thickness(0, 0, 12, 0);
            ApplyLibraryToolbarChrome(chrome.PhotoIndexButton, "#18242B", "#24353F", "#22323C", "#131D23");
            chrome.PhotoIndexButton.ToolTip = "Per-file SQLite index (stars, tags, capture time)";
            chrome.PhotographyGalleryButton = Btn("Photography", null, "#20343A", Brushes.White);
            chrome.PhotographyGalleryButton.Width = 122;
            chrome.PhotographyGalleryButton.Height = 42;
            chrome.PhotographyGalleryButton.FontSize = 13;
            chrome.PhotographyGalleryButton.Margin = new Thickness(0, 0, 12, 0);
            chrome.PhotographyGalleryButton.ToolTip = "Browse captures tagged for game photography";
            ApplyLibraryToolbarChrome(chrome.PhotographyGalleryButton, "#18242B", "#24353F", "#22323C", "#131D23");
            chrome.PhotographyGalleryButton.Content = BuildToolbarButtonContent("\uE722", "Photography");
            chrome.MyCoversButton = Btn("My Covers", null, "#20343A", Brushes.White);
            chrome.MyCoversButton.Width = 122;
            chrome.MyCoversButton.Height = 42;
            chrome.MyCoversButton.FontSize = 13;
            chrome.MyCoversButton.Margin = new Thickness(0, 0, 12, 0);
            ApplyLibraryToolbarChrome(chrome.MyCoversButton, "#18242B", "#24353F", "#22323C", "#131D23");
            chrome.MyCoversButton.Content = BuildToolbarButtonContent("\uEB9F", "My Covers");
            chrome.MyCoversButton.ToolTip = "Open the saved custom covers folder on disk";
            chrome.ExportStarredButton = Btn("Export Starred", null, "#20343A", Brushes.White);
            chrome.ExportStarredButton.Width = 150;
            chrome.ExportStarredButton.Height = 42;
            chrome.ExportStarredButton.FontSize = 13;
            chrome.ExportStarredButton.Margin = new Thickness(0, 0, 12, 0);
            chrome.ExportStarredButton.ToolTip = "Copy starred captures to the folder set in Path Settings, mirroring subfolders under the library root. Only new files or those with changed metadata are copied again; state is tracked per library in the index database. Existing files are replaced; read-only targets are cleared when possible.";
            ApplyLibraryToolbarChrome(chrome.ExportStarredButton, "#18242B", "#24353F", "#22323C", "#131D23");
            chrome.ExportStarredButton.Content = BuildToolbarButtonContent("\uE81E", "Export Starred");
            chrome.RefreshButton = Btn("Refresh", null, "#20343A", Brushes.White);
            chrome.RefreshButton.Width = 122;
            chrome.RefreshButton.Margin = new Thickness(8, 0, 0, 0);
            ApplyLibraryToolbarChrome(chrome.RefreshButton, "#18242B", "#24353F", "#22323C", "#131D23");
            chrome.RefreshButton.Content = BuildToolbarButtonContent("\uE72C", "Refresh");
            chrome.RefreshButton.ToolTip = "Rescan library folders from disk";
            chrome.IntakeReviewButton = Btn(string.Empty, null, "#152028", Brushes.White);
            chrome.IntakeReviewButton.Width = 76;
            chrome.IntakeReviewButton.Height = 56;
            chrome.IntakeReviewButton.Padding = new Thickness(0);
            chrome.IntakeReviewButton.Margin = new Thickness(8, 0, 0, 0);
            chrome.IntakeReviewButton.ToolTip = "Preview upload queue";
            ApplyLibraryToolbarChrome(chrome.IntakeReviewButton, "#152028", "#253745", "#1E2D37", "#121C23");
            // Inner size = button minus 1px toolbar border on each side so the glyph can use the full hit area (template centers content).
            var intakeInnerW = chrome.IntakeReviewButton.Width - 2;
            var intakeInnerH = chrome.IntakeReviewButton.Height - 2;
            var intakeReviewContent = new Grid { Width = intakeInnerW, Height = intakeInnerH };
            var inset = new Thickness(2);
            var intakeQueueIcon = LoadIntakeReviewQueueBitmap();
            if (intakeQueueIcon != null)
            {
                var intakeImage = new Image
                {
                    Margin = inset,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Source = intakeQueueIcon
                };
                RenderOptions.SetBitmapScalingMode(intakeImage, BitmapScalingMode.HighQuality);
                intakeReviewContent.Children.Add(intakeImage);
            }
            else
            {
                intakeReviewContent.Children.Add(new Viewbox
                {
                    Margin = inset,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Child = BuildGamepadGlyphCanvas(Brush("#F5F7FA"), 2.15)
                });
            }
            chrome.IntakeReviewBadgeText = new TextBlock
            {
                Foreground = Brushes.White,
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };
            chrome.IntakeReviewBadge = new Border
            {
                MinWidth = 22,
                Height = 22,
                Background = Brush("#FF453A"),
                BorderBrush = Brush("#FFD6D2"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(11),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -4, -4, 0),
                Padding = new Thickness(6, 0, 6, 0),
                Visibility = Visibility.Collapsed,
                Child = chrome.IntakeReviewBadgeText
            };
            intakeReviewContent.Children.Add(chrome.IntakeReviewBadge);
            chrome.IntakeReviewButton.Content = intakeReviewContent;
            var headerActions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            headerActions.Children.Add(chrome.SettingsButton);
            headerActions.Children.Add(chrome.GameIndexButton);
            headerActions.Children.Add(chrome.PhotoIndexButton);
            headerActions.Children.Add(chrome.PhotographyGalleryButton);
            headerActions.Children.Add(chrome.MyCoversButton);
            headerActions.Children.Add(chrome.ExportStarredButton);
            headerActions.Children.Add(chrome.RefreshButton);
            headerActions.Children.Add(chrome.IntakeReviewButton);
            var headerScroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalContentAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Right,
                MinWidth = 0,
                Content = headerActions
            };
            chrome.HeaderActionsScrollViewer = headerScroll;

            chrome.HeaderScrollLeftNudge = BuildLibraryNavHeaderScrollNudge(true);
            chrome.HeaderScrollRightNudge = BuildLibraryNavHeaderScrollNudge(false);
            chrome.HeaderScrollHoverTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(18) };
            chrome.HeaderScrollHoverTimer.Tick += delegate
            {
                if (chrome.HeaderScrollHoverDirection == 0 || chrome.HeaderActionsScrollViewer == null) return;
                var sv = chrome.HeaderActionsScrollViewer;
                var step = chrome.HeaderScrollHoverDirection * 8.0;
                var target = sv.HorizontalOffset + step;
                sv.ScrollToHorizontalOffset(Math.Max(0d, Math.Min(sv.ScrollableWidth, target)));
                UpdateLibraryNavChromeHeaderScrollNudges(chrome);
            };
            chrome.HeaderScrollLeftNudge.MouseEnter += delegate
            {
                chrome.HeaderScrollHoverDirection = -1;
                chrome.HeaderScrollHoverTimer?.Start();
            };
            chrome.HeaderScrollLeftNudge.MouseLeave += delegate
            {
                chrome.HeaderScrollHoverDirection = 0;
                chrome.HeaderScrollHoverTimer?.Stop();
            };
            chrome.HeaderScrollRightNudge.MouseEnter += delegate
            {
                chrome.HeaderScrollHoverDirection = 1;
                chrome.HeaderScrollHoverTimer?.Start();
            };
            chrome.HeaderScrollRightNudge.MouseLeave += delegate
            {
                chrome.HeaderScrollHoverDirection = 0;
                chrome.HeaderScrollHoverTimer?.Stop();
            };

            var middleHost = new Grid { Margin = new Thickness(12, 0, 0, 0), MinWidth = 0 };
            middleHost.Children.Add(headerScroll);
            middleHost.Children.Add(chrome.HeaderScrollLeftNudge);
            middleHost.Children.Add(chrome.HeaderScrollRightNudge);
            Grid.SetColumn(middleHost, 1);
            navGrid.Children.Add(middleHost);

            void pinTrailingAndRefreshNudges()
            {
                PinLibraryNavHeaderScrollToTrailingEdgeIfOverflow(chrome);
                UpdateLibraryNavChromeHeaderScrollNudges(chrome);
            }

            headerScroll.ScrollChanged += delegate { UpdateLibraryNavChromeHeaderScrollNudges(chrome); };
            headerScroll.SizeChanged += delegate { pinTrailingAndRefreshNudges(); };
            headerActions.SizeChanged += delegate { pinTrailingAndRefreshNudges(); };
            navGrid.SizeChanged += delegate { pinTrailingAndRefreshNudges(); };
            chrome.NavBar.Loaded += delegate
            {
                chrome.NavBar.Dispatcher.BeginInvoke(new Action(pinTrailingAndRefreshNudges), DispatcherPriority.Loaded);
            };

            chrome.NavBar.Child = navGrid;
            return chrome;
        }

        static LinearGradientBrush BuildLibraryNavNudgeGradientFadeFromLeft()
        {
            var br = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            br.GradientStops.Add(new GradientStop(Color.FromArgb(200, 14, 20, 26), 0));
            br.GradientStops.Add(new GradientStop(Color.FromArgb(0, 14, 20, 26), 1));
            if (br.CanFreeze) br.Freeze();
            return br;
        }

        static LinearGradientBrush BuildLibraryNavNudgeGradientFadeFromRight()
        {
            var br = new LinearGradientBrush { StartPoint = new Point(0, 0), EndPoint = new Point(1, 0) };
            br.GradientStops.Add(new GradientStop(Color.FromArgb(0, 14, 20, 26), 0));
            br.GradientStops.Add(new GradientStop(Color.FromArgb(200, 14, 20, 26), 1));
            if (br.CanFreeze) br.Freeze();
            return br;
        }

        static Border BuildLibraryNavHeaderScrollNudge(bool leftEdge)
        {
            var b = new Border
            {
                Width = 40,
                HorizontalAlignment = leftEdge ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = leftEdge ? BuildLibraryNavNudgeGradientFadeFromLeft() : BuildLibraryNavNudgeGradientFadeFromRight(),
                Cursor = System.Windows.Input.Cursors.Hand,
                Visibility = Visibility.Collapsed,
                Padding = new Thickness(leftEdge ? 6 : 0, 0, leftEdge ? 0 : 6, 0)
            };
            Panel.SetZIndex(b, 3);
            AutomationProperties.SetName(b, leftEdge ? "Scroll library toolbar left" : "Scroll library toolbar right");
            b.Child = new TextBlock
            {
                Text = leftEdge ? "\uE76B" : "\uE76C",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 13,
                Foreground = UiBrushHelper.FromHex("#DCE8EF"),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Opacity = 0.92
            };
            return b;
        }

        static void PinLibraryNavHeaderScrollToTrailingEdgeIfOverflow(LibraryBrowserNavChrome chrome)
        {
            if (chrome?.HeaderActionsScrollViewer == null) return;
            var sv = chrome.HeaderActionsScrollViewer;
            var viewport = sv.ViewportWidth;
            var extent = sv.ExtentWidth;
            if (viewport <= 0 || extent <= viewport + 0.5) return;
            sv.ScrollToHorizontalOffset(sv.ScrollableWidth);
        }

        static void UpdateLibraryNavChromeHeaderScrollNudges(LibraryBrowserNavChrome chrome)
        {
            if (chrome?.HeaderActionsScrollViewer == null || chrome.HeaderScrollLeftNudge == null || chrome.HeaderScrollRightNudge == null) return;
            var sv = chrome.HeaderActionsScrollViewer;
            var viewport = sv.ViewportWidth;
            var extent = sv.ExtentWidth;
            if (viewport <= 0 || extent <= viewport + 0.5)
            {
                chrome.HeaderScrollLeftNudge.Visibility = Visibility.Collapsed;
                chrome.HeaderScrollRightNudge.Visibility = Visibility.Collapsed;
                if (Math.Abs(sv.HorizontalOffset) > 0.01) sv.ScrollToHorizontalOffset(0);
                return;
            }
            var maxScroll = Math.Max(0d, sv.ScrollableWidth);
            chrome.HeaderScrollLeftNudge.Visibility = sv.HorizontalOffset > 0.75 ? Visibility.Visible : Visibility.Collapsed;
            chrome.HeaderScrollRightNudge.Visibility = sv.HorizontalOffset < maxScroll - 0.75 ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
