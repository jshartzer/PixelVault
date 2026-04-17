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
            var folder = BuildLibraryBrowserDisplayFolder(view);
            var normalized = NormalizeConsoleLabel(folder == null ? string.Empty : folder.PlatformLabel);
            AchievementsInfoWindow.ShowModal(
                owner,
                normalized,
                folder,
                CurrentSteamWebApiKey(),
                CurrentRetroAchievementsApiKey(),
                CurrentSteamUserId64(),
                CurrentRetroAchievementsUsername(),
                "PixelVault/" + AppVersion);
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

            var folder = BuildLibraryBrowserDisplayFolder(info);
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
