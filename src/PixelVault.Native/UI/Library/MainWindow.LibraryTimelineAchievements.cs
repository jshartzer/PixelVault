using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PixelVaultNative
{
    internal sealed class LibraryTimelineAchievement
    {
        // A layout identity, never a capture path. Hex preserves case-sensitive provider IDs in path-keyed layout maps.
        internal string Key => "achievement:" + Convert.ToHexString(Encoding.UTF8.GetBytes(
            Game.Provider + "\n" + Game.Id + "\n" + Row.ProviderAchievementId));
        internal AchievementHubGame Game;
        internal GameAchievementsFetchService.AchievementRow Row;
        internal List<GameAchievementsFetchService.AchievementRow> GameRows;
        internal string Account;
        internal long SavedUtc;
        internal DateTime UnlockLocal;

        internal static List<LibraryTimelineAchievement> Select(IEnumerable<AchievementHubGame> games,
            IReadOnlyDictionary<string, AchievementHubStore.AccountData> accounts, DateTime start, DateTime end,
            long nowUtcTicks, TimeZoneInfo zone)
        {
            var result = new List<LibraryTimelineAchievement>();
            foreach (var game in games.GroupBy(g => g.Key, StringComparer.Ordinal).Select(g => g.First()))
            {
                if (!accounts.TryGetValue(game.Provider, out var account)
                    || !account.Games.TryGetValue(game.Id, out var snapshot) || snapshot?.Rows == null) continue;
                foreach (var row in snapshot.Rows.Where(r => r?.HasStableProviderIdentity == true
                    && r.Provider == game.Provider && r.ProviderGameId == game.Id)
                    .GroupBy(r => r.ProviderAchievementId, StringComparer.Ordinal).Select(g => g.First()))
                {
                    if (!AchievementCollectionStats.ValidUnlock(row, nowUtcTicks)) continue;
                    var local = TimeZoneInfo.ConvertTimeFromUtc(new DateTime(row.UnlockUtcTicks, DateTimeKind.Utc), zone);
                    if (!LibraryBrowserViewModelMath.LibraryTimelineRangeContainsCapture(local, start, end)) continue;
                    result.Add(new LibraryTimelineAchievement { Game = game, Row = row, GameRows = snapshot.Rows,
                        Account = account.Account, SavedUtc = snapshot.UpdatedUtc, UnlockLocal = local });
                }
            }
            return result.OrderByDescending(e => e.Row.UnlockUtcTicks).ThenBy(e => e.Key, StringComparer.Ordinal).ToList();
        }
    }

    /// <summary>Mixed layout keys are isolated from the capture snapshot, file selection, metadata, and viewer lists.</summary>
    internal sealed class LibraryTimelineMosaicProjection
    {
        internal List<LibraryDetailRenderGroup> Groups;
        internal Dictionary<string, LibraryDetailMediaLayoutInfo> Media;
        internal Dictionary<string, LibraryTimelineAchievement> Achievements;

        internal static bool CaptureTimesChanged(LibraryDetailRenderSnapshot before, LibraryDetailRenderSnapshot after)
        {
            if (before.Achievements.Count == 0 && after.Achievements.Count == 0) return false;
            return before.TimelineContextByFile.Count != after.TimelineContextByFile.Count
                || after.TimelineContextByFile.Any(pair => !before.TimelineContextByFile.TryGetValue(pair.Key, out var prior)
                    || prior?.CaptureDate != pair.Value?.CaptureDate);
        }

        internal static LibraryTimelineMosaicProjection Build(LibraryDetailRenderSnapshot snapshot)
        {
            var result = new LibraryTimelineMosaicProjection
            {
                Media = new Dictionary<string, LibraryDetailMediaLayoutInfo>(snapshot.MediaLayoutByFile, StringComparer.OrdinalIgnoreCase),
                Achievements = snapshot.Achievements.GroupBy(e => e.Key, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase)
            };
            var items = new List<(string Key, DateTime Date)>();
            foreach (var group in snapshot.Groups)
                foreach (var file in group.Files)
                {
                    var date = snapshot.TimelineContextByFile.TryGetValue(file, out var context) ? context.CaptureDate : group.CaptureDate;
                    items.Add((file, date));
                }
            foreach (var pair in result.Achievements)
            {
                items.Add((pair.Key, pair.Value.UnlockLocal));
                result.Media[pair.Key] = new LibraryDetailMediaLayoutInfo { PixelWidth = 1, PixelHeight = 1, UseSingleSquareCell = true };
            }
            result.Groups = items.GroupBy(i => i.Key, StringComparer.OrdinalIgnoreCase).Select(g => g.First())
                .OrderByDescending(i => i.Date).ThenBy(i => i.Key, StringComparer.OrdinalIgnoreCase)
                .GroupBy(i => i.Date.Date).Select(g => new LibraryDetailRenderGroup { CaptureDate = g.Key, Files = g.Select(i => i.Key).ToList() }).ToList();
            return result;
        }
    }

    public sealed partial class MainWindow
    {
        internal static string BuildTimelineActivitySummary(LibraryDetailRenderSnapshot snapshot)
        {
            var contexts = snapshot.TimelineContextByFile.Values.Where(c => c != null).ToList();
            var events = snapshot.Achievements;
            var games = contexts.Select(c => c.GameTitle).Concat(events.Select(e => e.Game.Title))
                .Where(s => !string.IsNullOrWhiteSpace(s)).Distinct(StringComparer.OrdinalIgnoreCase).Count();
            var platforms = contexts.Select(c => c.PlatformLabel).Concat(events.Select(e => e.Game.Folder?.PlatformLabel))
                .Where(s => !string.IsNullOrWhiteSpace(s) && !string.Equals(s, "Other", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase).Count();
            var dates = contexts.Select(c => c.CaptureDate).Concat(events.Select(e => e.UnlockLocal)).Where(d => d > DateTime.MinValue).ToList();
            var photos = snapshot.VisibleFiles.Count(IsImage);
            var clips = snapshot.VisibleFiles.Count(IsVideo);
            var summary = BuildLibraryTimelineSummaryText(photos, games, platforms,
                dates.Count == 0 ? DateTime.MinValue : dates.Max(), dates.Count == 0 ? DateTime.MinValue : dates.Min());
            var split = summary.IndexOf(" | ", StringComparison.Ordinal);
            var counts = photos + (photos == 1 ? " photo" : " photos");
            if (clips > 0) counts += " | " + clips + (clips == 1 ? " clip" : " clips");
            if (events.Count > 0) counts += " | " + events.Count + (events.Count == 1 ? " achievement" : " achievements");
            return split >= 0 ? counts + summary.Substring(split) : counts;
        }

        readonly object timelineAchievementCacheGate = new object();
        string timelineAchievementCacheKey;
        DateTime timelineAchievementCacheUpdated;
        List<LibraryTimelineAchievement> timelineAchievementCache;
        System.Windows.Threading.DispatcherTimer timelineAchievementRefreshTimer;

        void QueueTimelineAchievementRefresh()
        {
            var current = _libraryBrowserLiveWorkingSet;
            if (current?.IsActive != true || !current.ShowTimelineAchievements || !IsLibraryBrowserTimelineView(current.Current)) return;
            if (timelineAchievementRefreshTimer == null)
            {
                timelineAchievementRefreshTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
                timelineAchievementRefreshTimer.Tick += (_, __) =>
                {
                    timelineAchievementRefreshTimer.Stop();
                    var ws = _libraryBrowserLiveWorkingSet;
                    if (ws?.IsActive != true || !ws.ShowTimelineAchievements || !IsLibraryBrowserTimelineView(ws.Current)) return;
                    ws.PreserveDetailScrollOnNextRender = true;
                    ws.PreservedDetailScrollOffset = ws.Panes?.ThumbScroll?.VerticalOffset ?? 0;
                    ws.RefreshDetailPaneForPhotoFilters?.Invoke();
                };
                Closed += (_, __) => timelineAchievementRefreshTimer.Stop();
            }
            timelineAchievementRefreshTimer.Stop();
            timelineAchievementRefreshTimer.Start();
        }

        internal static List<AchievementHubGame> BuildTimelineAchievementGames(IEnumerable<LibraryFolderInfo> folders)
        {
            var games = new List<AchievementHubGame>();
            foreach (var folder in folders.Where(f => f != null))
            {
                if (!GameAchievementsFetchService.TryPickAchievementsBackend(NormalizeConsoleLabel(folder.PlatformLabel), folder, out var steam)) continue;
                var id = steam ? folder.SteamAppId : folder.RetroAchievementsGameId;
                if (!int.TryParse(id, out var numeric) || numeric <= 0) continue;
                games.Add(new AchievementHubGame { Provider = steam ? "steam" : "retroachievements", Id = numeric.ToString(CultureInfo.InvariantCulture),
                    Title = folder.Name, Folder = folder });
            }
            return games.GroupBy(g => g.Key, StringComparer.Ordinal).Select(g => g.First()).OrderBy(g => g.Key, StringComparer.Ordinal).ToList();
        }

        async Task<List<LibraryTimelineAchievement>> LoadTimelineAchievementsAsync(List<AchievementHubGame> games,
            string steamAccount, string retroAccount, DateTime start, DateTime end, CancellationToken token)
        {
            if (_achievementHubStore == null || games.Count == 0) return new();
            var revision = _achievementHubStore.Revision;
            var key = string.Join("\n", libraryRoot, steamAccount, retroAccount, revision, start.Ticks, end.Ticks,
                string.Join("\u001f", games.Select(g => g.Key + "|" + g.Title + "|" + g.Folder?.PlatformLabel)));
            lock (timelineAchievementCacheGate)
                if (timelineAchievementCacheKey == key && DateTime.UtcNow - timelineAchievementCacheUpdated < TimeSpan.FromMinutes(1))
                    return timelineAchievementCache;
            var accounts = new Dictionary<string, AchievementHubStore.AccountData>(StringComparer.Ordinal);
            foreach (var provider in games.Select(g => g.Provider).Distinct())
            {
                token.ThrowIfCancellationRequested();
                var account = provider == "steam" ? steamAccount : retroAccount;
                if (string.IsNullOrWhiteSpace(account)) continue;
                try { accounts[provider] = await _achievementHubStore.LoadAsync(provider, account, token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception ex) { Log("Timeline achievement progress could not be loaded: " + ex.GetType().Name); }
            }
            token.ThrowIfCancellationRequested();
            var entries = LibraryTimelineAchievement.Select(games, accounts, start, end, DateTime.UtcNow.Ticks, TimeZoneInfo.Local);
            lock (timelineAchievementCacheGate)
            {
                timelineAchievementCacheKey = key;
                timelineAchievementCacheUpdated = DateTime.UtcNow;
                timelineAchievementCache = entries;
            }
            return entries;
        }

        internal static Border BuildLibraryTimelineAchievementTile(LibraryTimelineAchievement entry, int size, int decodeWidth,
            string dayLabel, Action open)
        {
            var row = entry.Row;
            var image = new Image { Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
            var grid = new Grid(); grid.Children.Add(image);
            AchievementsInfoWindow.StartAchievementBadgeDownload(image, row, "PixelVault/Timeline", decodeWidth);
            grid.Children.Add(new Border { HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                Background = SystemParameters.HighContrast ? SystemColors.WindowBrush : UiBrushHelper.FromHex("#D01C241E"),
                CornerRadius = new CornerRadius(7), Margin = new Thickness(6), Padding = new Thickness(6, 2, 6, 2),
                IsHitTestVisible = false, Child = new TextBlock { Text = "★", FontSize = 14,
                    Foreground = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : UiBrushHelper.FromHex("#EAD48E") } });
            if (!string.IsNullOrWhiteSpace(dayLabel))
                grid.Children.Add(new Border { Tag = "LibDayBadge", HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top,
                    Background = SystemParameters.HighContrast ? SystemColors.WindowBrush : UiBrushHelper.FromHex("#CC162028"),
                    BorderBrush = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : UiBrushHelper.FromHex("#45606E"),
                    BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10), Margin = new Thickness(8, 8, 0, 0),
                    Padding = new Thickness(11, 5, 11, 5), IsHitTestVisible = false,
                    Child = new TextBlock { Text = dayLabel, FontSize = 12.5, FontWeight = FontWeights.SemiBold,
                        MaxWidth = Math.Max(20, size - 50), TextTrimming = TextTrimming.CharacterEllipsis,
                        Foreground = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : UiBrushHelper.FromHex("#E6EEF3") } });
            var button = new Button { Content = grid, Padding = new Thickness(0), BorderThickness = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Stretch,
                Background = Brushes.Transparent, Cursor = Cursors.Hand,
                ToolTip = "Achievement · " + entry.Game.Title + Environment.NewLine + AchievementsInfoWindow.BuildAchievementGridToolTip(row)
                    + Environment.NewLine + "Unlocked " + entry.UnlockLocal.ToString("g", CultureInfo.CurrentCulture) };
            if (!SystemParameters.HighContrast)
                button.Style = LibraryButtonChrome.LibraryToolbarButtonStyle("#14231E", "#14231E", "#253B2F", "#14231E");
            AutomationProperties.SetName(button, "Achievement earned: " + row.Title + ", " + entry.Game.Title + ", "
                + entry.UnlockLocal.ToString("g", CultureInfo.CurrentCulture) + ", " + AchievementScoring.Label(row) + ". Open achievement details.");
            AccessibilityUi.TryApplyFocusVisualStyle(button);
            button.Click += (_, __) => open?.Invoke();
            return new Border { Tag = entry, Width = size, Height = size, CornerRadius = new CornerRadius(10), ClipToBounds = true,
                BorderBrush = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : UiBrushHelper.FromHex("#8A7543"),
                BorderThickness = new Thickness(1), Child = button };
        }

        void OpenTimelineAchievement(LibraryBrowserWorkingSet ws, LibraryTimelineAchievement entry)
        {
            if (ws?.IsActive != true || !ws.ShowTimelineAchievements || !IsLibraryBrowserTimelineView(ws.Current)) return;
            var account = entry.Game.Provider == "steam" ? CurrentSteamUserId64() : CurrentRetroAchievementsUsername();
            if (AchievementHubStore.AccountKey(account) != AchievementHubStore.AccountKey(entry.Account)) return;
            var detail = new AchievementDetailWindow(this, entry.Game, entry.Row, entry.GameRows, entry.SavedUtc, achievementGuideService, null, null,
                achievementTrackingStore, entry.Game.Provider == "steam" ? CurrentSteamUserId64() : CurrentRetroAchievementsUsername());
            detail.ShowDialog();
            if (detail.OpenGameRequested)
                AchievementsInfoWindow.ShowModal(this, entry.Game.Provider == "steam" ? "Steam" : "Emulation", entry.Game.Folder,
                    CurrentSteamWebApiKey(), CurrentRetroAchievementsApiKey(), CurrentSteamUserId64(), CurrentRetroAchievementsUsername(),
                    "PixelVault/" + AppVersion, achievementGuideService, trackingStore: achievementTrackingStore);
        }
    }
}
