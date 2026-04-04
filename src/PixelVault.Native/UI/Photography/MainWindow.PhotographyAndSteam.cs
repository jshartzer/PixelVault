using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        List<string> GetTaggedImagesCached(string root, bool forceRefresh, params string[] tagCandidates)
        {
            var stamp = BuildImageInventoryStamp(root);
            if (!forceRefresh)
            {
                var cached = LoadTaggedImageCache(root, stamp);
                if (cached != null)
                {
                    Log("Photography gallery cache hit.");
                    return cached;
                }
            }
            Log("Refreshing photography gallery cache.");
            var fresh = FindTaggedImages(root, tagCandidates);
            SaveTaggedImageCache(root, stamp, fresh);
            return fresh;
        }

        string BuildImageInventoryStamp(string root)
        {
            long latestTicks = 0;
            int count = 0;
            foreach (var file in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories).Where(IsMedia))
            {
                count++;
                var ticks = MetadataCacheStamp(file);
                if (ticks > latestTicks) latestTicks = ticks;
            }
            return count + "|" + latestTicks;
        }

        string TaggedImageCachePath(string root)
        {
            return Path.Combine(cacheRoot, "photography-gallery-" + SafeCacheName(root) + ".cache");
        }

        List<string> LoadTaggedImageCache(string root, string stamp)
        {
            var path = TaggedImageCachePath(root);
            if (!File.Exists(path)) return null;
            var lines = File.ReadAllLines(path);
            if (lines.Length < 2) return null;
            if (!string.Equals(lines[0], root, StringComparison.OrdinalIgnoreCase)) return null;
            if (!string.Equals(lines[1], stamp, StringComparison.Ordinal)) return null;
            return lines.Skip(2).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        void SaveTaggedImageCache(string root, string stamp, List<string> files)
        {
            var path = TaggedImageCachePath(root);
            var lines = new List<string>();
            lines.Add(root);
            lines.Add(stamp);
            lines.AddRange(files.Distinct(StringComparer.OrdinalIgnoreCase));
            File.WriteAllLines(path, lines.ToArray());
        }

        List<string> FindTaggedImages(string root, params string[] tagCandidates)
        {
            var tags = tagCandidates.Where(t => !string.IsNullOrWhiteSpace(t)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (tags.Count == 0) return new List<string>();
            var files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories)
                .Where(IsMedia)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var tagMap = ReadEmbeddedKeywordTagsBatch(files);
            return files
                .Where(file => tagMap.ContainsKey(file) && tagMap[file].Any(tag => tags.Any(candidate => string.Equals(tag, candidate, StringComparison.OrdinalIgnoreCase))))
                .ToList();
        }

        void ShowPhotographyGallery(Window owner)
        {
            try
            {
                EnsureDir(libraryWorkspace.LibraryRoot, "Library folder");
                EnsureExifTool();
                status.Text = "Loading photography gallery";
                var files = GetTaggedImagesCached(libraryWorkspace.LibraryRoot, false, GamePhotographyTag, "Photography");
                var galleryWindow = new Window
                {
                    Title = "PixelVault " + AppVersion + " Photography",
                    Width = 1320,
                    Height = 900,
                    MinWidth = 1080,
                    MinHeight = 760,
                    Owner = owner ?? this,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Background = Brush("#080A0D")
                };

                var root = new Grid { Margin = new Thickness(24), Background = Brushes.White };
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                var header = new Border { Background = Brush("#11161B"), CornerRadius = new CornerRadius(18), Padding = new Thickness(22), Margin = new Thickness(0, 0, 0, 18), BorderBrush = Brush("#273039"), BorderThickness = new Thickness(1) };
                var headerGrid = new Grid();
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition());
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var headerStack = new StackPanel();
                var galleryTitle = new TextBlock { Text = GamePhotographyTag, FontSize = 28, FontWeight = FontWeights.SemiBold, Foreground = Brush("#F5EFE4") };
                var galleryMeta = new TextBlock { Text = string.Empty, Margin = new Thickness(0, 8, 0, 0), Foreground = Brush("#B8B2A7"), FontSize = 14, TextWrapping = TextWrapping.Wrap };
                headerStack.Children.Add(galleryTitle);
                headerStack.Children.Add(galleryMeta);
                headerGrid.Children.Add(headerStack);
                var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                var openLibraryButton = Btn("Open Library", null, "#1B232B", Brushes.White);
                openLibraryButton.Margin = new Thickness(12, 0, 0, 0);
                var refreshGalleryButton = Btn("Refresh", null, "#275D47", Brushes.White);
                refreshGalleryButton.Margin = new Thickness(12, 0, 0, 0);
                actions.Children.Add(openLibraryButton);
                actions.Children.Add(refreshGalleryButton);
                Grid.SetColumn(actions, 1);
                headerGrid.Children.Add(actions);
                header.Child = headerGrid;
                root.Children.Add(header);

                var body = new Border { Background = Brush("#0D1115"), CornerRadius = new CornerRadius(18), Padding = new Thickness(22), BorderBrush = Brush("#20272F"), BorderThickness = new Thickness(1) };
                Grid.SetRow(body, 1);
                var bodyGrid = new Grid();
                bodyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                bodyGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                var controls = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
                var thumbLabel = new TextBlock { Text = "Curated gallery", FontSize = 18, FontWeight = FontWeights.SemiBold, Foreground = Brush("#F5EFE4"), VerticalAlignment = VerticalAlignment.Center };
                controls.Children.Add(thumbLabel);
                var sliderPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
                var sliderLabel = new TextBlock { Text = "Frame width", Foreground = Brush("#B8B2A7"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
                var sizeValue = new TextBlock { Text = "600", Foreground = Brush("#B8B2A7"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0), Width = 38 };
                var sizeSlider = new Slider { Minimum = 440, Maximum = 840, Value = 600, Width = 180, TickFrequency = 20, IsSnapToTickEnabled = true };
                sliderPanel.Children.Add(sliderLabel);
                sliderPanel.Children.Add(sizeSlider);
                sliderPanel.Children.Add(sizeValue);
                DockPanel.SetDock(sliderPanel, Dock.Right);
                controls.Children.Add(sliderPanel);
                bodyGrid.Children.Add(controls);
                var scroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Background = Brush("#0D1115") };
                var panel = new WrapPanel { Orientation = Orientation.Horizontal };
                scroll.Content = panel;
                Grid.SetRow(scroll, 1);
                bodyGrid.Children.Add(scroll);
                body.Child = bodyGrid;
                root.Children.Add(body);
                galleryWindow.Content = root;

                Action render = delegate
                {
                    panel.Children.Clear();
                    var ordered = files.OrderByDescending(GetLibraryDate).ThenBy(Path.GetFileName).ToList();
                    galleryMeta.Text = ordered.Count + " capture(s) tagged for game photography in " + libraryWorkspace.LibraryRoot;
                    sizeValue.Text = ((int)sizeSlider.Value).ToString();
                    if (ordered.Count == 0)
                    {
                        panel.Children.Add(new TextBlock { Text = "No " + GamePhotographyTag + " captures found yet.", Foreground = Brush("#B8B2A7"), FontSize = 15, Margin = new Thickness(8) });
                        return;
                    }
                    var width = (int)sizeSlider.Value;
                    foreach (var file in ordered)
                    {
                        var tile = new Border { Width = width, Margin = new Thickness(0, 0, 18, 22), Background = Brush("#12181E"), CornerRadius = new CornerRadius(10), BorderBrush = Brush("#262F38"), BorderThickness = new Thickness(1), Tag = file };
                        var tileStack = new StackPanel();
                        var frame = new Border { Background = Brush("#050607"), Margin = new Thickness(14, 14, 14, 10), Padding = new Thickness(10), CornerRadius = new CornerRadius(4) };
                        var presenter = new Grid();
                        var placeholder = new TextBlock { Text = Path.GetFileName(file), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(10), Foreground = Brush("#F5EFE4"), TextAlignment = TextAlignment.Center };
                        var image = new Image { Width = width - 48, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, Visibility = Visibility.Collapsed };
                        presenter.Children.Add(placeholder);
                        presenter.Children.Add(image);
                        frame.Child = presenter;
                        QueueImageLoad(image, file, width * 2, delegate(BitmapImage loaded)
                        {
                            image.Source = loaded;
                            image.Visibility = Visibility.Visible;
                            placeholder.Visibility = Visibility.Collapsed;
                        });
                        tileStack.Children.Add(frame);
                        tileStack.Children.Add(new TextBlock { Text = Path.GetFileName(Path.GetDirectoryName(file)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(14, 0, 14, 14), Foreground = Brush("#F5EFE4"), FontWeight = FontWeights.SemiBold, FontSize = 16, TextAlignment = TextAlignment.Center });
                        tile.Child = tileStack;
                        tile.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
                        {
                            if (e.ClickCount >= 2)
                            {
                                var clicked = sender as Border;
                                if (clicked != null && clicked.Tag is string) OpenWithShell((string)clicked.Tag);
                            }
                        };
                        panel.Children.Add(tile);
                    }
                };

                refreshGalleryButton.Click += delegate
                {
                    status.Text = "Refreshing photography gallery";
                    files = GetTaggedImagesCached(libraryWorkspace.LibraryRoot, true, GamePhotographyTag, "Photography");
                    render();
                    status.Text = "Photography gallery ready";
                };
                openLibraryButton.Click += delegate { OpenFolder(libraryWorkspace.LibraryRoot); };
                sizeSlider.ValueChanged += delegate { render(); };

                render();
                galleryWindow.Show();
                status.Text = "Photography gallery ready";
            }
            catch (Exception ex)
            {
                LogException("ShowPhotographyGallery", ex);
                MessageBox.Show(ex.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        Tuple<string, string> ShowSteamAppMatchWindow(Window owner, string query, List<Tuple<string, string>> matches)
        {
            var candidates = (matches ?? new List<Tuple<string, string>>()).Where(match => match != null && !string.IsNullOrWhiteSpace(match.Item1) && !string.IsNullOrWhiteSpace(match.Item2)).Take(24).ToList();
            if (candidates.Count == 0) return null;

            Tuple<string, string> selected = null;
            var wanted = NormalizeTitle(query);
            var pickerWindow = new Window
            {
                Title = "PixelVault " + AppVersion + " Steam Matches",
                Width = 760,
                Height = 720,
                MinWidth = 680,
                MinHeight = 560,
                Owner = owner ?? this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Brush("#0F1519")
            };

            var root = new Grid { Margin = new Thickness(18) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new Border { Background = Brush("#161C20"), CornerRadius = new CornerRadius(18), Padding = new Thickness(18), Margin = new Thickness(0, 0, 0, 14) };
            var headerStack = new StackPanel();
            headerStack.Children.Add(new TextBlock { Text = "Choose the Steam match", FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
            headerStack.Children.Add(new TextBlock { Text = "Results for \"" + query + "\". Pick the right game and PixelVault will save its AppID before import.", Margin = new Thickness(0, 8, 0, 0), Foreground = Brush("#B7C6C0"), FontSize = 14, TextWrapping = TextWrapping.Wrap });
            header.Child = headerStack;
            root.Children.Add(header);

            var list = new ListBox
            {
                Background = Brush("#12191E"),
                BorderBrush = Brush("#243139"),
                BorderThickness = new Thickness(1),
                Foreground = Brushes.White,
                Padding = new Thickness(12),
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            Grid.SetRow(list, 1);
            root.Children.Add(list);

            var selectedIndex = 0;
            for (int i = 0; i < candidates.Count; i++)
            {
                var match = candidates[i];
                var isExact = NormalizeTitle(match.Item2) == wanted;
                if (isExact) selectedIndex = i;
                var item = new ListBoxItem { Tag = match, Padding = new Thickness(0), Margin = new Thickness(0, 0, 0, 10), BorderThickness = new Thickness(0), Background = Brushes.Transparent };
                var border = new Border
                {
                    Background = isExact ? Brush("#183A30") : Brush("#1A2329"),
                    BorderBrush = isExact ? Brush("#3FAE7C") : Brush("#243139"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(14, 12, 14, 12)
                };
                var stack = new StackPanel();
                stack.Children.Add(new TextBlock { Text = match.Item2, Foreground = Brushes.White, FontSize = 16, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
                stack.Children.Add(new TextBlock { Text = "Steam AppID " + match.Item1 + (isExact ? " | exact title match" : string.Empty), Foreground = isExact ? Brush("#BEE8D3") : Brush("#9FB0BA"), Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap });
                border.Child = stack;
                item.Content = border;
                list.Items.Add(item);
            }

            var buttons = new Grid { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 14, 0, 0) };
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var cancelButton = Btn("Cancel", null, "#334249", Brushes.White);
            cancelButton.Margin = new Thickness(0);
            var selectButton = Btn("Use Match", null, "#275D47", Brushes.White);
            selectButton.Margin = new Thickness(12, 0, 0, 0);
            selectButton.IsEnabled = candidates.Count > 0;
            buttons.Children.Add(cancelButton);
            Grid.SetColumn(selectButton, 1);
            buttons.Children.Add(selectButton);
            Grid.SetRow(buttons, 2);
            root.Children.Add(buttons);

            Action<bool> closeWindow = delegate(bool accept)
            {
                if (accept)
                {
                    var selectedItem = list.SelectedItem as ListBoxItem;
                    if (selectedItem == null || !(selectedItem.Tag is Tuple<string, string>)) return;
                    selected = (Tuple<string, string>)selectedItem.Tag;
                    pickerWindow.DialogResult = true;
                }
                else
                {
                    pickerWindow.DialogResult = false;
                }
                pickerWindow.Close();
            };

            list.SelectionChanged += delegate
            {
                selectButton.IsEnabled = list.SelectedItem is ListBoxItem;
            };
            list.MouseDoubleClick += delegate
            {
                if (list.SelectedItem is ListBoxItem) closeWindow(true);
            };
            cancelButton.Click += delegate { closeWindow(false); };
            selectButton.Click += delegate { closeWindow(true); };
            if (list.Items.Count > 0) list.SelectedIndex = selectedIndex;

            pickerWindow.Content = root;
            return pickerWindow.ShowDialog() == true ? selected : null;
        }
    }
}
