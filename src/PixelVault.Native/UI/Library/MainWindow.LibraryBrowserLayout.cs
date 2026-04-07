using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        internal sealed class LibraryBrowserPaneRefs
        {
            internal Border LeftPane;
            internal GridSplitter Splitter;
            internal Button PhotoWorkspaceDividerToggleButton;
            internal TextBox SearchBox;
            internal DispatcherTimer SearchDebounceTimer;
            internal DispatcherTimer DetailResizeDebounceTimer;
            internal DispatcherTimer FolderResizeDebounceTimer;
            internal DispatcherTimer ScrollPersistDebounceTimer;
            internal Button GroupAllButton;
            internal Button GroupConsoleButton;
            internal Button GroupTimelineButton;
            internal Button OpenCapturesButton;
            internal Button SortMenuButton;
            internal Button FilterMenuButton;
            internal VirtualizedRowHost TileRows;
            internal ScrollViewer TileScroll;
            internal TextBlock ThumbLabel;
            internal Border PreviewFrame;
            internal Image PreviewImage;
            internal TextBlock DetailTitle;
            internal WrapPanel DetailTitleBadgePanel;
            internal TextBlock DetailMeta;
            internal Button OpenFolderButton;
            internal Button EditMetadataButton;
            internal Button RefreshThisFolderButton;
            internal Button ExitTimelineButton;
            internal Button ExitPhotoWorkspaceButton;
            internal WrapPanel TimelineFilterPanel;
            internal Button TimelinePresetTodayButton;
            internal Button TimelinePresetMonthButton;
            internal Button TimelinePresetThirtyDaysButton;
            internal DatePicker TimelineStartDatePicker;
            internal DatePicker TimelineEndDatePicker;
            internal Button DeleteSelectedButton;
            internal Button FolderCoverLayoutButton;
            internal Button PhotoCaptureLayoutButton;
            internal Button CommandPaletteButton;
            internal Button ShortcutsHelpButton;
            internal VirtualizedRowHost DetailRows;
            internal ScrollViewer ThumbScroll;
            internal Grid LibrarySplitContentGrid;
            internal Border RightPane;
            internal DispatcherTimer FolderPaneSplitClampTimer;
        }

        LibraryBrowserPaneRefs BuildLibraryBrowserContentPanes(Grid contentGrid)
        {
            var panes = new LibraryBrowserPaneRefs();

            var left = new Border
            {
                Background = Brush("#11181D"),
                BorderBrush = Brush("#27313A"),
                BorderThickness = new Thickness(0, 0, 1, 0),
                Padding = new Thickness(18, 16, 18, 12),
                MinWidth = 0
            };
            panes.LeftPane = left;
            var leftGrid = new Grid();
            leftGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            leftGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            leftGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            status = new TextBlock { Text = "Ready", Foreground = Brush("#8EA0AA"), FontSize = 11.5, Margin = new Thickness(2, 12, 0, 0), TextWrapping = TextWrapping.Wrap };

            var filterShell = new Border
            {
                Background = Brushes.Transparent,
                BorderBrush = Brush("#27313A"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 2, 0, 14),
                Margin = new Thickness(0, 0, 0, 6)
            };
            var filterGrid = new Grid();
            filterGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            filterGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var searchPanel = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            var searchBoxShell = new Border
            {
                Background = Brush("#182129"),
                BorderBrush = Brush("#2D3A43"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(12, 0, 12, 0),
                MinHeight = 42
            };
            var searchBoxRow = new Grid();
            searchBoxRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            searchBoxRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var searchIcon = BuildSymbolIcon("\uE721", "#8FA4B0", 13);
            searchIcon.Margin = new Thickness(0, 0, 10, 0);
            searchBoxRow.Children.Add(searchIcon);
            panes.SearchBox = new TextBox { Padding = new Thickness(0, 6, 0, 6), Background = Brushes.Transparent, Foreground = Brush("#F1E9DA"), BorderThickness = new Thickness(0), FontSize = 13.5, VerticalContentAlignment = VerticalAlignment.Center };
            Grid.SetColumn(panes.SearchBox, 1);
            searchBoxRow.Children.Add(panes.SearchBox);
            searchBoxShell.Child = searchBoxRow;
            panes.SearchDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
            panes.DetailResizeDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
            panes.FolderResizeDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(140) };
            panes.ScrollPersistDebounceTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
            panes.FolderPaneSplitClampTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            searchPanel.Children.Add(searchBoxShell);
            Grid.SetRow(searchPanel, 0);
            filterGrid.Children.Add(searchPanel);
            var browserToolbar = new Grid();
            browserToolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            browserToolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            browserToolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            browserToolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            browserToolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            browserToolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            panes.SortMenuButton = Btn("Sort", null, "#20343A", Brushes.White);
            panes.SortMenuButton.Width = 82;
            panes.SortMenuButton.Height = 34;
            panes.SortMenuButton.FontSize = 11.5;
            panes.SortMenuButton.Margin = new Thickness(0, 0, 8, 0);
            panes.SortMenuButton.ToolTip = "Choose folder sort order";
            ApplyLibraryPillChrome(panes.SortMenuButton, "#232B35", "#33424D", "#2A3440", "#182028", "#D7E2EA");
            panes.FilterMenuButton = Btn("Filter", null, "#20343A", Brushes.White);
            panes.FilterMenuButton.Width = 86;
            panes.FilterMenuButton.Height = 34;
            panes.FilterMenuButton.FontSize = 11.5;
            panes.FilterMenuButton.Margin = new Thickness(0, 0, 10, 0);
            panes.FilterMenuButton.ToolTip = "Filter visible games";
            ApplyLibraryPillChrome(panes.FilterMenuButton, "#232B35", "#33424D", "#2A3440", "#182028", "#D7E2EA");
            browserToolbar.Children.Add(panes.SortMenuButton);
            Grid.SetColumn(panes.FilterMenuButton, 1);
            browserToolbar.Children.Add(panes.FilterMenuButton);
            panes.GroupAllButton = Btn("All", null, "#20343A", Brushes.White);
            panes.GroupAllButton.Width = 58;
            panes.GroupAllButton.Height = 32;
            panes.GroupAllButton.FontSize = 11.5;
            panes.GroupAllButton.Margin = new Thickness(0, 0, 8, 0);
            ApplyLibraryPillChrome(panes.GroupAllButton, "#232B35", "#33424D", "#2A3440", "#182028", "#D7E2EA");
            panes.GroupConsoleButton = Btn("By Console", null, "#20343A", Brushes.White);
            panes.GroupConsoleButton.Width = 94;
            panes.GroupConsoleButton.Height = 32;
            panes.GroupConsoleButton.FontSize = 11.5;
            panes.GroupConsoleButton.Margin = new Thickness(0, 0, 8, 0);
            ApplyLibraryPillChrome(panes.GroupConsoleButton, "#232B35", "#33424D", "#2A3440", "#182028", "#D7E2EA");
            panes.GroupTimelineButton = Btn("Timeline", null, "#20343A", Brushes.White);
            panes.GroupTimelineButton.Width = 82;
            panes.GroupTimelineButton.Height = 32;
            panes.GroupTimelineButton.FontSize = 11.5;
            panes.GroupTimelineButton.Margin = new Thickness(0);
            ApplyLibraryPillChrome(panes.GroupTimelineButton, "#232B35", "#33424D", "#2A3440", "#182028", "#D7E2EA");
            Grid.SetColumn(panes.GroupAllButton, 3);
            browserToolbar.Children.Add(panes.GroupAllButton);
            Grid.SetColumn(panes.GroupConsoleButton, 4);
            browserToolbar.Children.Add(panes.GroupConsoleButton);
            Grid.SetColumn(panes.GroupTimelineButton, 5);
            browserToolbar.Children.Add(panes.GroupTimelineButton);
            browserToolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            panes.OpenCapturesButton = Btn("Captures", null, "#1F3340", Brushes.White);
            panes.OpenCapturesButton.Height = 32;
            panes.OpenCapturesButton.FontSize = 11.5;
            panes.OpenCapturesButton.Margin = new Thickness(10, 0, 0, 0);
            panes.OpenCapturesButton.ToolTip = "Open captures view (double-click a cover)";
            ApplyLibraryPillChrome(panes.OpenCapturesButton, "#232B35", "#33424D", "#2A3440", "#182028", "#D7E2EA");
            Grid.SetColumn(panes.OpenCapturesButton, 6);
            browserToolbar.Children.Add(panes.OpenCapturesButton);
            var toolbarScroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Margin = new Thickness(0, 4, 0, 0),
                Content = browserToolbar
            };
            Grid.SetRow(toolbarScroll, 1);
            filterGrid.Children.Add(toolbarScroll);
            filterShell.Child = filterGrid;
            Grid.SetRow(filterShell, 0);
            leftGrid.Children.Add(filterShell);

            panes.TileRows = CreateVirtualizedRowHost(new Thickness(0, 12, 0, 0), null);
            panes.TileRows.RecycleVisibleRowElements = true;
            panes.TileScroll = panes.TileRows.ScrollViewer;
            panes.TileScroll.Padding = new Thickness(0, 4, 0, 0);
            panes.TileScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            Grid.SetRow(panes.TileScroll, 1);
            leftGrid.Children.Add(panes.TileScroll);
            var footerGrid = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(10) });
            footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var footerButtons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            panes.CommandPaletteButton = Btn("⋯", null, "#20343A", Brushes.White);
            panes.CommandPaletteButton.Width = 32;
            panes.CommandPaletteButton.Height = 32;
            panes.CommandPaletteButton.FontSize = 18;
            panes.CommandPaletteButton.FontWeight = FontWeights.Bold;
            panes.CommandPaletteButton.Margin = new Thickness(0, 0, 6, 0);
            panes.CommandPaletteButton.ToolTip = "Commands (Ctrl+Shift+P)";
            ApplyLibraryPillChrome(panes.CommandPaletteButton, "#232B35", "#33424D", "#2A3440", "#182028", "#D7E2EA");
            panes.ShortcutsHelpButton = Btn("?", null, "#20343A", Brushes.White);
            panes.ShortcutsHelpButton.Width = 32;
            panes.ShortcutsHelpButton.Height = 32;
            panes.ShortcutsHelpButton.FontSize = 14;
            panes.ShortcutsHelpButton.FontWeight = FontWeights.Bold;
            panes.ShortcutsHelpButton.Margin = new Thickness(0, 0, 6, 0);
            panes.ShortcutsHelpButton.ToolTip = "Keyboard shortcuts (F1) · Command palette (Ctrl+Shift+P)";
            ApplyLibraryPillChrome(panes.ShortcutsHelpButton, "#232B35", "#33424D", "#2A3440", "#182028", "#D7E2EA");
            panes.ShortcutsHelpButton.Height = 32;
            panes.FolderCoverLayoutButton = Btn("Cover size ▾", null, "#20343A", Brushes.White);
            panes.FolderCoverLayoutButton.MinWidth = 108;
            panes.FolderCoverLayoutButton.Height = 32;
            panes.FolderCoverLayoutButton.FontSize = 11;
            panes.FolderCoverLayoutButton.Padding = new Thickness(10, 0, 10, 0);
            panes.FolderCoverLayoutButton.Margin = new Thickness(0, 0, 6, 0);
            panes.FolderCoverLayoutButton.ToolTip = "Folder cover density (saved)";
            ApplyLibraryPillChrome(panes.FolderCoverLayoutButton, "#232B35", "#33424D", "#2A3440", "#182028", "#D7E2EA");
            panes.FolderCoverLayoutButton.VerticalAlignment = VerticalAlignment.Center;
            footerButtons.Children.Add(panes.CommandPaletteButton);
            footerButtons.Children.Add(panes.ShortcutsHelpButton);
            footerButtons.Children.Add(panes.FolderCoverLayoutButton);
            footerGrid.Children.Add(footerButtons);
            status.VerticalAlignment = VerticalAlignment.Center;
            status.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(status, 2);
            footerGrid.Children.Add(status);
            Grid.SetRow(footerGrid, 2);
            leftGrid.Children.Add(footerGrid);
            left.Child = leftGrid;
            contentGrid.Children.Add(left);

            var splitter = new GridSplitter
            {
                Width = LibraryBrowserFolderPaneSplitterWidth,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = Brush("#182028"),
                BorderBrush = Brush("#27313A"),
                BorderThickness = new Thickness(1, 0, 1, 0),
                ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                ResizeDirection = GridResizeDirection.Columns,
                // Live resize remeasures the whole library panes on every mouse move; preview defers real layout until release.
                ShowsPreview = true
            };
            panes.Splitter = splitter;
            var splitterColumnHost = new Grid();
            splitterColumnHost.Children.Add(splitter);
            panes.PhotoWorkspaceDividerToggleButton = new Button
            {
                Width = 22,
                Height = 40,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Padding = new Thickness(0),
                Background = Brush("#E6182028"),
                BorderBrush = Brush("#33424D"),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand,
                ToolTip = "Back to cover-only list",
                FontFamily = new FontFamily("Segoe MDL2 Assets"),
                FontSize = 11,
                Content = "\uE76B",
                Foreground = Brush("#E8F4FC"),
                Visibility = Visibility.Collapsed
            };
            ApplyLibraryPillChrome(panes.PhotoWorkspaceDividerToggleButton, "#232B35", "#33424D", "#2A3440", "#182028", "#D7E2EA");
            AutomationProperties.SetName(panes.PhotoWorkspaceDividerToggleButton, "Back to cover-only list");
            Panel.SetZIndex(panes.PhotoWorkspaceDividerToggleButton, 2);
            splitterColumnHost.Children.Add(panes.PhotoWorkspaceDividerToggleButton);
            Grid.SetColumn(splitterColumnHost, 1);
            contentGrid.Children.Add(splitterColumnHost);
            splitter.DragCompleted += delegate
            {
                PersistLibraryBrowserFolderPaneWidthFromGrid(contentGrid);
            };
            panes.FolderPaneSplitClampTimer.Tick += delegate
            {
                panes.FolderPaneSplitClampTimer.Stop();
                LibraryBrowserFolderSplitClampAfterResize(contentGrid);
            };
            contentGrid.SizeChanged += delegate
            {
                if (_libraryBrowserPersistedFolderPaneWidth <= 0.5) return;
                if (contentGrid.ColumnDefinitions.Count < 1 || !contentGrid.ColumnDefinitions[0].Width.IsAbsolute) return;
                panes.FolderPaneSplitClampTimer.Stop();
                panes.FolderPaneSplitClampTimer.Start();
            };
            contentGrid.Loaded += delegate
            {
                var ws = _libraryBrowserLiveWorkingSet;
                if (ws != null && ReferenceEquals(ws.Panes?.LibrarySplitContentGrid, contentGrid))
                    ApplyLibraryBrowserLayoutMode(ws.Panes, ws.WorkspaceMode);
                else
                    ApplyLibraryBrowserFolderPaneSplit(contentGrid);
            };

            var right = new Border { Background = Brush("#10171C"), Padding = new Thickness(26, 22, 26, 18), MinWidth = 0 };
            Grid.SetColumn(right, 2);
            var rightGrid = new Grid();
            rightGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rightGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            rightGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var banner = new Border { Background = Brushes.Transparent, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 18) };
            var bannerGrid = new Grid();
            bannerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto, MinWidth = 96, MaxWidth = 240 });
            bannerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 140 });
            var previewFrame = new Border
            {
                HorizontalAlignment = HorizontalAlignment.Left,
                VerticalAlignment = VerticalAlignment.Top,
                MinWidth = 96,
                MinHeight = 144,
                MaxWidth = 210,
                MaxHeight = 315,
                CornerRadius = new CornerRadius(14),
                Background = Brush("#0D1216"),
                BorderBrush = Brush("#24323A"),
                BorderThickness = new Thickness(1),
                Margin = new Thickness(0, 0, 18, 0),
                ClipToBounds = true
            };
            panes.PreviewFrame = previewFrame;
            panes.PreviewImage = new Image { Stretch = Stretch.Uniform, MaxWidth = 210, MaxHeight = 315, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            previewFrame.Child = panes.PreviewImage;
            bannerGrid.Children.Add(previewFrame);
            var textStack = new StackPanel { MinWidth = 0 };
            var titleRow = new Grid { Margin = new Thickness(0, 0, 0, 0) };
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            panes.DetailTitle = new TextBlock { Text = "Select a folder", FontSize = 28, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap };
            panes.DetailTitleBadgePanel = new WrapPanel { Margin = new Thickness(12, 4, 0, 0), Visibility = Visibility.Collapsed, VerticalAlignment = VerticalAlignment.Top };
            titleRow.Children.Add(panes.DetailTitle);
            Grid.SetColumn(panes.DetailTitleBadgePanel, 1);
            titleRow.Children.Add(panes.DetailTitleBadgePanel);
            panes.DetailMeta = new TextBlock { Text = "Browse the library you chose in Settings.", Foreground = Brush("#9CB1BC"), Margin = new Thickness(0, 8, 0, 14), TextWrapping = TextWrapping.Wrap, FontSize = 13.5 };
            panes.OpenFolderButton = Btn("Open Folder", null, "#275D47", Brushes.White);
            panes.EditMetadataButton = Btn("Edit Metadata", null, "#20343A", Brushes.White);
            panes.RefreshThisFolderButton = Btn("Refresh folder", null, "#20343A", Brushes.White);
            panes.ExitTimelineButton = Btn("Folder Browser", null, "#20343A", Brushes.White);
            panes.ExitPhotoWorkspaceButton = Btn("Back to folders", null, "#20343A", Brushes.White);
            panes.OpenFolderButton.Content = BuildToolbarButtonContent("\uE8B7", "Open Folder");
            panes.RefreshThisFolderButton.Content = BuildToolbarButtonContent("\uE72C", "Refresh folder");
            panes.ExitTimelineButton.Content = BuildToolbarButtonContent("\uE72B", "Folder Browser");
            panes.ExitPhotoWorkspaceButton.Content = BuildToolbarButtonContent("\uE72B", "Back to folders");
            panes.RefreshThisFolderButton.ToolTip = "Refresh IDs and cover art for this folder only";
            panes.ExitTimelineButton.ToolTip = "Return to the folder browser";
            ApplyLibraryPillChrome(panes.OpenFolderButton, "#1F3340", "#314754", "#29424F", "#172630");
            ApplyLibraryPillChrome(panes.EditMetadataButton, "#1C2A32", "#2A3C46", "#22323C", "#141E24");
            ApplyLibraryPillChrome(panes.RefreshThisFolderButton, "#1C2A32", "#2A3C46", "#22323C", "#141E24");
            ApplyLibraryPillChrome(panes.ExitTimelineButton, "#1C2A32", "#2A3C46", "#22323C", "#141E24");
            ApplyLibraryPillChrome(panes.ExitPhotoWorkspaceButton, "#1C2A32", "#2A3C46", "#22323C", "#141E24");
            panes.OpenFolderButton.Height = 38;
            panes.EditMetadataButton.Height = 38;
            panes.RefreshThisFolderButton.Height = 38;
            panes.ExitTimelineButton.Height = 38;
            panes.ExitPhotoWorkspaceButton.Height = 38;
            panes.OpenFolderButton.Margin = new Thickness(0, 0, 12, 0);
            panes.EditMetadataButton.Margin = new Thickness(0, 0, 12, 0);
            panes.RefreshThisFolderButton.Margin = new Thickness(0, 0, 12, 0);
            panes.ExitTimelineButton.Margin = new Thickness(0);
            panes.ExitTimelineButton.Visibility = Visibility.Collapsed;
            panes.ExitPhotoWorkspaceButton.Margin = new Thickness(12, 0, 0, 0);
            panes.ExitPhotoWorkspaceButton.Visibility = Visibility.Collapsed;
            panes.ExitPhotoWorkspaceButton.ToolTip = "Return to the folder list (Esc)";
            var bannerButtonRow = new Grid { Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            bannerButtonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bannerButtonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bannerButtonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bannerButtonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bannerButtonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bannerButtonRow.Children.Add(panes.OpenFolderButton);
            Grid.SetColumn(panes.EditMetadataButton, 1);
            bannerButtonRow.Children.Add(panes.EditMetadataButton);
            Grid.SetColumn(panes.RefreshThisFolderButton, 2);
            bannerButtonRow.Children.Add(panes.RefreshThisFolderButton);
            Grid.SetColumn(panes.ExitTimelineButton, 3);
            bannerButtonRow.Children.Add(panes.ExitTimelineButton);
            Grid.SetColumn(panes.ExitPhotoWorkspaceButton, 4);
            bannerButtonRow.Children.Add(panes.ExitPhotoWorkspaceButton);
            textStack.Children.Add(titleRow);
            textStack.Children.Add(panes.DetailMeta);
            textStack.Children.Add(bannerButtonRow);
            Grid.SetColumn(textStack, 1);
            bannerGrid.Children.Add(textStack);
            banner.Child = bannerGrid;
            rightGrid.Children.Add(banner);

            var controls = new DockPanel { Margin = new Thickness(0, 4, 0, 14) };
            panes.ThumbLabel = new TextBlock { Text = "Screenshots", FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, VerticalAlignment = VerticalAlignment.Center };
            controls.Children.Add(panes.ThumbLabel);
            var sliderPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            panes.TimelineFilterPanel = new WrapPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 12, 0),
                Visibility = Visibility.Collapsed
            };
            panes.TimelineFilterPanel.Children.Add(new TextBlock
            {
                Text = "Range",
                Foreground = Brush("#8FA4B0"),
                FontSize = 11.5,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            });
            panes.TimelinePresetTodayButton = Btn("Today", null, "#20343A", Brushes.White);
            panes.TimelinePresetTodayButton.Width = 62;
            panes.TimelinePresetTodayButton.Height = 30;
            panes.TimelinePresetTodayButton.FontSize = 11;
            panes.TimelinePresetTodayButton.Margin = new Thickness(0, 0, 8, 0);
            ApplyLibraryPillChrome(panes.TimelinePresetTodayButton, "#232B35", "#33424D", "#2A3440", "#182028", "#D7E2EA");
            panes.TimelineFilterPanel.Children.Add(panes.TimelinePresetTodayButton);
            panes.TimelinePresetMonthButton = Btn("This Month", null, "#20343A", Brushes.White);
            panes.TimelinePresetMonthButton.Width = 92;
            panes.TimelinePresetMonthButton.Height = 30;
            panes.TimelinePresetMonthButton.FontSize = 11;
            panes.TimelinePresetMonthButton.Margin = new Thickness(0, 0, 8, 0);
            ApplyLibraryPillChrome(panes.TimelinePresetMonthButton, "#232B35", "#33424D", "#2A3440", "#182028", "#D7E2EA");
            panes.TimelineFilterPanel.Children.Add(panes.TimelinePresetMonthButton);
            panes.TimelinePresetThirtyDaysButton = Btn("30 Days", null, "#20343A", Brushes.White);
            panes.TimelinePresetThirtyDaysButton.Width = 82;
            panes.TimelinePresetThirtyDaysButton.Height = 30;
            panes.TimelinePresetThirtyDaysButton.FontSize = 11;
            panes.TimelinePresetThirtyDaysButton.Margin = new Thickness(0, 0, 10, 0);
            ApplyLibraryPillChrome(panes.TimelinePresetThirtyDaysButton, "#232B35", "#33424D", "#2A3440", "#182028", "#D7E2EA");
            panes.TimelineFilterPanel.Children.Add(panes.TimelinePresetThirtyDaysButton);
            panes.TimelineStartDatePicker = new DatePicker
            {
                Width = 126,
                Height = 30,
                Margin = new Thickness(0, 0, 6, 0),
                SelectedDateFormat = DatePickerFormat.Short,
                Background = Brush("#182129"),
                Foreground = Brush("#F1E9DA"),
                BorderBrush = Brush("#2D3A43"),
                BorderThickness = new Thickness(1)
            };
            panes.TimelineFilterPanel.Children.Add(panes.TimelineStartDatePicker);
            panes.TimelineFilterPanel.Children.Add(new TextBlock
            {
                Text = "to",
                Foreground = Brush("#8FA4B0"),
                FontSize = 11.5,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 6, 0)
            });
            panes.TimelineEndDatePicker = new DatePicker
            {
                Width = 126,
                Height = 30,
                Margin = new Thickness(0),
                SelectedDateFormat = DatePickerFormat.Short,
                Background = Brush("#182129"),
                Foreground = Brush("#F1E9DA"),
                BorderBrush = Brush("#2D3A43"),
                BorderThickness = new Thickness(1)
            };
            panes.TimelineFilterPanel.Children.Add(panes.TimelineEndDatePicker);
            panes.PhotoCaptureLayoutButton = Btn("Photo size ▾", null, "#20343A", Brushes.White);
            panes.PhotoCaptureLayoutButton.MinWidth = 108;
            panes.PhotoCaptureLayoutButton.Height = 30;
            panes.PhotoCaptureLayoutButton.FontSize = 11;
            panes.PhotoCaptureLayoutButton.Padding = new Thickness(10, 0, 10, 0);
            panes.PhotoCaptureLayoutButton.Margin = new Thickness(0, 0, 12, 0);
            panes.PhotoCaptureLayoutButton.ToolTip = "Capture tile density in this workspace (saved)";
            ApplyLibraryPillChrome(panes.PhotoCaptureLayoutButton, "#232B35", "#33424D", "#2A3440", "#182028", "#D7E2EA");
            panes.PhotoCaptureLayoutButton.Visibility = Visibility.Collapsed;
            panes.PhotoCaptureLayoutButton.VerticalAlignment = VerticalAlignment.Center;
            sliderPanel.Children.Add(panes.PhotoCaptureLayoutButton);
            sliderPanel.Children.Add(panes.TimelineFilterPanel);
            panes.DeleteSelectedButton = new Button
            {
                Width = 28,
                Height = 28,
                Margin = new Thickness(12, 0, 0, 0),
                Padding = new Thickness(0),
                Background = Brush("#8B2F2F"),
                BorderBrush = Brush("#E07A6E"),
                BorderThickness = new Thickness(1.5),
                Foreground = Brushes.White,
                Cursor = Cursors.Hand,
                ToolTip = "Delete selected capture(s) (cannot be undone from PixelVault)",
                Content = new TextBlock
                {
                    Text = "🗑",
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                    TextAlignment = TextAlignment.Center
                }
            };
            AutomationProperties.SetName(panes.DeleteSelectedButton, "Delete selected captures");
            panes.DeleteSelectedButton.IsEnabled = false;
            sliderPanel.Children.Add(panes.DeleteSelectedButton);
            DockPanel.SetDock(sliderPanel, Dock.Right);
            controls.Children.Add(sliderPanel);
            Grid.SetRow(controls, 1);
            rightGrid.Children.Add(controls);

            panes.DetailRows = CreateVirtualizedRowHost(new Thickness(0), Brush("#0F151A"));
            panes.DetailRows.RecycleVisibleRowElements = true;
            panes.ThumbScroll = panes.DetailRows.ScrollViewer;
            panes.ThumbScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            Grid.SetRow(panes.ThumbScroll, 2);
            rightGrid.Children.Add(panes.ThumbScroll);
            right.Child = rightGrid;
            panes.RightPane = right;
            contentGrid.Children.Add(right);

            panes.LibrarySplitContentGrid = contentGrid;
            ApplyLibraryBrowserFolderPaneSplit(contentGrid);

            return panes;
        }
    }
}
