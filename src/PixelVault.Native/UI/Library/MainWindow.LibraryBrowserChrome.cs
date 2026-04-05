using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

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
            internal Button FilenameRulesButton;
            internal Button MyCoversButton;
            internal Button ExportStarredButton;
            internal Button RefreshButton;
            internal Button FetchButton;
            internal Button IntakeReviewButton;
            internal Border IntakeReviewBadge;
            internal TextBlock IntakeReviewBadgeText;
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
            navGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            navGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            chrome.ImportButton = Btn("Import", null, "#2B7A52", Brushes.White);
            chrome.ImportButton.Width = 142;
            chrome.ImportButton.Height = 42;
            chrome.ImportButton.FontSize = 13.5;
            chrome.ImportButton.Margin = new Thickness(0, 0, 10, 0);
            ApplyLibraryToolbarChrome(chrome.ImportButton, "#275742", "#2F6B53", "#2E654D", "#214D39");
            chrome.ImportCommentsButton = Btn("Import and Edit", null, "#355F93", Brushes.White);
            chrome.ImportCommentsButton.Width = 156;
            chrome.ImportCommentsButton.Height = 42;
            chrome.ImportCommentsButton.FontSize = 13.5;
            chrome.ImportCommentsButton.Margin = new Thickness(0, 0, 10, 0);
            ApplyLibraryToolbarChrome(chrome.ImportCommentsButton, "#274B68", "#315D80", "#31597A", "#203E57");
            chrome.ManualImportButton = Btn("Manual Import", null, "#7C5A34", Brushes.White);
            chrome.ManualImportButton.Width = 150;
            chrome.ManualImportButton.Height = 42;
            chrome.ManualImportButton.FontSize = 13.5;
            chrome.ManualImportButton.Margin = new Thickness(0, 0, 0, 0);
            ApplyLibraryToolbarChrome(chrome.ManualImportButton, "#5F4528", "#7A5A35", "#735431", "#4E381F");
            var importActions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            importActions.Children.Add(chrome.ImportButton);
            importActions.Children.Add(chrome.ImportCommentsButton);
            importActions.Children.Add(chrome.ManualImportButton);
            var importScroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalContentAlignment = VerticalAlignment.Center,
                Content = importActions
            };
            Grid.SetColumn(importScroll, 0);
            navGrid.Children.Add(importScroll);
            chrome.SettingsButton = Btn("Settings", null, "#20343A", Brushes.White);
            chrome.SettingsButton.Width = 122;
            chrome.SettingsButton.Height = 42;
            chrome.SettingsButton.FontSize = 13;
            chrome.SettingsButton.Margin = new Thickness(0, 0, 12, 0);
            ApplyLibraryToolbarChrome(chrome.SettingsButton, "#18242B", "#24353F", "#22323C", "#131D23");
            chrome.SettingsButton.Content = BuildToolbarButtonContent("\uE713", "Settings");
            chrome.GameIndexButton = Btn("Game Index", null, "#20343A", Brushes.White);
            chrome.GameIndexButton.Width = 122;
            chrome.GameIndexButton.Height = 42;
            chrome.GameIndexButton.FontSize = 13;
            chrome.GameIndexButton.Margin = new Thickness(0, 0, 12, 0);
            ApplyLibraryToolbarChrome(chrome.GameIndexButton, "#18242B", "#24353F", "#22323C", "#131D23");
            chrome.PhotoIndexButton = Btn("Photo Index", null, "#20343A", Brushes.White);
            chrome.PhotoIndexButton.Width = 122;
            chrome.PhotoIndexButton.Height = 42;
            chrome.PhotoIndexButton.FontSize = 13;
            chrome.PhotoIndexButton.Margin = new Thickness(0, 0, 12, 0);
            ApplyLibraryToolbarChrome(chrome.PhotoIndexButton, "#18242B", "#24353F", "#22323C", "#131D23");
            chrome.PhotographyGalleryButton = Btn("Photography", null, "#20343A", Brushes.White);
            chrome.PhotographyGalleryButton.Width = 122;
            chrome.PhotographyGalleryButton.Height = 42;
            chrome.PhotographyGalleryButton.FontSize = 13;
            chrome.PhotographyGalleryButton.Margin = new Thickness(0, 0, 12, 0);
            chrome.PhotographyGalleryButton.ToolTip = "Browse captures tagged for game photography";
            ApplyLibraryToolbarChrome(chrome.PhotographyGalleryButton, "#18242B", "#24353F", "#22323C", "#131D23");
            chrome.PhotographyGalleryButton.Content = BuildToolbarButtonContent("\uE722", "Photography");
            chrome.FilenameRulesButton = Btn("Filename Rules", null, "#20343A", Brushes.White);
            chrome.FilenameRulesButton.Width = 122;
            chrome.FilenameRulesButton.Height = 42;
            chrome.FilenameRulesButton.FontSize = 13;
            chrome.FilenameRulesButton.Margin = new Thickness(0, 0, 12, 0);
            ApplyLibraryToolbarChrome(chrome.FilenameRulesButton, "#18242B", "#24353F", "#22323C", "#131D23");
            chrome.MyCoversButton = Btn("My Covers", null, "#20343A", Brushes.White);
            chrome.MyCoversButton.Width = 122;
            chrome.MyCoversButton.Height = 42;
            chrome.MyCoversButton.FontSize = 13;
            chrome.MyCoversButton.Margin = new Thickness(0, 0, 12, 0);
            ApplyLibraryToolbarChrome(chrome.MyCoversButton, "#18242B", "#24353F", "#22323C", "#131D23");
            chrome.MyCoversButton.Content = BuildToolbarButtonContent("\uEB9F", "My Covers");
            chrome.ExportStarredButton = Btn("Export Starred", null, "#20343A", Brushes.White);
            chrome.ExportStarredButton.Width = 150;
            chrome.ExportStarredButton.Height = 42;
            chrome.ExportStarredButton.FontSize = 13;
            chrome.ExportStarredButton.Margin = new Thickness(0, 0, 12, 0);
            chrome.ExportStarredButton.ToolTip = "Copy starred captures to the folder set in Path Settings (overwrites same file names).";
            ApplyLibraryToolbarChrome(chrome.ExportStarredButton, "#18242B", "#24353F", "#22323C", "#131D23");
            chrome.ExportStarredButton.Content = BuildToolbarButtonContent("\uE81E", "Export Starred");
            chrome.RefreshButton = Btn("Refresh", null, "#20343A", Brushes.White);
            chrome.FetchButton = Btn("Fetch Covers", null, "#275D47", Brushes.White);
            chrome.RefreshButton.Width = 122;
            chrome.FetchButton.Width = 136;
            chrome.RefreshButton.Margin = new Thickness(8, 0, 0, 0);
            chrome.FetchButton.Margin = new Thickness(8, 0, 0, 0);
            ApplyLibraryToolbarChrome(chrome.RefreshButton, "#18242B", "#24353F", "#22323C", "#131D23");
            ApplyLibraryToolbarChrome(chrome.FetchButton, "#234E3B", "#2F6950", "#2C604A", "#1B3F31");
            chrome.RefreshButton.Content = BuildToolbarButtonContent("\uE72C", "Refresh");
            chrome.IntakeReviewButton = Btn(string.Empty, null, "#152028", Brushes.White);
            chrome.IntakeReviewButton.Width = 76;
            chrome.IntakeReviewButton.Height = 56;
            chrome.IntakeReviewButton.Padding = new Thickness(0);
            chrome.IntakeReviewButton.Margin = new Thickness(8, 0, 0, 0);
            chrome.IntakeReviewButton.ToolTip = "Preview upload queue";
            ApplyLibraryToolbarChrome(chrome.IntakeReviewButton, "#152028", "#253745", "#1E2D37", "#121C23");
            var intakeReviewContent = new Grid();
            intakeReviewContent.Children.Add(new Viewbox
            {
                Width = 42,
                Height = 28,
                Stretch = Stretch.Uniform,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Child = BuildGamepadGlyph(Brush("#F5F7FA"), 2.15, 42, 28)
            });
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
            var headerActions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
            headerActions.Children.Add(chrome.SettingsButton);
            headerActions.Children.Add(chrome.GameIndexButton);
            headerActions.Children.Add(chrome.PhotoIndexButton);
            headerActions.Children.Add(chrome.PhotographyGalleryButton);
            headerActions.Children.Add(chrome.FilenameRulesButton);
            headerActions.Children.Add(chrome.MyCoversButton);
            headerActions.Children.Add(chrome.ExportStarredButton);
            headerActions.Children.Add(chrome.RefreshButton);
            headerActions.Children.Add(chrome.FetchButton);
            headerActions.Children.Add(chrome.IntakeReviewButton);
            var headerScroll = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(12, 0, 0, 0),
                Content = headerActions
            };
            Grid.SetColumn(headerScroll, 2);
            navGrid.Children.Add(headerScroll);
            chrome.NavBar.Child = navGrid;
            return chrome;
        }
    }
}
