using System;
using System.Windows;
using System.Windows.Automation;
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
            internal Button PhotographyGalleryButton;
            internal Button MyCoversButton;
            internal Button RefreshButton;
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

        static FrameworkElement BuildLibraryToolbarLabel(string glyph, string label)
        {
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(new TextBlock { Text = glyph, FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 14, Margin = new Thickness(0, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center });
            row.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
            return row;
        }

        static Grid BuildLibraryToolbarGroups(FrameworkElement leading, FrameworkElement trailing, double singleRowWidth)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            leading.HorizontalAlignment = HorizontalAlignment.Left;
            trailing.HorizontalAlignment = HorizontalAlignment.Right;
            leading.VerticalAlignment = trailing.VerticalAlignment = VerticalAlignment.Center;
            grid.Children.Add(leading);
            grid.Children.Add(trailing);
            void ArrangeGroups()
            {
                var stacked = grid.ActualWidth < singleRowWidth;
                Grid.SetColumnSpan(leading, stacked ? 2 : 1);
                Grid.SetColumn(trailing, stacked ? 0 : 1);
                Grid.SetColumnSpan(trailing, stacked ? 2 : 1);
                Grid.SetRow(trailing, stacked ? 1 : 0);
                trailing.Margin = stacked ? new Thickness(0, 10, 0, 0) : new Thickness(16, 0, 0, 0);
            }
            grid.SizeChanged += delegate { ArrangeGroups(); };
            ArrangeGroups();
            return grid;
        }

        LibraryBrowserNavChrome BuildLibraryBrowserNavChrome()
        {
            var chrome = new LibraryBrowserNavChrome();
            chrome.NavBar = new Border
            {
                Background = Brush("#161E24"), BorderBrush = Brush("#27313A"),
                BorderThickness = new Thickness(0, 0, 0, 1), Padding = new Thickness(18, 16, 18, 16)
            };
            Button ActionButton(string label, string glyph, double width, string fill, string border, string hover, string pressed, string tip)
            {
                var button = Btn(label, null, fill, Brushes.White);
                button.Width = width; button.Height = 42; button.FontSize = 13;
                button.Margin = new Thickness(0, 0, 8, 0);
                button.ToolTip = tip;
                button.Content = string.IsNullOrEmpty(label) ? BuildToolbarIconButtonContent(glyph) : BuildLibraryToolbarLabel(glyph, label);
                ApplyLibraryToolbarChrome(button, fill, border, hover, pressed);
                AutomationProperties.SetName(button, label);
                AccessibilityUi.TryApplyFocusVisualStyle(button);
                return button;
            }
            Button Utility(string name, string glyph, string tip)
            {
                var button = ActionButton("", glyph, 44, "#1B2931", "#30414B", "#2A3D48", "#142129", tip);
                AutomationProperties.SetName(button, name);
                return button;
            }
            chrome.ImportButton = ActionButton("Import", "\uE896", 114, "#2D6852", "#41876C", "#3B8067", "#234F3F", "Move queued uploads into the library (quick path)");
            chrome.ImportCommentsButton = ActionButton("Import and Edit", "\uE70F", 154, "#294D67", "#3A6079", "#365E78", "#203E53", "Review captures with comments, then import");
            chrome.ManualImportButton = ActionButton("Manual Import", "\uE8B7", 144, "#60492E", "#7B6040", "#735938", "#493722", "Open unmatched intake files for manual metadata");
            chrome.ManualImportButton.Margin = new Thickness(0);
            chrome.SettingsButton = Utility("Settings", "\uE713", "Settings — Game Index and Photo Index are under Library");
            chrome.PhotographyGalleryButton = Utility("Photography", "\uE722", "Browse captures tagged for game photography");
            chrome.MyCoversButton = Utility("My Covers", "\uEB9F", "Open the saved custom covers folder on disk");
            chrome.RefreshButton = Utility("Refresh", "\uE72C", "Reload the folder list from cache (fast). To re-read metadata, use Refresh metadata index in the command palette.");
            chrome.IntakeReviewButton = Utility("Upload queue preview", "\uE896", "Preview upload queue");
            var queueContent = new Grid { Width = 34, Height = 34 };
            queueContent.Children.Add(new Viewbox { Margin = new Thickness(5), Child = BuildIntakeDownloadTrayGlyphCanvas(Brush("#F5F7FA"), 6.0) });
            chrome.IntakeReviewBadgeText = new TextBlock { Foreground = Brushes.White, FontSize = 10, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            chrome.IntakeReviewBadge = new Border
            {
                MinWidth = 18, Height = 18, Background = Brush("#C83C37"), BorderBrush = Brush("#FFD6D2"), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, -5, -5, 0), Padding = new Thickness(4, 0, 4, 0), Visibility = Visibility.Collapsed, Child = chrome.IntakeReviewBadgeText
            };
            queueContent.Children.Add(chrome.IntakeReviewBadge);
            chrome.IntakeReviewButton.Content = queueContent;
            var utilities = new StackPanel { Orientation = Orientation.Horizontal };
            utilities.Children.Add(chrome.PhotographyGalleryButton);
            utilities.Children.Add(chrome.MyCoversButton);
            utilities.Children.Add(new Border { Width = 1, Height = 22, Background = Brush("#34434C"), Margin = new Thickness(4, 0, 12, 0) });
            utilities.Children.Add(chrome.RefreshButton);
            var notifications = CreateAchievementNotificationsButton();
            notifications.Width = 44;
            notifications.Height = 42;
            ApplyLibraryToolbarChrome(notifications, "#1B2931", "#30414B", "#2A3D48", "#142129");
            AccessibilityUi.TryApplyFocusVisualStyle(notifications);
            utilities.Children.Add(notifications);
            utilities.Children.Add(chrome.SettingsButton);
            chrome.SettingsButton.Margin = new Thickness(0);
            var imports = new StackPanel { Orientation = Orientation.Horizontal };
            imports.Children.Add(chrome.IntakeReviewButton);
            imports.Children.Add(chrome.ImportButton);
            imports.Children.Add(chrome.ImportCommentsButton);
            imports.Children.Add(chrome.ManualImportButton);
            AutomationProperties.SetName(imports, "Import actions");
            AutomationProperties.SetName(utilities, "Library utilities");
            chrome.NavBar.Child = BuildLibraryToolbarGroups(utilities, imports, 800);
            return chrome;
        }
    }
}
