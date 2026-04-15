using System;
using System.Collections.Generic;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        /// <summary>Mutable state + control tree for the manual metadata / import-and-edit dialog.</summary>
        internal sealed class ManualMetadataDialogHost
        {
            public List<ManualMetadataItem> Items;
            public bool LibraryMode;
            public bool ImportAndEditMode;
            public bool UseFlexiblePreview;
            public string EmptySelectionText;
            public string DefaultStatusText;
            public string SingleSelectionMetaPrefix;
            public string ConfirmCaption;

            public Window ManualWindow;
            public Button LeaveButton;
            public Button FinishButton;
            public ListBox FileList;
            public TextBlock SelectedTitle;
            public TextBlock SelectedMeta;
            public Border PreviewBorder;
            public Image PreviewImage;
            public TextBlock GuessText;
            public TextBox SteamSearchBox;
            public Button SteamSearchButton;
            public TextBox SteamAppIdBox;
            public TextBlock SteamLookupStatus;
            public Button SameAsPreviousButton;
            public ComboBox GameNameBox;
            public TextBox TagsBox;
            public CheckBox PhotographyBox;
            public CheckBox SteamBox;
            public CheckBox PcBox;
            public CheckBox EmulationBox;
            public CheckBox Ps5Box;
            public CheckBox XboxBox;
            public CheckBox OtherBox;
            public TextBox OtherPlatformBox;
            public CheckBox UseCustomTimeBox;
            public DatePicker CaptureDatePicker;
            public ComboBox HourBox;
            public ComboBox MinuteBox;
            public ComboBox AmpmBox;
            public TextBox CommentBox;
            public TextBlock StatusText;
            public CheckBox DeleteBeforeBox;

            public bool SuppressSync;
            public bool DialogReady;
            public CancellationTokenSource SteamSearchCancellation;
            public int SteamSearchRequestVersion;
            public int GameTitleChoicesRefreshVersion;
            public List<ManualMetadataItem> SelectedItems = new List<ManualMetadataItem>();
            public Dictionary<ManualMetadataItem, TextBlock> BadgeBlocks = new Dictionary<ManualMetadataItem, TextBlock>();
            public Dictionary<ManualMetadataItem, Border> TileBorders = new Dictionary<ManualMetadataItem, Border>();
            public List<string> KnownGameChoices = new List<string>();
            public HashSet<string> KnownGameChoiceSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            public Dictionary<string, string> KnownGameChoiceNameMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            /// <summary>Last loaded game index rows for title choices; used to align <see cref="ManualMetadataItem.GameId"/> with edited title + platform.</summary>
            public List<GameIndexEditorRow> GameTitleIndexRows;
        }

        void BuildManualMetadataDialogLayout(ManualMetadataDialogHost h, string windowLabel, string headerTitleText, string headerDescriptionText, string leaveButtonText, string finishButtonText)
        {
            var resolvedOwner = ResolveStatusWindowOwner();
            h.ManualWindow = new Window
            {
                Title = "PixelVault " + AppVersion + " " + windowLabel,
                Width = 1220,
                Height = 1040,
                MinWidth = 1040,
                MinHeight = 920,
                Owner = resolvedOwner,
                WindowStartupLocation = resolvedOwner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
                Background = Brush("#0F1519")
            };

            var root = new Grid { Margin = new Thickness(18) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            var banner = new Border { Background = Brush("#161C20"), CornerRadius = new CornerRadius(18), Padding = new Thickness(20), Margin = new Thickness(0, 0, 0, 16) };
            var bannerGrid = new Grid();
            bannerGrid.ColumnDefinitions.Add(new ColumnDefinition());
            bannerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bannerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var bannerStack = new StackPanel();
            bannerStack.Children.Add(new TextBlock { Text = headerTitleText, FontSize = 26, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
            bannerStack.Children.Add(new TextBlock { Text = headerDescriptionText, Margin = new Thickness(0, 8, 0, 0), Foreground = Brush("#B7C6C0"), FontSize = 14, TextWrapping = TextWrapping.Wrap });
            bannerGrid.Children.Add(bannerStack);
            h.LeaveButton = Btn(leaveButtonText, null, "#334249", Brushes.White);
            h.LeaveButton.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(h.LeaveButton, 1);
            bannerGrid.Children.Add(h.LeaveButton);
            h.FinishButton = Btn(finishButtonText, null, "#275D47", Brushes.White);
            h.FinishButton.Margin = new Thickness(12, 0, 0, 0);
            Grid.SetColumn(h.FinishButton, 2);
            bannerGrid.Children.Add(h.FinishButton);
            banner.Child = bannerGrid;
            root.Children.Add(banner);

            var main = new Grid();
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(main, 1);
            root.Children.Add(main);

            var listCard = new Border { Background = Brush("#12191E"), CornerRadius = new CornerRadius(18), Padding = new Thickness(8), Margin = new Thickness(0, 0, 16, 0) };
            h.FileList = new ListBox { Background = Brush("#12191E"), BorderThickness = new Thickness(0), Foreground = Brushes.White, Padding = new Thickness(10), HorizontalContentAlignment = HorizontalAlignment.Stretch, SelectionMode = SelectionMode.Extended };
            listCard.Child = h.FileList;
            main.Children.Add(listCard);

            var detailCard = new Border { Background = Brushes.White, CornerRadius = new CornerRadius(18), Padding = new Thickness(18), BorderBrush = Brush("#D7E1E8"), BorderThickness = new Thickness(1) };
            Grid.SetColumn(detailCard, 1);
            main.Children.Add(detailCard);

            var detailScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            var detailStack = new StackPanel();
            detailScroll.Content = detailStack;
            detailCard.Child = detailScroll;

            h.SelectedTitle = new TextBlock { Text = "Select one or more captures", FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2A30"), TextWrapping = TextWrapping.Wrap };
            h.SelectedMeta = new TextBlock { Text = h.EmptySelectionText, Foreground = Brush("#5F6970"), Margin = new Thickness(0, 8, 0, 12), TextWrapping = TextWrapping.Wrap };
            h.PreviewBorder = new Border { Background = Brush("#EAF0F5"), CornerRadius = new CornerRadius(16), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 16), BorderBrush = Brush("#D7E1E8"), BorderThickness = new Thickness(1) };
            h.PreviewImage = new Image { Stretch = Stretch.Uniform };
            if (h.UseFlexiblePreview)
            {
                h.PreviewBorder.MinHeight = 200;
                h.PreviewImage.MinHeight = 200;
                h.PreviewImage.MaxHeight = 420;
                h.ManualWindow.SizeChanged += delegate
                {
                    if (!h.ManualWindow.IsLoaded) return;
                    var height = Math.Max(220, (h.ManualWindow.ActualHeight - 460) * 0.45);
                    h.PreviewImage.MaxHeight = height;
                };
            }
            else h.PreviewImage.Height = 320;
            var guessCallout = new Border { Background = Brush("#F4F7F9"), BorderBrush = Brush("#D7E1E8"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 0, 16) };
            h.GuessText = new TextBlock { Text = "Best Guess | No confident match", FontSize = 13, Foreground = Brush("#8B98A3"), TextWrapping = TextWrapping.Wrap };
            guessCallout.Child = h.GuessText;
            var steamLookupLabel = new TextBlock { Text = "Steam lookup", FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2A30") };
            var steamLookupGrid = new Grid { Margin = new Thickness(0, 8, 0, 14) };
            steamLookupGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            steamLookupGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            steamLookupGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
            h.SteamSearchBox = new TextBox { Margin = new Thickness(0, 0, 12, 0), Background = Brushes.White, BorderBrush = Brush("#D7E1E8"), BorderThickness = new Thickness(1), Padding = new Thickness(12, 8, 12, 8), FontSize = 14 };
            h.SteamSearchButton = Btn("Search Steam", null, "#174A73", Brushes.White);
            h.SteamSearchButton.Width = 150;
            h.SteamSearchButton.Height = 40;
            h.SteamSearchButton.Margin = new Thickness(0, 0, 12, 0);
            h.SteamAppIdBox = new TextBox { Background = Brushes.White, BorderBrush = Brush("#D7E1E8"), BorderThickness = new Thickness(1), Padding = new Thickness(12, 8, 12, 8), FontSize = 14 };
            steamLookupGrid.Children.Add(h.SteamSearchBox);
            Grid.SetColumn(h.SteamSearchButton, 1);
            steamLookupGrid.Children.Add(h.SteamSearchButton);
            Grid.SetColumn(h.SteamAppIdBox, 2);
            steamLookupGrid.Children.Add(h.SteamAppIdBox);
            h.SteamLookupStatus = new TextBlock { Text = "Search by game title or numeric Steam AppID (Search looks up the store name), or paste an AppID in the box on the right.", Foreground = Brush("#5F6970"), Margin = new Thickness(0, 0, 0, 16), TextWrapping = TextWrapping.Wrap };
            h.TagsBox = new TextBox { Margin = new Thickness(0, 8, 0, 14), Background = Brushes.White, BorderBrush = Brush("#D7E1E8"), BorderThickness = new Thickness(1), Padding = new Thickness(12), FontSize = 14 };
            h.PhotographyBox = new CheckBox { Content = "Add Game Photography tag", Foreground = Brush("#1F2A30"), Margin = new Thickness(0, 0, 14, 10), IsThreeState = true };
            var tagSeparator = new Border { Width = 1, Height = 20, Background = Brush("#D7E1E8"), Margin = new Thickness(2, 2, 16, 10), VerticalAlignment = VerticalAlignment.Center };
            h.SteamBox = new CheckBox { Content = "Steam", Foreground = Brush("#1F2A30"), Margin = new Thickness(0, 0, 18, 10), IsThreeState = true };
            h.PcBox = new CheckBox { Content = "PC", Foreground = Brush("#1F2A30"), Margin = new Thickness(0, 0, 18, 10), IsThreeState = true };
            h.EmulationBox = new CheckBox { Content = "Emulation", Foreground = Brush("#1F2A30"), Margin = new Thickness(0, 0, 18, 10), IsThreeState = true };
            h.Ps5Box = new CheckBox { Content = "PS5", Foreground = Brush("#1F2A30"), Margin = new Thickness(0, 0, 18, 10), IsThreeState = true };
            h.XboxBox = new CheckBox { Content = "Xbox", Foreground = Brush("#1F2A30"), Margin = new Thickness(0, 0, 18, 10), IsThreeState = true };
            h.OtherBox = new CheckBox { Content = "Other", Foreground = Brush("#1F2A30"), Margin = new Thickness(0, 0, 12, 10), IsThreeState = true };
            h.OtherPlatformBox = new TextBox { Width = 190, Margin = new Thickness(0, 0, 0, 10), Background = Brushes.White, BorderBrush = Brush("#D7E1E8"), BorderThickness = new Thickness(1), Padding = new Thickness(10, 6, 10, 6), FontSize = 13, IsEnabled = false };
            var tagToggleRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 4) };
            tagToggleRow.Children.Add(h.PhotographyBox);
            tagToggleRow.Children.Add(tagSeparator);
            tagToggleRow.Children.Add(h.SteamBox);
            tagToggleRow.Children.Add(h.PcBox);
            tagToggleRow.Children.Add(h.EmulationBox);
            tagToggleRow.Children.Add(h.Ps5Box);
            tagToggleRow.Children.Add(h.XboxBox);
            tagToggleRow.Children.Add(h.OtherBox);
            tagToggleRow.Children.Add(h.OtherPlatformBox);
            h.UseCustomTimeBox = new CheckBox { Content = "Use custom date/time", Foreground = Brush("#1F2A30"), Margin = new Thickness(0, 0, 0, 8), IsThreeState = true };
            var dateRow = new WrapPanel { Margin = new Thickness(0, 0, 0, 14) };
            h.CaptureDatePicker = new DatePicker { Width = 170 };
            h.HourBox = new ComboBox { Width = 68, Margin = new Thickness(12, 0, 0, 0) };
            for (int hour = 1; hour <= 12; hour++) h.HourBox.Items.Add(hour.ToString());
            h.MinuteBox = new ComboBox { Width = 72, Margin = new Thickness(8, 0, 0, 0) };
            for (int minute = 0; minute < 60; minute++) h.MinuteBox.Items.Add(minute.ToString("00"));
            h.AmpmBox = new ComboBox { Width = 72, Margin = new Thickness(8, 0, 0, 0) };
            h.AmpmBox.Items.Add("AM");
            h.AmpmBox.Items.Add("PM");
            dateRow.Children.Add(h.CaptureDatePicker);
            dateRow.Children.Add(h.HourBox);
            dateRow.Children.Add(h.MinuteBox);
            dateRow.Children.Add(h.AmpmBox);
            h.CommentBox = new TextBox { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MinHeight = 120, Margin = new Thickness(0, 8, 0, 0), Background = Brushes.White, BorderBrush = Brush("#D7E1E8"), BorderThickness = new Thickness(1), Padding = new Thickness(12), FontSize = 14 };
            h.StatusText = new TextBlock { Foreground = Brush("#5F6970"), Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };

            detailStack.Children.Add(h.SelectedTitle);
            detailStack.Children.Add(h.SelectedMeta);
            detailStack.Children.Add(h.PreviewBorder);
            detailStack.Children.Add(guessCallout);
            detailStack.Children.Add(steamLookupLabel);
            detailStack.Children.Add(steamLookupGrid);
            detailStack.Children.Add(h.SteamLookupStatus);
            detailStack.Children.Add(new TextBlock { Text = "Game title to prepend", FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2A30") });
            var gameTitleRow = new Grid { Margin = new Thickness(0, 8, 0, 14) };
            gameTitleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            gameTitleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            h.SameAsPreviousButton = Btn("Same as previous", null, "#334249", Brushes.White);
            h.SameAsPreviousButton.ToolTip = "Copy game title, Steam AppID, tags, platform, date, and comment from the file immediately above each selected row in the list.";
            h.SameAsPreviousButton.Margin = new Thickness(0, 0, 10, 0);
            h.SameAsPreviousButton.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(h.SameAsPreviousButton, 0);
            h.GameNameBox = new ComboBox
            {
                Background = Brushes.White,
                BorderBrush = Brush("#D7E1E8"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(8, 4, 8, 4),
                FontSize = 14,
                FontFamily = new FontFamily("Cascadia Mono"),
                IsEditable = true,
                IsTextSearchEnabled = true,
                StaysOpenOnEdit = true
            };
            Grid.SetColumn(h.GameNameBox, 1);
            gameTitleRow.Children.Add(h.SameAsPreviousButton);
            gameTitleRow.Children.Add(h.GameNameBox);
            detailStack.Children.Add(gameTitleRow);
            detailStack.Children.Add(new TextBlock { Text = "Additional tags", FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2A30") });
            detailStack.Children.Add(h.TagsBox);
            detailStack.Children.Add(tagToggleRow);
            detailStack.Children.Add(new TextBlock { Text = "Capture date and time", FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2A30") });
            detailStack.Children.Add(h.UseCustomTimeBox);
            detailStack.Children.Add(new TextBlock { Text = "If left off, PixelVault uses the existing filesystem timestamp.", Foreground = Brush("#5F6970"), Margin = new Thickness(0, 0, 0, 8), TextWrapping = TextWrapping.Wrap });
            detailStack.Children.Add(dateRow);
            h.DeleteBeforeBox = new CheckBox
            {
                Content = "Delete selected file(s) before import (skipped for metadata and move)",
                Foreground = Brush("#9B2C2C"),
                Margin = new Thickness(0, 0, 0, 12),
                Visibility = h.ImportAndEditMode ? Visibility.Visible : Visibility.Collapsed
            };
            detailStack.Children.Add(h.DeleteBeforeBox);
            detailStack.Children.Add(new TextBlock { Text = "Comment for Immich description", FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2A30") });
            detailStack.Children.Add(h.CommentBox);
            detailStack.Children.Add(h.StatusText);

            h.ManualWindow.Content = root;
        }
    }
}
