using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Threading;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        internal void LibraryBrowserOpenGameProfile(Window owner, LibraryBrowserFolderView view)
        {
            if (view == null || IsLibraryBrowserTimeProjectionView(view)) return;
            var folder = BuildLibraryBrowserDisplayFolder(view);
            if (folder == null) return;
            ShowLibraryGameProfileWindow(owner ?? this, view, folder);
        }

        void ShowLibraryGameProfileWindow(Window owner, LibraryBrowserFolderView view, LibraryFolderInfo folder)
        {
            var title = string.IsNullOrWhiteSpace(folder.Name) ? "Game Profile" : folder.Name.Trim();
            var win = new Window
            {
                Title = "PixelVault Game Profile - " + title,
                Width = 1180,
                Height = 860,
                MinWidth = 900,
                MinHeight = 640,
                Owner = owner,
                WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner,
                Background = Brush("#0B1116")
            };
            AutomationProperties.SetName(win, "Game Profile - " + title);

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.Children.Add(BuildLibraryGameProfileHero(win, view, folder));

            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(22, 18, 22, 24)
            };
            Grid.SetRow(scroll, 1);
            var body = new StackPanel();
            scroll.Content = body;
            var files = GetFilesForLibraryFolderEntry(folder, false)
                .Where(file => !string.IsNullOrWhiteSpace(file) && File.Exists(file))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var metadataIndex = librarySession == null || !librarySession.HasLibraryRoot
                ? new Dictionary<string, LibraryMetadataIndexEntry>(StringComparer.OrdinalIgnoreCase)
                : librarySession.LoadLibraryMetadataIndexForFilePaths(files);
            var orderedFiles = files
                .Select(file => new { File = file, Date = ResolveLibraryProfileCaptureDate(file, metadataIndex) })
                .OrderByDescending(row => row.Date)
                .ThenBy(row => row.File, StringComparer.OrdinalIgnoreCase)
                .ToList();
            var orderedFilePaths = orderedFiles.Select(row => row.File).ToList();
            body.Children.Add(BuildLibraryGameProfileStats(folder, orderedFilePaths, metadataIndex));
            body.Children.Add(BuildLibraryGameProfileCaptureFilmstrip(win, view, orderedFilePaths));
            var achievementHost = new StackPanel { Margin = new Thickness(0, 24, 0, 0) };
            body.Children.Add(achievementHost);
            BuildLibraryGameProfileAchievementsAsync(win, achievementHost, view);
            root.Children.Add(scroll);
            win.Content = root;
            win.Show();
            win.Activate();
        }

        FrameworkElement BuildLibraryGameProfileHero(Window window, LibraryBrowserFolderView view, LibraryFolderInfo folder)
        {
            var hero = new Grid
            {
                Height = 275,
                Background = Brush("#111A21"),
                ClipToBounds = true
            };
            var bannerPath = GetLibraryHeroBannerPathForDisplayOnly(folder);
            if (string.IsNullOrWhiteSpace(bannerPath) || !File.Exists(bannerPath))
                bannerPath = GetLibraryArtPathForDisplayOnly(folder);
            var banner = new Image
            {
                Stretch = Stretch.UniformToFill,
                Opacity = 0.58,
                Visibility = Visibility.Collapsed
            };
            hero.Children.Add(banner);
            QueueImageLoad(
                banner,
                bannerPath,
                CalculateLibraryBannerArtDecodeWidth(window, window, ResolveLibraryDpiScale()),
                loaded =>
                {
                    banner.Source = loaded;
                    banner.Visibility = Visibility.Visible;
                },
                true);
            hero.Children.Add(new Border
            {
                Background = new LinearGradientBrush(
                    new GradientStopCollection
                    {
                        new GradientStop(Color.FromArgb(238, 8, 13, 17), 0),
                        new GradientStop(Color.FromArgb(170, 8, 13, 17), 0.42),
                        new GradientStop(Color.FromArgb(238, 8, 13, 17), 1)
                    },
                    new Point(0, 0),
                    new Point(1, 1))
            });
            hero.Children.Add(new Border
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                Height = 96,
                Background = new LinearGradientBrush(Color.FromArgb(0, 11, 17, 22), Color.FromArgb(255, 11, 17, 22), new Point(0, 0), new Point(0, 1))
            });

            var content = new Grid { Margin = new Thickness(26, 22, 26, 24) };
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 0 });
            var cover = CreateAsyncImageTile(
                GetLibraryArtPathForDisplayOnly(folder),
                420,
                138,
                207,
                Stretch.UniformToFill,
                folder.Name ?? string.Empty,
                Brushes.White,
                new Thickness(0),
                new Thickness(0),
                Brush("#151F27"),
                new CornerRadius(16),
                Brush("#3E5665"),
                new Thickness(1));
            cover.Effect = new DropShadowEffect { BlurRadius = 22, ShadowDepth = 7, Direction = 270, Color = Color.FromArgb(120, 0, 0, 0), Opacity = 0.75 };
            content.Children.Add(cover);

            var copy = new StackPanel { Margin = new Thickness(24, 14, 0, 0), VerticalAlignment = VerticalAlignment.Top };
            var logoPath = GetLibraryHeroLogoPathForDisplayOnly(folder);
            if (!string.IsNullOrWhiteSpace(logoPath) && File.Exists(logoPath))
            {
                var logo = new Image
                {
                    MaxWidth = 420,
                    MaxHeight = 94,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    Visibility = Visibility.Collapsed,
                    Margin = new Thickness(0, 0, 0, 12)
                };
                QueueImageLoad(logo, logoPath, 720, loaded =>
                {
                    logo.Source = loaded;
                    logo.Visibility = Visibility.Visible;
                }, true);
                copy.Children.Add(logo);
            }
            copy.Children.Add(new TextBlock
            {
                Text = folder.Name ?? "Game Profile",
                Foreground = Brushes.White,
                FontSize = 38,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                TextTrimming = TextTrimming.CharacterEllipsis
            });
            var badges = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) };
            foreach (var label in ResolveLibraryGameProfilePlatformLabels(view, folder))
            {
                var badge = BuildLibraryBrowserDetailTitlePlatformBadge(label);
                if (badge == null) continue;
                if (badge is FrameworkElement fe) fe.Margin = new Thickness(0, 0, 8, 8);
                badges.Children.Add(badge);
            }
            if (badges.Children.Count > 0) copy.Children.Add(badges);
            var ids = BuildLibraryGameProfileIdLine(folder);
            if (!string.IsNullOrWhiteSpace(ids))
            {
                copy.Children.Add(new TextBlock
                {
                    Text = ids,
                    Foreground = Brush("#A9BAC4"),
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0)
                });
            }
            if (!string.IsNullOrWhiteSpace(folder.CollectionNotes))
            {
                copy.Children.Add(new TextBlock
                {
                    Text = folder.CollectionNotes.Trim(),
                    Foreground = Brush("#D2DFE7"),
                    FontSize = 14,
                    TextWrapping = TextWrapping.Wrap,
                    MaxHeight = 54,
                    Margin = new Thickness(0, 12, 0, 0),
                    Opacity = 0.92
                });
            }
            Grid.SetColumn(copy, 1);
            content.Children.Add(copy);
            hero.Children.Add(content);
            return hero;
        }

        IEnumerable<string> ResolveLibraryGameProfilePlatformLabels(LibraryBrowserFolderView view, LibraryFolderInfo folder)
        {
            var labels = (view == null ? Array.Empty<string>() : view.PlatformLabels ?? Array.Empty<string>())
                .Concat(new[] { folder == null ? string.Empty : folder.PlatformLabel })
                .Select(NormalizeConsoleLabel)
                .Where(label => !string.IsNullOrWhiteSpace(label))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(PlatformGroupOrder)
                .ThenBy(label => label, StringComparer.OrdinalIgnoreCase)
                .ToList();
            return labels.Count == 0 ? new[] { "Other" } : labels;
        }

        string BuildLibraryGameProfileIdLine(LibraryFolderInfo folder)
        {
            if (folder == null) return string.Empty;
            var parts = new List<string>();
            if (!string.IsNullOrWhiteSpace(folder.SteamAppId)) parts.Add("Steam " + folder.SteamAppId.Trim());
            if (!string.IsNullOrWhiteSpace(folder.SteamGridDbId)) parts.Add("STID " + folder.SteamGridDbId.Trim());
            if (!string.IsNullOrWhiteSpace(folder.RetroAchievementsGameId)) parts.Add("RA " + folder.RetroAchievementsGameId.Trim());
            if (!string.IsNullOrWhiteSpace(folder.NonSteamId)) parts.Add("Non-Steam " + folder.NonSteamId.Trim());
            return string.Join("  |  ", parts);
        }

        FrameworkElement BuildLibraryGameProfileStats(LibraryFolderInfo folder, IReadOnlyList<string> files, IReadOnlyDictionary<string, LibraryMetadataIndexEntry> metadataIndex)
        {
            var root = new UniformGrid { Columns = 4 };
            var safeFiles = files ?? Array.Empty<string>();
            var captureDates = safeFiles
                .Select(file => ResolveLibraryProfileCaptureDate(file, metadataIndex))
                .Where(date => date > DateTime.MinValue)
                .OrderBy(date => date)
                .ToList();
            var dateRange = captureDates.Count == 0
                ? "No capture dates"
                : FormatLibraryGameProfileDate(captureDates.First()) + " - " + FormatLibraryGameProfileDate(captureDates.Last());
            var starred = safeFiles.Count(file =>
            {
                LibraryMetadataIndexEntry entry;
                return metadataIndex != null && metadataIndex.TryGetValue(file, out entry) && entry != null && entry.Starred;
            });
            root.Children.Add(BuildLibraryGameProfileStatCard("Captures", safeFiles.Count.ToString(CultureInfo.CurrentCulture)));
            root.Children.Add(BuildLibraryGameProfileStatCard("Date range", dateRange));
            root.Children.Add(BuildLibraryGameProfileStatCard("Videos", safeFiles.Count(IsVideo).ToString(CultureInfo.CurrentCulture)));
            root.Children.Add(BuildLibraryGameProfileStatCard("Starred", starred.ToString(CultureInfo.CurrentCulture)));
            return root;
        }

        FrameworkElement BuildLibraryGameProfileStatCard(string label, string value)
        {
            var card = new Border
            {
                Margin = new Thickness(0, 0, 12, 0),
                Padding = new Thickness(16, 14, 16, 12),
                CornerRadius = new CornerRadius(16),
                Background = Brush("#111A21"),
                BorderBrush = Brush("#263640"),
                BorderThickness = new Thickness(1),
                MinHeight = 86
            };
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = label,
                Foreground = Brush("#86A0AE"),
                FontSize = 12,
                FontWeight = FontWeights.SemiBold
            });
            stack.Children.Add(new TextBlock
            {
                Text = value,
                Foreground = Brushes.White,
                FontSize = value != null && value.Length > 18 ? 18 : 26,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            });
            card.Child = stack;
            return card;
        }

        void BuildLibraryGameProfileAchievementsAsync(Window owner, StackPanel host, LibraryBrowserFolderView view)
        {
            host.Children.Add(BuildLibraryGameProfileSectionTitle("Achievements", "Collected achievements are grouped first; hover any badge for details."));
            var loading = new TextBlock
            {
                Text = "Loading achievements...",
                Foreground = Brush("#8FA4B0"),
                Margin = new Thickness(0, 10, 0, 0),
                FontSize = 13
            };
            host.Children.Add(loading);
            var folder = ResolveLibraryBrowserAchievementLookupFolder(view);
            var platform = NormalizeConsoleLabel(folder == null ? string.Empty : folder.PlatformLabel);
            var steamLookupFromNonSteamEntry = IsLibraryBrowserSteamAchievementLookupFromNonSteamEntry(view, folder);
            var userAgent = "PixelVault/" + AppVersion;
            _ = Task.Run(async () =>
            {
                GameAchievementsFetchService.FetchResult result;
                try
                {
                    result = await GameAchievementsFetchService.FetchAsync(
                        platform,
                        folder,
                        CurrentSteamWebApiKey(),
                        CurrentRetroAchievementsApiKey(),
                        CurrentSteamUserId64(),
                        CurrentRetroAchievementsUsername(),
                        userAgent,
                        default).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    result = new GameAchievementsFetchService.FetchResult { ErrorMessage = ex.Message };
                }

                await owner.Dispatcher.InvokeAsync(() =>
                {
                    host.Children.Remove(loading);
                    if (result == null || result.IsError)
                    {
                        host.Children.Add(BuildLibraryGameProfileEmptyCard(result == null ? "Achievements are not available for this game yet." : result.ErrorMessage));
                        return;
                    }

                    var rows = result.Rows ?? new List<GameAchievementsFetchService.AchievementRow>();
                    var progressKnown = rows.Any(row => row != null && row.ProgressKnown);
                    var earned = rows.Where(row => row != null && row.ProgressKnown && row.Unlocked).ToList();
                    var hideSteamDefinitionsForEmptyNonSteamEntry =
                        steamLookupFromNonSteamEntry
                        && string.Equals(result.SourceLabel ?? string.Empty, "Steam", StringComparison.OrdinalIgnoreCase)
                        && progressKnown
                        && earned.Count == 0;
                    var displayRows = rows
                        .Where(row => row != null)
                        .OrderBy(row => progressKnown && row.Unlocked ? 0 : 1)
                        .ThenBy(row => row.SortKey)
                        .ThenBy(row => row.Title ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    var summary = hideSteamDefinitionsForEmptyNonSteamEntry
                        ? "No Steam achievements have been obtained for this non-Steam entry"
                        : (progressKnown
                            ? earned.Count + " of " + rows.Count + " earned"
                            : "Progress unknown - showing achievement definitions");
                    host.Children.Add(new TextBlock
                    {
                        Text = (result.SourceLabel ?? "Achievements") + ": " + summary,
                        Foreground = Brush("#B8C9D4"),
                        FontSize = 13,
                        FontWeight = FontWeights.SemiBold,
                        Margin = new Thickness(0, 8, 0, 10)
                    });
                    if (hideSteamDefinitionsForEmptyNonSteamEntry)
                        return;

                    if (displayRows.Count == 0)
                    {
                        host.Children.Add(BuildLibraryGameProfileEmptyCard(progressKnown ? "No achievements earned yet." : "No achievement definitions were returned."));
                        return;
                    }

                    var container = new Border
                    {
                        Padding = new Thickness(12),
                        CornerRadius = new CornerRadius(16),
                        Background = Brush("#101820"),
                        BorderBrush = Brush("#263640"),
                        BorderThickness = new Thickness(1)
                    };
                    var grid = new WrapPanel { Orientation = Orientation.Horizontal };
                    foreach (var row in displayRows)
                        grid.Children.Add(BuildLibraryGameProfileAchievementCard(row, userAgent, progressKnown));
                    container.Child = grid;
                    host.Children.Add(container);
                }, DispatcherPriority.Background);
            });
        }

        FrameworkElement BuildLibraryGameProfileAchievementCard(GameAchievementsFetchService.AchievementRow row, string userAgent, bool progressKnown)
        {
            var unlocked = row != null && row.ProgressKnown && row.Unlocked;
            var muted = progressKnown && !unlocked;
            var card = new Border
            {
                Width = 48,
                Height = 48,
                Margin = new Thickness(0, 0, 8, 8),
                Padding = new Thickness(3),
                CornerRadius = new CornerRadius(10),
                Background = Brush(unlocked ? "#192316" : "#111A21"),
                BorderBrush = Brush(unlocked ? "#C7A245" : "#31414C"),
                BorderThickness = new Thickness(unlocked ? 1.25 : 1),
                Opacity = muted ? 0.46 : 1,
                SnapsToDevicePixels = true
            };
            var iconHost = new Border
            {
                CornerRadius = new CornerRadius(7),
                ClipToBounds = true,
                Background = Brush("#18242B"),
                BorderBrush = Brush("#30404C"),
                BorderThickness = new Thickness(1)
            };
            var img = new Image { Stretch = Stretch.UniformToFill };
            iconHost.Child = img;
            AchievementsInfoWindow.StartAchievementBadgeDownload(img, row, userAgent);
            card.Child = iconHost;
            card.ToolTip = BuildLibraryGameProfileAchievementToolTip(row, progressKnown);
            ToolTipService.SetShowDuration(card, 90000);
            return card;
        }

        FrameworkElement BuildLibraryGameProfileAchievementToolTip(GameAchievementsFetchService.AchievementRow row, bool progressKnown)
        {
            var stack = new StackPanel { MaxWidth = 320 };
            stack.Children.Add(new TextBlock
            {
                Text = row == null ? "Achievement" : row.Title ?? "Achievement",
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });
            if (!string.IsNullOrWhiteSpace(row == null ? string.Empty : row.Description))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = row.Description,
                    Foreground = Brush("#B8C9D4"),
                    FontSize = 12,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 6, 0, 0)
                });
            }
            var earnedMeta = row == null ? string.Empty : AchievementsInfoWindow.FormatAchievementEarnedMeta(row.UnlockUtcTicks);
            var status = progressKnown
                ? ((row != null && row.Unlocked) ? (string.IsNullOrWhiteSpace(earnedMeta) ? "Collected" : earnedMeta) : "Not collected")
                : "Progress not tracked";
            stack.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(row == null ? string.Empty : row.Meta)
                    ? status
                    : row.Meta + " | " + status,
                Foreground = row != null && row.Unlocked ? Brush("#E7C66B") : Brush("#8FA4B0"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 8, 0, 0)
            });
            return stack;
        }

        FrameworkElement BuildLibraryGameProfileCaptureFilmstrip(Window profileWindow, LibraryBrowserFolderView view, IReadOnlyList<string> orderedFiles)
        {
            var section = new StackPanel { Margin = new Thickness(0, 16, 0, 0) };
            var files = (orderedFiles ?? Array.Empty<string>())
                .Where(file => !string.IsNullOrWhiteSpace(file) && File.Exists(file))
                .ToList();
            if (files.Count == 0)
            {
                section.Children.Add(BuildLibraryGameProfileEmptyCard("No captures are currently linked to this profile yet."));
                return section;
            }

            var shown = files.Take(14).ToList();
            var tempWs = new LibraryBrowserWorkingSet();
            tempWs.DetailFilesDisplayOrder.AddRange(files.Where(IsImage));
            var rail = new StackPanel { Orientation = Orientation.Horizontal };
            foreach (var file in shown)
                rail.Children.Add(BuildLibraryGameProfileCaptureTile(profileWindow, tempWs, file, 132, 82));
            rail.Children.Add(BuildLibraryGameProfileOpenPhotoViewTile(profileWindow, view, files.Count));

            var scroller = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Padding = new Thickness(0, 0, 0, 6),
                Content = rail
            };
            section.Children.Add(scroller);
            return section;
        }

        FrameworkElement BuildLibraryGameProfileCaptureTile(Window profileWindow, LibraryBrowserWorkingSet tempWs, string file, double width, double height)
        {
            var tile = new Border
            {
                Width = width,
                Height = height,
                Margin = new Thickness(0, 0, 10, 10),
                CornerRadius = new CornerRadius(12),
                ClipToBounds = true,
                Background = Brush("#111A21"),
                BorderBrush = Brush("#263640"),
                BorderThickness = new Thickness(1),
                Cursor = Cursors.Hand
            };
            var grid = new Grid();
            if (IsImage(file))
            {
                grid.Children.Add(CreateAsyncImageTile(
                    file,
                    CalculateLibraryDetailTileDecodeWidth((int)width, ResolveLibraryDpiScale()),
                    width,
                    height,
                    Stretch.UniformToFill,
                    Path.GetFileName(file),
                    Brushes.White,
                    new Thickness(0),
                    new Thickness(0),
                    Brushes.Transparent,
                    new CornerRadius(0),
                    Brushes.Transparent,
                    new Thickness(0)));
                tile.ToolTip = Path.GetFileName(file);
                ToolTipService.SetShowDuration(tile, 90000);
                tile.MouseLeftButtonDown += delegate
                {
                    OpenLibraryCaptureViewer(profileWindow, tempWs, file);
                };
            }
            else
            {
                grid.Children.Add(new TextBlock
                {
                    Text = "CLIP",
                    Foreground = Brushes.White,
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 18,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                });
                tile.MouseLeftButtonDown += delegate
                {
                    try
                    {
                        Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
                    }
                    catch (Exception ex)
                    {
                        Log("Open video failed: " + ex.Message);
                    }
                };
            }
            tile.Child = grid;
            return tile;
        }

        FrameworkElement BuildLibraryGameProfileOpenPhotoViewTile(Window profileWindow, LibraryBrowserFolderView view, int captureCount)
        {
            var tile = new Border
            {
                Width = 132,
                Height = 82,
                Margin = new Thickness(0, 0, 10, 10),
                CornerRadius = new CornerRadius(12),
                Background = Brush("#15242D"),
                BorderBrush = Brush("#4A9FE8"),
                BorderThickness = new Thickness(1.25),
                Cursor = Cursors.Hand
            };
            var stack = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8)
            };
            stack.Children.Add(new TextBlock
            {
                Text = "Open Photo View",
                Foreground = Brushes.White,
                FontWeight = FontWeights.SemiBold,
                FontSize = 12.5,
                TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            });
            stack.Children.Add(new TextBlock
            {
                Text = captureCount.ToString(CultureInfo.CurrentCulture) + " captures",
                Foreground = Brush("#9DC7E8"),
                FontSize = 11,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 4, 0, 0)
            });
            tile.Child = stack;
            tile.MouseLeftButtonDown += delegate(object _, MouseButtonEventArgs e)
            {
                if (e.ChangedButton != MouseButton.Left) return;
                e.Handled = true;
                OpenLibraryGameProfilePhotoWorkspace(profileWindow, view);
            };
            return tile;
        }

        void OpenLibraryGameProfilePhotoWorkspace(Window profileWindow, LibraryBrowserFolderView view)
        {
            if (view == null) return;
            if (_libraryBrowserLiveWorkingSet == null || _libraryBrowserLiveWorkingSet.Panes == null)
                ShowLibraryBrowser(true);

            var ws = _libraryBrowserLiveWorkingSet;
            if (ws == null || ws.OpenPhotoWorkspaceForFolder == null) return;
            if (IsLibraryBrowserTimelineMode())
            {
                libraryGroupingMode = "all";
                SaveSettings();
                LibraryBrowserSyncWorkspaceModeWithGrouping(ws, libraryGroupingMode);
                ws.RerenderFolderList?.Invoke();
            }

            var target = ws.ViewFolders.FirstOrDefault(candidate => SameLibraryBrowserSelection(candidate, view)) ?? view;
            ws.OpenPhotoWorkspaceForFolder(target);
            (profileWindow == null ? this : profileWindow.Owner ?? this).Activate();
        }

        FrameworkElement BuildLibraryGameProfileSectionTitle(string title, string subtitle)
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock
            {
                Text = title,
                Foreground = Brushes.White,
                FontSize = 22,
                FontWeight = FontWeights.SemiBold
            });
            if (!string.IsNullOrWhiteSpace(subtitle))
            {
                stack.Children.Add(new TextBlock
                {
                    Text = subtitle,
                    Foreground = Brush("#8FA4B0"),
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 4, 0, 0)
                });
            }
            return stack;
        }

        FrameworkElement BuildLibraryGameProfileEmptyCard(string message)
        {
            return new Border
            {
                Margin = new Thickness(0, 10, 0, 0),
                Padding = new Thickness(16),
                CornerRadius = new CornerRadius(14),
                Background = Brush("#101820"),
                BorderBrush = Brush("#263640"),
                BorderThickness = new Thickness(1),
                Child = new TextBlock
                {
                    Text = string.IsNullOrWhiteSpace(message) ? "Nothing to show yet." : message,
                    Foreground = Brush("#9FB1BC"),
                    FontSize = 13,
                    TextWrapping = TextWrapping.Wrap
                }
            };
        }

        DateTime ResolveLibraryProfileCaptureDate(string file, IReadOnlyDictionary<string, LibraryMetadataIndexEntry> metadataIndex)
        {
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) return DateTime.MinValue;
            var dict = metadataIndex as Dictionary<string, LibraryMetadataIndexEntry>;
            if (librarySession != null && librarySession.HasLibraryRoot)
                return librarySession.ResolveIndexedLibraryDate(file, dict);
            return GetLibraryDate(file);
        }

        static string FormatLibraryGameProfileDate(DateTime date)
        {
            if (date <= DateTime.MinValue) return string.Empty;
            return date.ToString("MMM d, yyyy", CultureInfo.CurrentCulture);
        }
    }
}
