using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using PixelVault.Tracking;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        readonly Dictionary<string, AchievementCompletionTier> libraryCompletionTiers = new();
        DispatcherTimer libraryCompletionRefreshTimer;
        bool reconcilingSavedLibraryCompletion;

        async Task ReconcileSavedLibraryCompletionAsync()
        {
            if (reconcilingSavedLibraryCompletion || _achievementHubStore == null || librarySession?.HasLibraryRoot != true) return;
            reconcilingSavedLibraryCompletion = true;
            var root = libraryRoot;
            var steamAccount = CurrentSteamUserId64();
            var retroAccount = CurrentRetroAchievementsUsername();
            try
            {
                var savedGames = new List<AchievementHubStore.Snapshot>();
                foreach (var source in new[] { (Provider:"steam",Account:steamAccount),(Provider:"retroachievements",Account:retroAccount) })
                {
                    if (string.IsNullOrWhiteSpace(source.Account)) continue;
                    var saved = await _achievementHubStore.LoadAsync(source.Provider, source.Account, _achievementMonitorCancellation.Token);
                    savedGames.AddRange(saved.Games.Values);
                }
                await Task.Run(() => achievementGuideService?.LoadSavedAchievementGroups(savedGames.SelectMany(g => g.Rows)), _achievementMonitorCancellation.Token);
                if (libraryRoot != root || steamAccount != CurrentSteamUserId64() || retroAccount != CurrentRetroAchievementsUsername()) return;
                var rows = librarySession.LoadSavedGameIndexRows();
                var changed = new List<GameIndexEditorRow>();
                foreach (var saved in savedGames)
                    changed.AddRange(MarkCompletedAchievementGames(new GameAchievementsFetchService.FetchResult { Rows = saved.Rows }, rows));
                if (changed.Count == 0) return;
                librarySession.PersistGameIndexRows(rows);
                ApplyReconciledAchievementCompletion(changed);
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { LogException("Library completion saved-progress reconciliation", ex); }
            finally { reconcilingSavedLibraryCompletion = false; }
        }

        internal static AchievementCompletionTier ClassifyLibraryAchievementCompletion(
            IEnumerable<GameAchievementsFetchService.AchievementRow> rows, bool markedComplete = false,
            IEnumerable<AchievementCompletionProgress> pvProgress = null) =>
            AchievementCompletion.Classify(rows?.Select(r => new AchievementCompletionProgress(
                r?.ProgressKnown == true, r?.Unlocked == true,
                !string.IsNullOrWhiteSpace(AchievementOrganization.DlcName(r?.DlcName, r?.GroupName)))), markedComplete, pvProgress);

        void QueueLibraryCompletionRefresh()
        {
            libraryCompletionTiers.Clear();
            if (libraryCompletionRefreshTimer == null)
            {
                libraryCompletionRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
                libraryCompletionRefreshTimer.Tick += (_, __) =>
                {
                    libraryCompletionRefreshTimer.Stop();
                    _libraryBrowserLiveWorkingSet?.RerenderFolderList?.Invoke();
                };
                Closed += (_, __) => libraryCompletionRefreshTimer.Stop();
            }
            libraryCompletionRefreshTimer.Stop();
            libraryCompletionRefreshTimer.Start();
        }

        internal static bool HasLibraryAchievementSource(LibraryFolderInfo folder) => folder != null
            && GameAchievementsFetchService.TryPickAchievementsBackend(NormalizeConsoleLabel(folder.PlatformLabel), folder, out _);

        FrameworkElement CreateLibraryCompletionDecoration(LibraryBrowserFolderView view, double width, double height, CornerRadius corner, Action<AchievementCompletionTier> updateLayout = null)
        {
            var host = new Grid { Width = width, Height = height, IsHitTestVisible = false, ClipToBounds = false };
            void Paint(AchievementCompletionTier tier)
            {
                host.Children.Clear();
                if (tier != AchievementCompletionTier.None)
                    host.Children.Add(BuildLibraryTileCompletionFrameOverlay(width, height, corner, tier));
                AutomationProperties.SetName(host, tier == AchievementCompletionTier.None ? string.Empty : tier == AchievementCompletionTier.BaseAndDlcAndPv ? "Base, DLC and PV achievements complete" : tier == AchievementCompletionTier.BaseAndDlc
                    ? "Base and DLC achievements complete" : "Base achievements complete");
                updateLayout?.Invoke(tier);
            }
            // Old index flags may have been set by the legacy category-based check.
            // Never show a provider completion award before verifying saved achievements.
            Paint(AchievementCompletionTier.None);
            host.Loaded += async (_, __) =>
            {
                try
                {
                    var tier = AchievementCompletionTier.None;
                    var folders = view.SourceFolders.Count > 0 ? view.SourceFolders : new List<LibraryFolderInfo> { BuildLibraryBrowserDisplayFolder(view) };
                    foreach (var folder in folders.Where(f => f != null))
                    {
                        if (!GameAchievementsFetchService.TryPickAchievementsBackend(NormalizeConsoleLabel(folder.PlatformLabel), folder, out var steam))
                        {
                            if (folder.IsCompleted100Percent && tier == AchievementCompletionTier.None) tier = AchievementCompletionTier.Base;
                            continue;
                        }
                        var provider = steam ? "steam" : "retroachievements";
                        var account = steam ? CurrentSteamUserId64() : CurrentRetroAchievementsUsername();
                        var game = steam ? folder.SteamAppId : folder.RetroAchievementsGameId;
                        if (string.IsNullOrWhiteSpace(account) || string.IsNullOrWhiteSpace(game) || _achievementHubStore == null) continue;
                        var key = provider + "\n" + account + "\n" + game;
                        if (!libraryCompletionTiers.TryGetValue(key, out var savedTier))
                        {
                            var revision = _achievementHubStore.Revision;
                            var snapshot = await _achievementHubStore.LoadGameAsync(provider, account, game, _achievementMonitorCancellation.Token);
                            if (snapshot?.Rows == null) continue;
                            await Task.Run(() => achievementGuideService?.LoadSavedAchievementGroups(snapshot.Rows));
                            savedTier = ClassifyLibraryAchievementCompletion(snapshot.Rows);
                            if (_achievementHubStore.Revision == revision) libraryCompletionTiers[key] = savedTier;
                        }
                        if (savedTier > tier) tier = savedTier;
                    }
                    if (host.IsLoaded) Paint(tier);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex) { LogException("Library completion decoration", ex); }
            };
            return host;
        }

        internal static FrameworkElement BuildLibraryTileCompletionFrameOverlay(double width, double height, CornerRadius corner, bool emerald = false)
            => new CompletionFrameElement(corner, emerald) { Width = width, Height = height };

        internal static FrameworkElement BuildLibraryTileCompletionFrameOverlay(double width, double height, CornerRadius corner, AchievementCompletionTier tier)
            => new CompletionFrameElement(corner, tier == AchievementCompletionTier.BaseAndDlc, tier == AchievementCompletionTier.BaseAndDlcAndPv) { Width = width, Height = height };

        internal static DrawingGroup BuildLibraryCompletionFrameDrawing(double width, double height, CornerRadius corner, bool emerald, double pixelsPerDip, bool diamond = false)
        {
            var root = new DrawingGroup();
            if (width <= 0 || height <= 0) { root.Freeze(); return root; }
            var tone = diamond ? "diamond" : emerald ? "emerald" : "gold";
            var scale = Math.Clamp(width / 220, 2d / 3, 2);
            var rail = Math.Clamp(width * 4 / 220, 3, 6);
            var light = HeritageCompletionOrnaments.Paint(diamond ? "#effaff" : emerald ? "#e0f3ce" : "#f9e1a4");
            var shade = HeritageCompletionOrnaments.Paint(diamond ? "#486981" : emerald ? "#235b45" : "#654820");
            void Stroke(double inset, Brush brush, double thickness)
            {
                var radius = Math.Max(1, corner.TopLeft - inset);
                root.Children.Add(new GeometryDrawing(null, new Pen(brush, thickness),
                    new RectangleGeometry(new Rect(inset, inset, Math.Max(0, width - inset * 2), Math.Max(0, height - inset * 2)), radius, radius)));
            }
            var contrast = SystemParameters.HighContrast;
            Stroke(rail / 2, contrast ? SystemColors.WindowTextBrush : HeritageCompletionOrnaments.Paint(tone), rail);
            if (!contrast)
            {
                Stroke(1, light, .6);
                Stroke(rail - .5, shade, .7);
                // The same 42px perimeter tracery and 56px clasp are generated for Companion.
                var edge = 42 * scale;
                HeritageCompletionOrnaments.Add(root, tone + "-corner", 42, scale, 0, 0);
                HeritageCompletionOrnaments.Add(root, tone + "-corner", 42, scale, width - edge, 0, true);
                HeritageCompletionOrnaments.Add(root, tone + "-corner", 42, scale, 0, height - edge, false, true);
                HeritageCompletionOrnaments.Add(root, tone + "-corner", 42, scale, width - edge, height - edge, true, true);
                HeritageCompletionOrnaments.Add(root, tone + "-accent", 42, scale, width - edge, 0, true);
                HeritageCompletionOrnaments.Add(root, tone + "-accent", 42, scale, 0, height - edge, false, true);
                HeritageCompletionOrnaments.Add(root, tone + "-badge", 56, scale, width - 55 * scale, height - 55 * scale);
            }
            var center = new Point(width - 27 * scale, height - 27 * scale);
            if (contrast)
                root.Children.Add(new GeometryDrawing(SystemColors.WindowBrush, new Pen(SystemColors.WindowTextBrush, 1),
                    new EllipseGeometry(center, 25 * scale, 25 * scale)));
            var ink = contrast ? SystemColors.WindowTextBrush : HeritageCompletionOrnaments.Paint(diamond ? "#edfbff" : emerald ? "#c6f7dc" : "#f9dfa3");
            var text = new FormattedText("100%", System.Globalization.CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface(SystemFonts.MessageFontFamily, FontStyles.Normal, FontWeights.Bold, FontStretches.Normal), 14 * scale, ink, pixelsPerDip);
            var origin = new Point(center.X - text.Width / 2, center.Y - text.Height / 2);
            if (!contrast)
                root.Children.Add(new GeometryDrawing(HeritageCompletionOrnaments.Paint("#99000000"), null, text.BuildGeometry(new Point(origin.X, origin.Y + scale))));
            root.Children.Add(new GeometryDrawing(ink, null, text.BuildGeometry(origin)));
            root.Freeze(); return root;
        }

    }

    // A single retained drawing has no child layout to invalidate while scrolling or resizing.
    internal sealed class CompletionFrameElement : FrameworkElement
    {
        readonly CornerRadius corner;
        readonly bool emerald;
        readonly bool diamond;
        DrawingGroup drawing;
        Size drawingSize;
        double drawingDpi;
        bool drawingHighContrast;

        internal CompletionFrameElement(CornerRadius corner, bool emerald, bool diamond = false)
        {
            this.corner = corner; this.emerald = emerald; this.diamond = diamond;
            IsHitTestVisible = false; ClipToBounds = false;
            SnapsToDevicePixels = true; UseLayoutRounding = true;
        }
        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            InvalidateVisual();
        }
        protected override void OnRender(DrawingContext context)
        {
            base.OnRender(context);
            if (RenderSize.Width <= 0 || RenderSize.Height <= 0) return;
            var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
            if (drawing == null || drawingSize != RenderSize || drawingDpi != dpi || drawingHighContrast != SystemParameters.HighContrast)
            {
                drawing = MainWindow.BuildLibraryCompletionFrameDrawing(RenderSize.Width, RenderSize.Height, corner, emerald, dpi, diamond);
                drawingSize = RenderSize; drawingDpi = dpi; drawingHighContrast = SystemParameters.HighContrast;
            }
            context.DrawDrawing(drawing);
        }
    }
}
