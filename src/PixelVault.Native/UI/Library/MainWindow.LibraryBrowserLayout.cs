using System;
using System.Windows;
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
            internal TextBox SearchBox;
            internal DispatcherTimer SearchDebounceTimer;
            internal DispatcherTimer DetailResizeDebounceTimer;
            internal DispatcherTimer FolderResizeDebounceTimer;
            internal DispatcherTimer ScrollPersistDebounceTimer;
            internal Button GroupAllButton;
            internal Button GroupConsoleButton;
            internal Button SortPlatformButton;
            internal Button SortRecentButton;
            internal Button SortPhotosButton;
            internal VirtualizedRowHost TileRows;
            internal ScrollViewer TileScroll;
            internal TextBlock ThumbLabel;
            internal Image PreviewImage;
            internal TextBlock DetailTitle;
            internal WrapPanel DetailTitleBadgePanel;
            internal TextBlock DetailMeta;
            internal Button OpenFolderButton;
            internal Button EditMetadataButton;
            internal Button RefreshThisFolderButton;
            internal Button DeleteSelectedButton;
            internal Button FolderTileSmallerButton;
            internal Button FolderTileLargerButton;
            internal Button ShortcutsHelpButton;
            internal VirtualizedRowHost DetailRows;
            internal ScrollViewer ThumbScroll;
            internal Grid LibrarySplitContentGrid;
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
            browserToolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            browserToolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            browserToolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            browserToolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            panes.SortPlatformButton = Btn("Sort + Filter", null, "#20343A", Brushes.White);
            panes.SortPlatformButton.Width = 112;
            panes.SortPlatformButton.Height = 34;
            panes.SortPlatformButton.FontSize = 11.5;
            panes.SortPlatformButton.Margin = new Thickness(0, 0, 10, 0);
            ApplyLibraryPillChrome(panes.SortPlatformButton, "#232B35", "#33424D", "#2A3440", "#182028", "#D7E2EA");
            panes.SortRecentButton = Btn("Recently Added", null, "#20343A", Brushes.White);
            panes.SortRecentButton.Width = 122;
            panes.SortRecentButton.Height = 34;
            panes.SortRecentButton.FontSize = 11.5;
            panes.SortRecentButton.Margin = new Thickness(0, 0, 10, 0);
            ApplyLibraryPillChrome(panes.SortRecentButton, "#232B35", "#33424D", "#2A3440", "#182028", "#D7E2EA");
            panes.SortPhotosButton = Btn("Most Photos", null, "#20343A", Brushes.White);
            panes.SortPhotosButton.Width = 108;
            panes.SortPhotosButton.Height = 34;
            panes.SortPhotosButton.FontSize = 11.5;
            panes.SortPhotosButton.Margin = new Thickness(0, 0, 0, 0);
            ApplyLibraryPillChrome(panes.SortPhotosButton, "#232B35", "#33424D", "#2A3440", "#182028", "#D7E2EA");
            browserToolbar.Children.Add(panes.SortPlatformButton);
            Grid.SetColumn(panes.SortRecentButton, 1);
            browserToolbar.Children.Add(panes.SortRecentButton);
            Grid.SetColumn(panes.SortPhotosButton, 2);
            browserToolbar.Children.Add(panes.SortPhotosButton);
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
            panes.GroupConsoleButton.Margin = new Thickness(0);
            ApplyLibraryPillChrome(panes.GroupConsoleButton, "#232B35", "#33424D", "#2A3440", "#182028", "#D7E2EA");
            Grid.SetColumn(panes.GroupAllButton, 4);
            browserToolbar.Children.Add(panes.GroupAllButton);
            Grid.SetColumn(panes.GroupConsoleButton, 5);
            browserToolbar.Children.Add(panes.GroupConsoleButton);
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
            var footerButtons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Top };
            panes.ShortcutsHelpButton = Btn("?", null, "#20343A", Brushes.White);
            panes.ShortcutsHelpButton.Width = 32;
            panes.ShortcutsHelpButton.Height = 30;
            panes.ShortcutsHelpButton.FontSize = 14;
            panes.ShortcutsHelpButton.FontWeight = FontWeights.Bold;
            panes.ShortcutsHelpButton.Margin = new Thickness(0, 0, 6, 0);
            panes.ShortcutsHelpButton.ToolTip = "Keyboard shortcuts (F1)";
            ApplyLibraryPillChrome(panes.ShortcutsHelpButton, "#232B35", "#33424D", "#2A3440", "#182028", "#D7E2EA");
            panes.FolderTileSmallerButton = Btn("Tiles −", null, "#20343A", Brushes.White);
            panes.FolderTileSmallerButton.Height = 30;
            panes.FolderTileSmallerButton.FontSize = 11;
            panes.FolderTileSmallerButton.Padding = new Thickness(10, 0, 10, 0);
            panes.FolderTileSmallerButton.Margin = new Thickness(0, 0, 6, 0);
            panes.FolderTileSmallerButton.ToolTip = "Smaller folder tiles";
            ApplyLibraryPillChrome(panes.FolderTileSmallerButton, "#232B35", "#33424D", "#2A3440", "#182028", "#D7E2EA");
            panes.FolderTileLargerButton = Btn("Tiles +", null, "#20343A", Brushes.White);
            panes.FolderTileLargerButton.Height = 30;
            panes.FolderTileLargerButton.FontSize = 11;
            panes.FolderTileLargerButton.Padding = new Thickness(10, 0, 10, 0);
            panes.FolderTileLargerButton.ToolTip = "Larger folder tiles";
            ApplyLibraryPillChrome(panes.FolderTileLargerButton, "#232B35", "#33424D", "#2A3440", "#182028", "#D7E2EA");
            footerButtons.Children.Add(panes.ShortcutsHelpButton);
            footerButtons.Children.Add(panes.FolderTileSmallerButton);
            footerButtons.Children.Add(panes.FolderTileLargerButton);
            footerGrid.Children.Add(footerButtons);
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
                ShowsPreview = false
            };
            Grid.SetColumn(splitter, 1);
            contentGrid.Children.Add(splitter);
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
            panes.OpenFolderButton.Content = BuildToolbarButtonContent("\uE8B7", "Open Folder");
            panes.RefreshThisFolderButton.Content = BuildToolbarButtonContent("\uE72C", "Refresh folder");
            panes.RefreshThisFolderButton.ToolTip = "Refresh IDs and cover art for this folder only";
            ApplyLibraryPillChrome(panes.OpenFolderButton, "#1F3340", "#314754", "#29424F", "#172630");
            ApplyLibraryPillChrome(panes.EditMetadataButton, "#1C2A32", "#2A3C46", "#22323C", "#141E24");
            ApplyLibraryPillChrome(panes.RefreshThisFolderButton, "#1C2A32", "#2A3C46", "#22323C", "#141E24");
            panes.OpenFolderButton.Height = 38;
            panes.EditMetadataButton.Height = 38;
            panes.RefreshThisFolderButton.Height = 38;
            panes.OpenFolderButton.Margin = new Thickness(0, 0, 12, 0);
            panes.EditMetadataButton.Margin = new Thickness(0, 0, 12, 0);
            panes.RefreshThisFolderButton.Margin = new Thickness(0, 0, 12, 0);
            var bannerButtonRow = new Grid { Margin = new Thickness(0, 8, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
            bannerButtonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bannerButtonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bannerButtonRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bannerButtonRow.Children.Add(panes.OpenFolderButton);
            Grid.SetColumn(panes.EditMetadataButton, 1);
            bannerButtonRow.Children.Add(panes.EditMetadataButton);
            Grid.SetColumn(panes.RefreshThisFolderButton, 2);
            bannerButtonRow.Children.Add(panes.RefreshThisFolderButton);
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
            var sliderPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            panes.DeleteSelectedButton = new Button
            {
                Width = 28,
                Height = 28,
                Margin = new Thickness(12, 0, 0, 0),
                Padding = new Thickness(0),
                Background = Brush("#A3473E"),
                BorderBrush = Brush("#C46A5D"),
                BorderThickness = new Thickness(1),
                Foreground = Brushes.White,
                Cursor = Cursors.Hand,
                ToolTip = "Delete selected capture(s)",
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
            panes.DeleteSelectedButton.IsEnabled = false;
            sliderPanel.Children.Add(panes.DeleteSelectedButton);
            DockPanel.SetDock(sliderPanel, Dock.Right);
            controls.Children.Add(sliderPanel);
            Grid.SetRow(controls, 1);
            rightGrid.Children.Add(controls);

            panes.DetailRows = CreateVirtualizedRowHost(new Thickness(0), Brush("#0F151A"));
            panes.ThumbScroll = panes.DetailRows.ScrollViewer;
            panes.ThumbScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
            Grid.SetRow(panes.ThumbScroll, 2);
            rightGrid.Children.Add(panes.ThumbScroll);
            right.Child = rightGrid;
            contentGrid.Children.Add(right);

            panes.LibrarySplitContentGrid = contentGrid;
            ApplyLibraryBrowserFolderPaneSplit(contentGrid);

            return panes;
        }
    }
}
