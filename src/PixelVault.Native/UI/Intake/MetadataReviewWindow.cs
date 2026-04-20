using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PixelVaultNative
{
    /// <summary>Callbacks supplied by <see cref="MainWindow"/> for the pre-import metadata review modal (Phase C2).</summary>
    sealed class MetadataReviewServices
    {
        public Func<string, RoutedEventHandler, string, Brush, Button> CreateButton { get; set; }
        public Func<string, Brush> PreviewBadge { get; set; }
        public Func<string, int, BitmapImage> LoadImageSource { get; set; }
        public string GamePhotographyTag { get; set; }
    }

    /// <summary>Review comments / tags before metadata + move (extracted from MainWindow, Phase C2).</summary>
    static class MetadataReviewWindow
    {
        static SolidColorBrush B(string hex) => UiBrushHelper.FromHex(hex);

        /// <summary>Returns true if the user finished; false if canceled.</summary>
        public static bool Show(Window owner, string appVersion, List<ReviewItem> items, MetadataReviewServices services)
        {
            if (items == null || items.Count == 0) return true;
            if (services == null) throw new ArgumentNullException(nameof(services));

            Func<string, RoutedEventHandler, string, Brush, Button> Btn = services.CreateButton;
            Func<string, Brush> previewBadge = services.PreviewBadge;
            Func<string, int, BitmapImage> loadImage = services.LoadImageSource;
            string photoTagLabel = services.GamePhotographyTag ?? string.Empty;

            var reviewWindow = new Window
            {
                Title = "PixelVault " + appVersion + " Review",
                Width = 1260,
                Height = 900,
                MinWidth = 1020,
                MinHeight = 760,
                Owner = owner,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = B("#0F1519")
            };
            var root = new Grid { Margin = new Thickness(18) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            var banner = new Border { Background = B("#161C20"), CornerRadius = new CornerRadius(18), Padding = new Thickness(20), Margin = new Thickness(0, 0, 0, 16) };
            var bannerGrid = new Grid();
            bannerGrid.ColumnDefinitions.Add(new ColumnDefinition());
            bannerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bannerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var bannerStack = new StackPanel();
            bannerStack.Children.Add(new TextBlock { Text = "Review comments before finish", FontSize = 26, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
            bannerStack.Children.Add(new TextBlock { Text = items.Count + " image(s) are queued for metadata and move. Add optional comments, game-photography tags, console tags, or mark files for deletion before finishing.", Margin = new Thickness(0, 8, 0, 0), Foreground = B("#B7C6C0"), FontSize = 14, TextWrapping = TextWrapping.Wrap });
            bannerGrid.Children.Add(bannerStack);
            var cancelButton = Btn("Cancel Import", null, "#334249", Brushes.White);
            cancelButton.Margin = new Thickness(12, 0, 0, 0);
            cancelButton.Padding = new Thickness(22, 12, 22, 12);
            Grid.SetColumn(cancelButton, 1);
            bannerGrid.Children.Add(cancelButton);
            var finishButton = Btn("Finish", null, "#275D47", Brushes.White);
            finishButton.Margin = new Thickness(12, 0, 0, 0);
            finishButton.Padding = new Thickness(22, 12, 22, 12);
            Grid.SetColumn(finishButton, 2);
            bannerGrid.Children.Add(finishButton);
            banner.Child = bannerGrid;
            root.Children.Add(banner);
            var main = new Grid();
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(320) });
            main.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetRow(main, 1);
            root.Children.Add(main);
            var listCard = new Border { Background = B("#12191E"), CornerRadius = new CornerRadius(18), Padding = new Thickness(16), Margin = new Thickness(0, 0, 16, 0) };
            var listGrid = new Grid();
            listGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            listGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            listGrid.Children.Add(new TextBlock { Text = "Queued images", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Margin = new Thickness(0, 0, 0, 12) });
            var fileList = new ListBox
            {
                Background = B("#12191E"),
                BorderThickness = new Thickness(0),
                Foreground = Brushes.White,
                Padding = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            Grid.SetRow(fileList, 1);
            listGrid.Children.Add(fileList);
            listCard.Child = listGrid;
            main.Children.Add(listCard);
            var detailCard = new Border { Background = Brushes.White, BorderBrush = B("#D7E1E8"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18), Padding = new Thickness(18) };
            Grid.SetColumn(detailCard, 1);
            var detailGrid = new Grid();
            detailGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            detailGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            detailGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            detailGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var detailHeader = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            var selectedTitle = new TextBlock { Text = string.Empty, FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = B("#1F2A30"), TextWrapping = TextWrapping.Wrap };
            var selectedMeta = new TextBlock { Text = string.Empty, Foreground = B("#5F6970"), Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
            detailHeader.Children.Add(selectedTitle);
            detailHeader.Children.Add(selectedMeta);
            detailGrid.Children.Add(detailHeader);
            var previewBorder = new Border { Background = B("#F4F7FA"), CornerRadius = new CornerRadius(16), Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 14), BorderBrush = B("#D7E1E8"), BorderThickness = new Thickness(1) };
            var previewImage = new Image { Stretch = Stretch.Uniform };
            previewBorder.Child = previewImage;
            Grid.SetRow(previewBorder, 1);
            detailGrid.Children.Add(previewBorder);
            var commentHeader = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            commentHeader.ColumnDefinitions.Add(new ColumnDefinition());
            commentHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            commentHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var commentLabel = new TextBlock { Text = "Comment for Immich description", FontSize = 16, FontWeight = FontWeights.SemiBold, Foreground = B("#1F2A30"), VerticalAlignment = VerticalAlignment.Center };
            var photographyBox = new CheckBox { Content = "Add Game Photography tag", Foreground = B("#1F2A30"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            var steamBox = new CheckBox { Content = "Steam", Foreground = B("#1F2A30"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            var ps5Box = new CheckBox { Content = "PS5", Foreground = B("#1F2A30"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            var switchBox = new CheckBox { Content = "Switch", Foreground = B("#1F2A30"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            var xboxBox = new CheckBox { Content = "Xbox", Foreground = B("#1F2A30"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            var deleteBox = new CheckBox { Content = "Delete before processing", Foreground = B("#8B2F2F"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };
            var tagToggleRow = new WrapPanel { VerticalAlignment = VerticalAlignment.Center };
            tagToggleRow.Children.Add(photographyBox);
            tagToggleRow.Children.Add(steamBox);
            tagToggleRow.Children.Add(ps5Box);
            tagToggleRow.Children.Add(switchBox);
            tagToggleRow.Children.Add(xboxBox);
            commentHeader.Children.Add(commentLabel);
            Grid.SetColumn(tagToggleRow, 1);
            commentHeader.Children.Add(tagToggleRow);
            Grid.SetColumn(deleteBox, 2);
            commentHeader.Children.Add(deleteBox);
            Grid.SetRow(commentHeader, 2);
            detailGrid.Children.Add(commentHeader);
            var commentStack = new StackPanel();
            var commentBox = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MinHeight = 120,
                Background = Brushes.White,
                BorderBrush = B("#D7E1E8"),
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12),
                FontSize = 14
            };
            var commentStatus = new TextBlock { Text = "Leave blank to process normally.", Foreground = B("#5F6970"), Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
            commentStack.Children.Add(commentBox);
            commentStack.Children.Add(commentStatus);
            Grid.SetRow(commentStack, 3);
            detailGrid.Children.Add(commentStack);
            detailCard.Child = detailGrid;
            main.Children.Add(detailCard);
            reviewWindow.Content = root;
            bool suppressCommentSync = false;
            ReviewItem selectedItem = null;
            var reviewTileBorders = new Dictionary<ReviewItem, Border>();
            Action<ReviewItem> refreshReviewTile = delegate(ReviewItem item)
            {
                Border tileBorder;
                if (item == null || !reviewTileBorders.TryGetValue(item, out tileBorder)) return;
                if (item.DeleteBeforeProcessing)
                {
                    tileBorder.Background = B("#4A1F24");
                    tileBorder.BorderBrush = B("#C96A73");
                    tileBorder.BorderThickness = new Thickness(1);
                }
                else
                {
                    tileBorder.Background = B("#1A2329");
                    tileBorder.BorderBrush = B("#1A2329");
                    tileBorder.BorderThickness = new Thickness(1);
                }
            };
            Action refreshCommentStatus = delegate
            {
                if (selectedItem == null)
                {
                    commentStatus.Text = "Leave blank to process normally.";
                    return;
                }
                var notes = new List<string>();
                if (!string.IsNullOrWhiteSpace(selectedItem.Comment)) notes.Add("comment saved");
                if (selectedItem.AddPhotographyTag) notes.Add(photoTagLabel + " tag enabled");
                var consoleTags = new List<string>();
                if (selectedItem.TagSteam) consoleTags.Add("Steam");
                if (selectedItem.TagPs5) consoleTags.Add("PS5");
                if (selectedItem.TagSwitch) consoleTags.Add("Switch");
                if (selectedItem.TagXbox) consoleTags.Add("Xbox");
                if (consoleTags.Count > 0) notes.Add("platform tags: " + string.Join(", ", consoleTags.ToArray()));
                if (selectedItem.DeleteBeforeProcessing) notes.Add("marked for deletion");
                commentStatus.Text = notes.Count == 0 ? "Leave blank to process normally." : string.Join(" | ", notes.ToArray()) + ".";
            };
            Action<ReviewItem> showItem = delegate(ReviewItem item)
            {
                selectedItem = item;
                selectedTitle.Text = item.FileName;
                selectedMeta.Text = item.PlatformLabel + " | " + item.CaptureTime.ToString("MMMM d, yyyy h:mm:ss tt") + (item.PreserveFileTimes ? " | filesystem time preserved" : string.Empty);
                previewImage.Source = loadImage(item.FilePath, 1600);
                suppressCommentSync = true;
                commentBox.Text = item.Comment ?? string.Empty;
                photographyBox.IsChecked = item.AddPhotographyTag;
                steamBox.IsChecked = item.TagSteam;
                ps5Box.IsChecked = item.TagPs5;
                switchBox.IsChecked = item.TagSwitch;
                xboxBox.IsChecked = item.TagXbox;
                deleteBox.IsChecked = item.DeleteBeforeProcessing;
                suppressCommentSync = false;
                refreshReviewTile(item);
                refreshCommentStatus();
            };
            foreach (var item in items)
            {
                var tile = new ListBoxItem { Tag = item, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 10), BorderThickness = new Thickness(0), Background = Brushes.Transparent };
                var tileBorder = new Border { Background = B("#1A2329"), BorderBrush = B("#1A2329"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(12), Padding = new Thickness(10) };
                var tileStack = new StackPanel();
                tileStack.Children.Add(new TextBlock { Text = "[" + item.PlatformLabel + "]", Foreground = previewBadge(item.PlatformLabel), FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
                tileStack.Children.Add(new TextBlock { Text = item.FileName, Foreground = Brushes.White, TextWrapping = TextWrapping.Wrap, FontWeight = FontWeights.SemiBold });
                tileStack.Children.Add(new TextBlock { Text = item.CaptureTime.ToString("MMM d, yyyy h:mm tt"), Foreground = B("#B7C6C0"), Margin = new Thickness(0, 6, 0, 0) });
                tileBorder.Child = tileStack;
                tile.Content = tileBorder;
                reviewTileBorders[item] = tileBorder;
                refreshReviewTile(item);
                fileList.Items.Add(tile);
            }
            fileList.SelectionChanged += delegate
            {
                var entry = fileList.SelectedItem as ListBoxItem;
                if (entry != null && entry.Tag is ReviewItem) showItem((ReviewItem)entry.Tag);
            };
            commentBox.TextChanged += delegate
            {
                if (suppressCommentSync || selectedItem == null) return;
                selectedItem.Comment = commentBox.Text;
                refreshReviewTile(selectedItem);
                refreshCommentStatus();
            };
            photographyBox.Checked += delegate { if (suppressCommentSync || selectedItem == null) return; selectedItem.AddPhotographyTag = true; refreshReviewTile(selectedItem); refreshCommentStatus(); };
            photographyBox.Unchecked += delegate { if (suppressCommentSync || selectedItem == null) return; selectedItem.AddPhotographyTag = false; refreshReviewTile(selectedItem); refreshCommentStatus(); };
            steamBox.Checked += delegate { if (suppressCommentSync || selectedItem == null) return; selectedItem.TagSteam = true; refreshReviewTile(selectedItem); refreshCommentStatus(); };
            steamBox.Unchecked += delegate { if (suppressCommentSync || selectedItem == null) return; selectedItem.TagSteam = false; refreshReviewTile(selectedItem); refreshCommentStatus(); };
            ps5Box.Checked += delegate { if (suppressCommentSync || selectedItem == null) return; selectedItem.TagPs5 = true; refreshReviewTile(selectedItem); refreshCommentStatus(); };
            ps5Box.Unchecked += delegate { if (suppressCommentSync || selectedItem == null) return; selectedItem.TagPs5 = false; refreshReviewTile(selectedItem); refreshCommentStatus(); };
            switchBox.Checked += delegate { if (suppressCommentSync || selectedItem == null) return; selectedItem.TagSwitch = true; refreshReviewTile(selectedItem); refreshCommentStatus(); };
            switchBox.Unchecked += delegate { if (suppressCommentSync || selectedItem == null) return; selectedItem.TagSwitch = false; refreshReviewTile(selectedItem); refreshCommentStatus(); };
            xboxBox.Checked += delegate { if (suppressCommentSync || selectedItem == null) return; selectedItem.TagXbox = true; refreshReviewTile(selectedItem); refreshCommentStatus(); };
            xboxBox.Unchecked += delegate { if (suppressCommentSync || selectedItem == null) return; selectedItem.TagXbox = false; refreshReviewTile(selectedItem); refreshCommentStatus(); };
            deleteBox.Checked += delegate { if (suppressCommentSync || selectedItem == null) return; selectedItem.DeleteBeforeProcessing = true; refreshReviewTile(selectedItem); refreshCommentStatus(); };
            deleteBox.Unchecked += delegate { if (suppressCommentSync || selectedItem == null) return; selectedItem.DeleteBeforeProcessing = false; refreshReviewTile(selectedItem); refreshCommentStatus(); };
            finishButton.Click += delegate
            {
                if (!suppressCommentSync && selectedItem != null)
                {
                    selectedItem.Comment = commentBox.Text;
                    selectedItem.AddPhotographyTag = photographyBox.IsChecked == true;
                    selectedItem.TagSteam = steamBox.IsChecked == true;
                    selectedItem.TagPs5 = ps5Box.IsChecked == true;
                    selectedItem.TagSwitch = switchBox.IsChecked == true;
                    selectedItem.TagXbox = xboxBox.IsChecked == true;
                    selectedItem.DeleteBeforeProcessing = deleteBox.IsChecked == true;
                }
                var deleteCount = items.Count(i => i.DeleteBeforeProcessing);
                var processCount = items.Count - deleteCount;
                if (deleteCount > 0)
                {
                    var deleteChoice = MessageBox.Show(reviewWindow, deleteCount + " image(s) are marked for deletion.\n" + processCount + " image(s) will continue through metadata and move.\n\nYes = Finish and delete them\nNo = Finish without deleting\nCancel = Keep reviewing", "Finish Processing", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning);
                    if (deleteChoice == MessageBoxResult.Cancel) return;
                    if (deleteChoice == MessageBoxResult.No)
                    {
                        foreach (var item in items)
                        {
                            item.DeleteBeforeProcessing = false;
                            refreshReviewTile(item);
                        }
                        if (selectedItem != null)
                        {
                            suppressCommentSync = true;
                            deleteBox.IsChecked = false;
                            suppressCommentSync = false;
                            refreshCommentStatus();
                        }
                    }
                }
                else
                {
                    var confirm = MessageBox.Show(reviewWindow, processCount + " image(s) will continue through metadata and move.\n\nFinish processing?", "Finish Processing", MessageBoxButton.OKCancel, MessageBoxImage.Question);
                    if (confirm != MessageBoxResult.OK) return;
                }
                reviewWindow.DialogResult = true;
                reviewWindow.Close();
            };
            cancelButton.Click += delegate
            {
                reviewWindow.DialogResult = false;
                reviewWindow.Close();
            };
            if (items.Count > 0)
            {
                showItem(items[0]);
                fileList.SelectedIndex = 0;
            }
            var result = reviewWindow.ShowDialog();
            return result == true;
        }
    }
}
