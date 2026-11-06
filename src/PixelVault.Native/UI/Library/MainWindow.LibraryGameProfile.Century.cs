using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using PixelVault.Tracking;
using Microsoft.Data.Sqlite;

namespace PixelVaultNative
{
    /// <summary>A read-only five-slot display. Empty slots are presentation, never achievement definitions.</summary>
    sealed class GameProfileCenturyShelf : Border
    {
        readonly string provider, game;
        readonly Func<string> account;
        readonly AchievementTrackingStore store;
        readonly Action<GameAchievementsFetchService.AchievementRow, long> open;
        readonly UniformGrid slots = new UniformGrid { Columns = 5 };
        readonly TextBlock earned = new TextBlock { FontSize = 11, VerticalAlignment = VerticalAlignment.Center };
        readonly DispatcherTimer timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        string signature, renderedAccount;
        Dictionary<string, CenturyProgress> savedObservations = new Dictionary<string, CenturyProgress>();
        internal IReadOnlyList<GameAchievementsFetchService.AchievementRow> Rows { get; private set; } = Array.Empty<GameAchievementsFetchService.AchievementRow>();

        internal GameProfileCenturyShelf(string provider, string game, Func<string> account,
            AchievementTrackingStore store, Action<GameAchievementsFetchService.AchievementRow, long> open)
        {
            this.provider = provider; this.game = game; this.account = account; this.store = store; this.open = open;
            Background = Ink("#101A22", SystemColors.WindowBrush); CornerRadius = new CornerRadius(14);
            Padding = new Thickness(18, 16, 18, 16); Margin = new Thickness(0, 0, 0, 16);
            Tag = "GameProfileCenturyShelf";
            var body = new StackPanel(); var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
            earned.Foreground = Ink("#A6BBC8", SystemColors.WindowTextBrush);
            DockPanel.SetDock(earned, Dock.Right); heading.Children.Add(earned);
            heading.Children.Add(new TextBlock { Text = CenturyAchievementCatalog.GroupName, FontSize = 16,
                FontWeight = FontWeights.SemiBold, Foreground = Ink("#A3DCE5", SystemColors.WindowTextBrush) });
            body.Children.Add(heading); body.Children.Add(slots); Child = body;
            AutomationProperties.SetName(this, CenturyAchievementCatalog.GroupName);
            slots.SizeChanged += (_, __) => { var columns = slots.ActualWidth < 354 ? 2 : slots.ActualWidth < 590 ? 3 : 5; if (slots.Columns != columns) slots.Columns = columns; };
            timer.Tick += (_, __) => Refresh();
            Loaded += (_, __) => { Refresh(); if (IsVisible) timer.Start(); };
            Unloaded += (_, __) => timer.Stop();
            IsVisibleChanged += (_, __) => { if (IsLoaded && IsVisible) { Refresh(); timer.Start(); } else timer.Stop(); };
            Refresh();
        }

        static Brush Ink(string color, Brush highContrast) => SystemParameters.HighContrast ? highContrast : UiBrushHelper.FromHex(color);

        internal void Refresh()
        {
            var currentAccount = account?.Invoke();
            var definitions = CenturyAchievementCatalog.ForGame(provider, game);
            Dictionary<string, CenturyProgress> observations;
            try { observations = definitions.ToDictionary(d => d.Id, d => store?.Century(provider, game, d.Id, currentAccount)); }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is SqliteException || ex is System.Text.Json.JsonException)
            {
                if (signature != null && renderedAccount == currentAccount) return;
                observations = definitions.ToDictionary(d => d.Id, d => (CenturyProgress)null);
            }
            var nextRows = definitions.Select(d => AchievementsInfoWindow.CenturyPresentation(d, observations[d.Id])).ToArray();
            var nextSignature = currentAccount + "\n" + string.Join("\n", nextRows.Select(r => r.CenturyDefinitionId + ":" + r.ProgressKnown + ":" + r.Unlocked + ":" + r.UnlockUtcTicks + ":" + r.CounterCurrent + ":" + r.CounterTarget));
            if (signature == nextSignature) return;
            signature = nextSignature; renderedAccount = currentAccount; savedObservations = observations; Rows = nextRows;
            slots.Children.Clear();
            earned.Text = nextRows.Count(r => r.ProgressKnown && r.Unlocked).ToString(CultureInfo.CurrentCulture) + " earned";
            foreach (var definition in definitions)
            {
                var row = nextRows.Single(r => r.CenturyDefinitionId == definition.Id);
                var button = new Button { Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(6, 2, 6, 4),
                    HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top, Tag = row };
                if (!SystemParameters.HighContrast)
                {
                    var template = LibraryButtonChrome.BuildRoundedTileButtonTemplate();
                    template.VisualTree.SetValue(Border.BackgroundProperty, Brushes.Transparent);
                    template.VisualTree.SetValue(Border.BorderThicknessProperty, new Thickness(0));
                    button.Template = template;
                }
                button.Cursor = System.Windows.Input.Cursors.Hand;
                button.Content = Content(row, definition.Title, false);
                button.ToolTip = definition.Title + "\n" + definition.Requirement + "\n100 PV pts"
                    + (row.CounterCurrent.HasValue ? "\n" + row.CounterCurrent.Value.ToString("0", CultureInfo.CurrentCulture) + " / " + row.CounterTarget.Value.ToString("0", CultureInfo.CurrentCulture) : "");
                AutomationProperties.SetName(button, definition.Title + ", " + (row.ProgressKnown && row.Unlocked ? "earned" : "not earned")
                    + (row.CounterCurrent.HasValue ? ", " + row.CounterCurrent.Value.ToString("0", CultureInfo.CurrentCulture) + " of " + row.CounterTarget.Value.ToString("0", CultureInfo.CurrentCulture) : "") + ", 100 PV points. Open achievement details.");
                AccessibilityUi.TryApplyFocusVisualStyle(button);
                button.Click += (_, __) =>
                {
                    Refresh();
                    var fresh = Rows.SingleOrDefault(r => r.CenturyDefinitionId == definition.Id);
                    if (fresh == null) return;
                    savedObservations.TryGetValue(definition.Id, out var verified);
                    open?.Invoke(fresh, verified?.VerifiedUtc ?? 0);
                };
                slots.Children.Add(button);
            }
            for (var i = definitions.Count; i < CenturyAchievementCatalog.PlannedPerGame; i++)
            {
                var placeholder = new Border { Padding = new Thickness(6, 2, 6, 4), Child = Content(null, "To be announced", true), Tag = "CenturyPlaceholder" };
                AutomationProperties.SetName(placeholder, "Unannounced Century achievement");
                slots.Children.Add(placeholder);
            }
        }

        static FrameworkElement Content(GameAchievementsFetchService.AchievementRow row, string title, bool placeholder)
        {
            var body = new StackPanel { HorizontalAlignment = HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 2) };
            var imageHost = new Grid { Width = 106, Height = 107, HorizontalAlignment = HorizontalAlignment.Center };
            if (placeholder)
            {
                imageHost.Children.Add(new Rectangle { Width = 100, Height = 100, RadiusX = 10, RadiusY = 10,
                    Stroke = Ink("#304550", SystemColors.WindowTextBrush), StrokeThickness = 1, StrokeDashArray = new DoubleCollection { 3, 3 },
                    Fill = Ink("#0B1319", SystemColors.WindowBrush), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top });
                imageHost.Children.Add(new TextBlock { Text = "?", FontSize = 32, Foreground = Ink("#718995", SystemColors.WindowTextBrush),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 0, 8) });
            }
            else
            {
                var unlocked = row.ProgressKnown && row.Unlocked;
                var image = new Image { Stretch = Stretch.UniformToFill, Opacity = unlocked ? 1 : .8 };
                // Preserve the row's actual earned state so locked goals use their locked artwork.
                AchievementsInfoWindow.StartAchievementBadgeDownload(image, row, "PixelVault/Profile", 256);
                var icon = new Border { Width = 100, Height = 100, CornerRadius = new CornerRadius(10), Padding = new Thickness(0),
                    Background = Ink("#0B1319", SystemColors.WindowBrush), BorderBrush = Ink(unlocked ? "#86BAA1" : "#304550", SystemColors.WindowTextBrush), BorderThickness = new Thickness(1),
                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Child = image };
                CenturyAchievementFinish.Apply(icon, row);
                if (icon.Child == image)
                {
                    // Fill the shelf frame, cropping the artwork's baked-in outer rim.
                    image.RenderTransformOrigin = new Point(.5, .5);
                    image.RenderTransform = new ScaleTransform(1.12, 1.12);
                    icon.Child = null;
                    var face = new Grid { ClipToBounds = true, IsHitTestVisible = false };
                    face.Children.Add(image); icon.Child = face;
                    face.SizeChanged += (_, __) => face.Clip = new RectangleGeometry(new Rect(face.RenderSize), 9, 9);
                }
                imageHost.Children.Add(icon);
                if (!unlocked)
                    imageHost.Children.Add(new Border { Width = 21, Height = 21, CornerRadius = new CornerRadius(11),
                        HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                        Background = Ink("#0B1319", SystemColors.WindowBrush), BorderBrush = Ink("#304550", SystemColors.WindowTextBrush), BorderThickness = new Thickness(1),
                        Child = new TextBlock { Text = "\uE72E", FontFamily = new FontFamily("Segoe MDL2 Assets"), FontSize = 11,
                            Foreground = Ink("#A6BBC8", SystemColors.WindowTextBrush), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center } });
            }
            body.Children.Add(imageHost);
            body.Children.Add(new TextBlock { Text = title, FontSize = 12, Height = 34, Margin = new Thickness(0, 7, 0, 0), TextAlignment = TextAlignment.Center,
                TextWrapping = TextWrapping.Wrap, TextTrimming = TextTrimming.CharacterEllipsis, Foreground = Ink(placeholder ? "#A6BBC8" : "#F3F6F8", SystemColors.WindowTextBrush) });
            return body;
        }
    }

    public sealed partial class MainWindow
    {
        GameProfileCenturyShelf BuildLibraryGameProfileCenturyShelf(Window owner, LibraryFolderInfo folder)
        {
            string provider = null, gameId = null;
            if (GameAchievementsFetchService.TryPickAchievementsBackend(NormalizeConsoleLabel(folder?.PlatformLabel), folder, out var steam))
            { provider = steam ? "steam" : "retroachievements"; gameId = (steam ? folder?.SteamAppId : folder?.RetroAchievementsGameId)?.Trim(); }
            string Account() => provider == "steam" ? CurrentSteamUserId64() : provider == "retroachievements" ? CurrentRetroAchievementsUsername() : "";
            GameProfileCenturyShelf shelf = null;
            shelf = new GameProfileCenturyShelf(provider, gameId, Account, achievementTrackingStore, (row, savedUtc) =>
            {
                var game = new AchievementHubGame { Provider = row.Provider, Id = row.ProviderGameId, Title = folder?.Name, Folder = folder };
                new AchievementDetailWindow(owner, game, row, shelf.Rows, savedUtc, null,
                    trackingStore: achievementTrackingStore, trackingAccount: Account()).Show();
            });
            EventHandler activated = (_, __) => shelf.Refresh();
            owner.Activated += activated;
            owner.Closed += (_, __) => owner.Activated -= activated;
            return shelf;
        }
    }
}
