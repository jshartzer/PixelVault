using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace PixelVaultNative
{
    sealed class AchievementsInfoWindow : Window
    {
        readonly TextBlock _heading;
        readonly TextBlock _detail;
        readonly TextBlock _error;
        readonly ListBox _list;
        readonly TextBlock _empty;

        AchievementsInfoWindow(Window owner, string initialHeading)
        {
            Owner = owner;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Title = "Achievements";
            Width = 720;
            Height = 720;
            MinWidth = 560;
            MinHeight = 420;
            Background = UiBrushHelper.FromHex("#11181D");

            var root = new Grid { Margin = new Thickness(22, 18, 22, 16) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _heading = new TextBlock
            {
                Text = initialHeading ?? "Loading…",
                FontSize = 22,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap
            };
            root.Children.Add(_heading);

            _detail = new TextBlock
            {
                Margin = new Thickness(0, 8, 0, 0),
                FontSize = 13,
                Foreground = UiBrushHelper.FromHex("#9CB1BC"),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetRow(_detail, 1);
            root.Children.Add(_detail);

            _error = new TextBlock
            {
                Margin = new Thickness(0, 10, 0, 0),
                FontSize = 13,
                Foreground = UiBrushHelper.FromHex("#E8A598"),
                TextWrapping = TextWrapping.Wrap,
                Visibility = Visibility.Collapsed
            };
            Grid.SetRow(_error, 2);
            root.Children.Add(_error);

            _empty = new TextBlock
            {
                Margin = new Thickness(0, 12, 0, 0),
                FontSize = 13,
                Foreground = UiBrushHelper.FromHex("#7D909C"),
                Text = "No achievement definitions were returned for this game.",
                Visibility = Visibility.Collapsed
            };
            Grid.SetRow(_empty, 3);
            root.Children.Add(_empty);

            _list = new ListBox
            {
                Margin = new Thickness(0, 12, 0, 12),
                Background = UiBrushHelper.FromHex("#0D1216"),
                BorderBrush = UiBrushHelper.FromHex("#27313A"),
                BorderThickness = new Thickness(1),
                Foreground = Brushes.White,
                HorizontalContentAlignment = HorizontalAlignment.Stretch
            };
            VirtualizingPanel.SetIsVirtualizing(_list, true);
            VirtualizingPanel.SetVirtualizationMode(_list, VirtualizationMode.Recycling);
            Grid.SetRow(_list, 4);
            root.Children.Add(_list);

            var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 4, 0, 0) };
            var close = new Button
            {
                Content = "Close",
                Width = 120,
                Height = 40,
                Foreground = Brushes.White,
                Background = UiBrushHelper.FromHex("#20343A"),
                BorderBrush = UiBrushHelper.FromHex("#C0CCD6"),
                BorderThickness = new Thickness(1),
                FontWeight = FontWeights.SemiBold,
                Effect = new DropShadowEffect { Color = Color.FromArgb(64, 18, 27, 36), BlurRadius = 16, ShadowDepth = 4, Direction = 270, Opacity = 0.55 }
            };
            close.Click += delegate { Close(); };
            AccessibilityUi.TryApplyFocusVisualStyle(close);
            buttons.Children.Add(close);
            Grid.SetRow(buttons, 5);
            root.Children.Add(buttons);

            Content = root;
        }

        internal static void ShowModal(
            Window owner,
            string normalizedPlatform,
            LibraryFolderInfo folder,
            string steamKey,
            string retroKey,
            string steamUserId64,
            string retroAchievementsUsername,
            string userAgent)
        {
            var title = folder == null ? string.Empty : (folder.Name ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(title)) title = "Achievements";
            var dlg = new AchievementsInfoWindow(owner, title);
            AutomationProperties.SetName(dlg, "Achievements");
            dlg.Loaded += async (_, __) => { await dlg.LoadAsync(normalizedPlatform, folder, steamKey, retroKey, steamUserId64, retroAchievementsUsername, userAgent); };
            dlg.ShowDialog();
        }

        async Task LoadAsync(
            string normalizedPlatform,
            LibraryFolderInfo folder,
            string steamKey,
            string retroKey,
            string steamUserId64,
            string retroAchievementsUsername,
            string userAgent)
        {
            GameAchievementsFetchService.FetchResult result;
            try
            {
                result = await GameAchievementsFetchService.FetchAsync(
                    normalizedPlatform,
                    folder,
                    steamKey,
                    retroKey,
                    steamUserId64,
                    retroAchievementsUsername,
                    userAgent,
                    default);
            }
            catch (Exception ex)
            {
                result = new GameAchievementsFetchService.FetchResult { ErrorMessage = ex.Message };
            }

            Title = result.IsError
                ? "Achievements"
                : ("Achievements — " + (result.SourceLabel ?? ""));

            var displayTitle = result.IsError
                ? (folder == null ? string.Empty : (folder.Name ?? string.Empty).Trim())
                : (result.GameTitle ?? string.Empty);
            if (string.IsNullOrWhiteSpace(displayTitle) && folder != null)
                displayTitle = folder.Name ?? string.Empty;
            _heading.Text = string.IsNullOrWhiteSpace(displayTitle) ? "Achievements" : displayTitle;
            _detail.Text = result.IsError ? string.Empty : (result.DetailLine ?? string.Empty);

            if (result.IsError)
            {
                _error.Visibility = Visibility.Visible;
                _error.Text = result.ErrorMessage ?? "Unknown error.";
                _list.Visibility = Visibility.Collapsed;
                _empty.Visibility = Visibility.Collapsed;
                return;
            }

            _error.Visibility = Visibility.Collapsed;
            _list.Items.Clear();
            var rows = result.Rows ?? new List<GameAchievementsFetchService.AchievementRow>();
            if (rows.Count == 0)
            {
                _list.Visibility = Visibility.Collapsed;
                _empty.Visibility = Visibility.Visible;
                return;
            }

            _empty.Visibility = Visibility.Collapsed;
            _list.Visibility = Visibility.Visible;

            var lastSection = string.Empty;
            foreach (var row in rows)
            {
                var section = !row.ProgressKnown ? "unknown" : (row.Unlocked ? "unlocked" : "locked");
                if (!string.Equals(section, lastSection, StringComparison.Ordinal))
                {
                    lastSection = section;
                    var headerText = section == "unlocked"
                        ? "Unlocked"
                        : section == "locked"
                            ? "Locked"
                            : UnknownProgressSectionTitle(result.SourceLabel);
                    _list.Items.Add(new TextBlock
                    {
                        Text = headerText,
                        FontWeight = FontWeights.SemiBold,
                        FontSize = 12,
                        Foreground = UiBrushHelper.FromHex("#7D909C"),
                        Margin = new Thickness(8, section == "unlocked" ? 4 : 14, 0, 4)
                    });
                }

                _list.Items.Add(BuildAchievementRowCard(row));
            }
        }

        static string UnknownProgressSectionTitle(string sourceLabel)
        {
            if (string.Equals(sourceLabel, "RetroAchievements", StringComparison.OrdinalIgnoreCase))
                return "Progress unknown — add your RetroAchievements site username in Path Settings (under RetroAchievements API key).";
            if (string.Equals(sourceLabel, "Steam", StringComparison.OrdinalIgnoreCase))
                return "Progress unknown — add your SteamID64 in Path Settings.";
            return "Progress unknown";
        }

        static Border BuildAchievementRowCard(GameAchievementsFetchService.AchievementRow row)
        {
            var card = new Border
            {
                BorderBrush = UiBrushHelper.FromHex("#24323A"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(10, 8, 10, 8),
                Background = Brushes.Transparent
            };
            var grid = new Grid { MinHeight = 52 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var badgeHost = new Border
            {
                Width = 48,
                Height = 48,
                Margin = new Thickness(0, 0, 12, 0),
                CornerRadius = new CornerRadius(6),
                ClipToBounds = true,
                Background = UiBrushHelper.FromHex("#141E24"),
                BorderBrush = UiBrushHelper.FromHex("#2B3A44"),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Top
            };
            var img = new Image { Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            badgeHost.Child = img;
            ApplyAchievementBadgeArt(img, row);
            Grid.SetColumn(badgeHost, 0);
            grid.Children.Add(badgeHost);

            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = row.Title ?? string.Empty,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap
            });
            if (!string.IsNullOrWhiteSpace(row.Description))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = row.Description,
                    Margin = new Thickness(0, 4, 0, 0),
                    Foreground = UiBrushHelper.FromHex("#9CB1BC"),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap
                });
            }
            Grid.SetColumn(stack, 1);
            grid.Children.Add(stack);

            if (!string.IsNullOrWhiteSpace(row.Meta))
            {
                var meta = new TextBlock
                {
                    Text = row.Meta,
                    Foreground = UiBrushHelper.FromHex("#B8C9D4"),
                    FontSize = 11,
                    Margin = new Thickness(14, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Top
                };
                Grid.SetColumn(meta, 2);
                grid.Children.Add(meta);
            }

            card.Child = grid;
            return card;
        }

        /// <summary>Unlocked or unknown progress: full color. Locked: Steam <see cref="GameAchievementsFetchService.AchievementRow.IconUrlGray"/> when set, else grayscale the color badge.</summary>
        static void ApplyAchievementBadgeArt(Image img, GameAchievementsFetchService.AchievementRow row)
        {
            var grayOut = row.ProgressKnown && !row.Unlocked;
            string url;
            if (grayOut && !string.IsNullOrWhiteSpace(row.IconUrlGray))
                url = row.IconUrlGray;
            else if (!string.IsNullOrWhiteSpace(row.IconUrlColor))
                url = row.IconUrlColor;
            else
            {
                img.Visibility = Visibility.Collapsed;
                return;
            }

            img.Visibility = Visibility.Visible;
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(url, UriKind.Absolute);
            bmp.DecodePixelWidth = 108;
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.CreateOptions = BitmapCreateOptions.IgnoreImageCache;
            bmp.EndInit();

            if (grayOut && string.IsNullOrWhiteSpace(row.IconUrlGray))
            {
                bmp.DownloadCompleted += delegate { ApplyGrayscaleSource(img, bmp); };
                img.Source = bmp;
                return;
            }

            img.Source = bmp;
        }

        static void ApplyGrayscaleSource(Image img, BitmapSource colorSource)
        {
            try
            {
                var fmt = new FormatConvertedBitmap();
                fmt.BeginInit();
                fmt.Source = colorSource;
                fmt.DestinationFormat = PixelFormats.Gray8;
                fmt.EndInit();
                fmt.Freeze();
                img.Source = fmt;
            }
            catch
            {
                img.Source = colorSource;
            }
        }
    }
}
