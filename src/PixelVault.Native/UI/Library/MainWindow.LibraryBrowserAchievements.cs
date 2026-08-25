using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        internal void LibraryBrowserShowAchievementsInfo(Window owner, LibraryBrowserFolderView view)
        {
            if (owner == null || view == null) return;
            var folder = ResolveLibraryBrowserAchievementLookupFolder(view);
            var normalized = NormalizeConsoleLabel(folder == null ? string.Empty : folder.PlatformLabel);
            AchievementsInfoWindow.ShowModal(
                owner,
                normalized,
                folder,
                CurrentSteamWebApiKey(),
                CurrentRetroAchievementsApiKey(),
                CurrentSteamUserId64(),
                CurrentRetroAchievementsUsername(),
                "PixelVault/" + AppVersion,
                achievementGuideService);
        }

        LibraryFolderInfo ResolveLibraryBrowserAchievementLookupFolder(LibraryBrowserFolderView view)
        {
            var displayFolder = BuildLibraryBrowserDisplayFolder(view);
            if (view == null) return displayFolder;

            var candidates = new List<LibraryFolderInfo>();
            foreach (var folder in view.SourceFolders ?? Enumerable.Empty<LibraryFolderInfo>())
            {
                if (folder != null) candidates.Add(folder);
            }
            if (view.PrimaryFolder != null) candidates.Add(view.PrimaryFolder);
            if (displayFolder != null) candidates.Add(displayFolder);

            LibraryFolderInfo PickByPlatformAndId(string platform, Func<LibraryFolderInfo, string> pickId)
            {
                return candidates.FirstOrDefault(folder =>
                    folder != null
                    && string.Equals(NormalizeConsoleLabel(folder.PlatformLabel), platform, StringComparison.OrdinalIgnoreCase)
                    && HasLibraryBrowserAchievementId(pickId(folder)));
            }

            var steam = PickByPlatformAndId("Steam", folder => folder.SteamAppId);
            if (steam != null) return BuildLibraryBrowserAchievementLookupFolder(steam, displayFolder, "Steam");

            var retro = PickByPlatformAndId("Emulation", folder => folder.RetroAchievementsGameId);
            if (retro != null) return BuildLibraryBrowserAchievementLookupFolder(retro, displayFolder, "Emulation");

            var steamById = candidates
                .Where(folder => folder != null && HasLibraryBrowserAchievementId(folder.SteamAppId))
                .GroupBy(folder => CleanTag(folder.SteamAppId ?? string.Empty), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            if (steamById.Count == 1) return BuildLibraryBrowserAchievementLookupFolder(steamById[0], displayFolder, "Steam");

            var retroById = candidates
                .Where(folder => folder != null && HasLibraryBrowserAchievementId(folder.RetroAchievementsGameId))
                .GroupBy(folder => CleanTag(folder.RetroAchievementsGameId ?? string.Empty), StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .ToList();
            if (retroById.Count == 1) return BuildLibraryBrowserAchievementLookupFolder(retroById[0], displayFolder, "Emulation");

            return displayFolder;
        }

        LibraryFolderInfo BuildLibraryBrowserAchievementLookupFolder(LibraryFolderInfo source, LibraryFolderInfo displayFolder, string platform)
        {
            var folder = CloneLibraryFolderInfo(source) ?? CloneLibraryFolderInfo(displayFolder) ?? new LibraryFolderInfo();
            if (displayFolder != null)
            {
                folder.Name = displayFolder.Name ?? folder.Name;
                folder.FileCount = displayFolder.FileCount;
                folder.FilePaths = displayFolder.FilePaths == null ? folder.FilePaths : displayFolder.FilePaths.ToArray();
                folder.PreviewImagePath = string.IsNullOrWhiteSpace(displayFolder.PreviewImagePath) ? folder.PreviewImagePath : displayFolder.PreviewImagePath;
            }
            folder.PlatformLabel = platform ?? string.Empty;
            if (string.Equals(platform, "Steam", StringComparison.OrdinalIgnoreCase))
                folder.RetroAchievementsGameId = string.Empty;
            else if (string.Equals(platform, "Emulation", StringComparison.OrdinalIgnoreCase))
                folder.SteamAppId = string.Empty;
            return folder;
        }

        static bool HasLibraryBrowserAchievementId(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return false;
            return raw.Any(char.IsDigit);
        }

        internal void LibraryBrowserClearAchievementsSummary(LibraryBrowserPaneRefs panes)
        {
            var ws = _libraryBrowserLiveWorkingSet;
            if (ws != null) Interlocked.Increment(ref ws.AchievementsSummaryFetchGeneration);
            if (panes?.PhotoAchievementsSummary == null) return;
            panes.PhotoAchievementsSummary.Text = string.Empty;
            panes.PhotoAchievementsSummary.Visibility = Visibility.Collapsed;
            if (panes.PhotoAchievementsRecentPanel != null)
            {
                panes.PhotoAchievementsRecentPanel.Children.Clear();
                panes.PhotoAchievementsRecentPanel.Visibility = Visibility.Collapsed;
            }
        }

        internal static List<GameAchievementsFetchService.AchievementRow> BuildRecentAchievementsPreviewRows(
            IEnumerable<GameAchievementsFetchService.AchievementRow> rows,
            int maxCount = 5)
        {
            return (rows ?? Enumerable.Empty<GameAchievementsFetchService.AchievementRow>())
                .Where(row => row != null && row.ProgressKnown && row.Unlocked)
                .OrderByDescending(row => row.UnlockUtcTicks > 0)
                .ThenByDescending(row => row.UnlockUtcTicks)
                .ThenBy(row => row.Title ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(0, maxCount))
                .ToList();
        }

        FrameworkElement BuildLibraryRecentAchievementHoverCard(GameAchievementsFetchService.AchievementRow row, string userAgent)
        {
            var card = new Border
            {
                Background = Brush("#10171D"),
                BorderBrush = Brush("#273540"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12),
                MaxWidth = 340
            };
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 0 });

            var badgeHost = new Border
            {
                Width = 56,
                Height = 56,
                Margin = new Thickness(0, 0, 12, 0),
                CornerRadius = new CornerRadius(8),
                ClipToBounds = true,
                Background = Brush("#151F27"),
                BorderBrush = Brush("#30404C"),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Top
            };
            var badgeImage = new Image
            {
                Stretch = Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            badgeHost.Child = badgeImage;
            AchievementsInfoWindow.StartAchievementBadgeDownload(badgeImage, row, userAgent);
            Grid.SetColumn(badgeHost, 0);
            grid.Children.Add(badgeHost);

            var copy = new StackPanel { MinWidth = 0 };
            copy.Children.Add(new TextBlock
            {
                Text = row?.Title ?? string.Empty,
                Foreground = Brushes.White,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap
            });
            if (!string.IsNullOrWhiteSpace(row?.Description))
            {
                copy.Children.Add(new TextBlock
                {
                    Text = row.Description,
                    Margin = new Thickness(0, 4, 0, 0),
                    Foreground = Brush("#A7BAC5"),
                    FontSize = 11.5,
                    TextWrapping = TextWrapping.Wrap
                });
            }
            var earnedMeta = AchievementsInfoWindow.FormatAchievementEarnedMeta(row == null ? 0 : row.UnlockUtcTicks);
            if (!string.IsNullOrWhiteSpace(earnedMeta))
            {
                copy.Children.Add(new TextBlock
                {
                    Text = earnedMeta,
                    Margin = new Thickness(0, 6, 0, 0),
                    Foreground = Brush("#88B8A0"),
                    FontSize = 11.5,
                    FontWeight = FontWeights.SemiBold,
                    TextWrapping = TextWrapping.Wrap
                });
            }
            if (!string.IsNullOrWhiteSpace(row?.Meta))
            {
                copy.Children.Add(new TextBlock
                {
                    Text = row.Meta,
                    Margin = new Thickness(0, 4, 0, 0),
                    Foreground = Brush("#88A1AF"),
                    FontSize = 11,
                    TextWrapping = TextWrapping.Wrap
                });
            }
            Grid.SetColumn(copy, 1);
            grid.Children.Add(copy);

            card.Child = grid;
            return card;
        }

        FrameworkElement BuildLibraryRecentAchievementBadge(GameAchievementsFetchService.AchievementRow row, string userAgent)
        {
            var badgeHost = new Border
            {
                Width = 28,
                Height = 28,
                Margin = new Thickness(0, 0, 6, 0),
                CornerRadius = new CornerRadius(6),
                ClipToBounds = true,
                Background = Brush("#151F27"),
                BorderBrush = Brush("#30404C"),
                BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Center
            };
            var badgeImage = new Image
            {
                Stretch = Stretch.UniformToFill,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            badgeHost.Child = badgeImage;
            AchievementsInfoWindow.StartAchievementBadgeDownload(badgeImage, row, userAgent);
            badgeHost.ToolTip = BuildLibraryRecentAchievementHoverCard(row, userAgent);
            ToolTipService.SetShowDuration(badgeHost, 120000);
            return badgeHost;
        }

        /// <summary>Photo workspace: prefetch counts for the label next to the achievements button.</summary>
        internal void LibraryBrowserScheduleAchievementsSummaryRefresh(
            LibraryBrowserWorkingSet ws,
            LibraryBrowserPaneRefs panes,
            Window libraryWindow,
            LibraryBrowserFolderView info)
        {
            if (ws == null || panes == null || libraryWindow == null || info == null || panes.PhotoAchievementsSummary == null)
                return;

            var gen = Interlocked.Increment(ref ws.AchievementsSummaryFetchGeneration);
            panes.PhotoAchievementsSummary.Text = "Loading…";
            panes.PhotoAchievementsSummary.Visibility = Visibility.Visible;
            if (panes.PhotoAchievementsRecentPanel != null)
            {
                panes.PhotoAchievementsRecentPanel.Children.Clear();
                panes.PhotoAchievementsRecentPanel.Visibility = Visibility.Collapsed;
            }

            var folder = ResolveLibraryBrowserAchievementLookupFolder(info);
            var normalized = NormalizeConsoleLabel(folder == null ? string.Empty : folder.PlatformLabel);
            var captureInfo = info;
            var userAgent = "PixelVault/" + AppVersion;

            _ = Task.Run(async () =>
            {
                GameAchievementsFetchService.FetchResult result;
                try
                {
                    result = await GameAchievementsFetchService.FetchAsync(
                        normalized,
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

                await libraryWindow.Dispatcher.InvokeAsync(() =>
                {
                    if (gen != Volatile.Read(ref ws.AchievementsSummaryFetchGeneration)) return;
                    if (!SameLibraryBrowserSelection(ws.Current, captureInfo)) return;
                    if (result.IsError || result.Rows == null || result.Rows.Count == 0)
                    {
                        panes.PhotoAchievementsSummary.Text = string.Empty;
                        panes.PhotoAchievementsSummary.Visibility = Visibility.Collapsed;
                        if (panes.PhotoAchievementsRecentPanel != null)
                        {
                            panes.PhotoAchievementsRecentPanel.Children.Clear();
                            panes.PhotoAchievementsRecentPanel.Visibility = Visibility.Collapsed;
                        }
                        return;
                    }
                    var rows = result.Rows;
                    var total = rows.Count;
                    var earned = rows.Count(r => r.ProgressKnown && r.Unlocked);
                    panes.PhotoAchievementsSummary.Text = earned + " of " + total + " Achievements earned";
                    panes.PhotoAchievementsSummary.Visibility = Visibility.Visible;
                    if (panes.PhotoAchievementsRecentPanel != null)
                    {
                        panes.PhotoAchievementsRecentPanel.Children.Clear();
                        var recentRows = BuildRecentAchievementsPreviewRows(rows, 5);
                        foreach (var row in recentRows)
                            panes.PhotoAchievementsRecentPanel.Children.Add(BuildLibraryRecentAchievementBadge(row, userAgent));
                        panes.PhotoAchievementsRecentPanel.Visibility = recentRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                    }
                }, DispatcherPriority.Background);
            });
        }
    }
}
