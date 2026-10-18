using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PixelVaultNative
{
    sealed partial class AchievementsInfoWindow
    {
        readonly ComboBox _sort = new ComboBox { Width = 290, MinHeight = 30, VerticalContentAlignment = VerticalAlignment.Center };
        readonly Expander _progressDetails = new Expander
        {
            Header = "Progress details", Foreground = Brushes.White, Margin = new Thickness(8, 8, 8, 4),
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        AchievementCollectionStats _stats;
        AchievementCollectionSort SelectedSort => (AchievementCollectionSort)Math.Max(0, _sort.SelectedIndex);
        bool SortingByCounterProgress => SelectedSort == AchievementCollectionSort.ProgressHighestFirst
            || SelectedSort == AchievementCollectionSort.ProgressLowestFirst;

        internal static string CounterLabel(GameAchievementsFetchService.AchievementRow row) =>
            !AchievementCollectionStats.CounterPercent(row).HasValue ? string.Empty
            : row.CounterCurrent.Value.ToString("#,0.##", CultureInfo.CurrentCulture)
                + " / " + row.CounterTarget.Value.ToString("#,0.##", CultureInfo.CurrentCulture);

        static FrameworkElement BuildCounterProgress(GameAchievementsFetchService.AchievementRow row)
        {
            var percent = AchievementCollectionStats.CounterPercent(row);
            if (!percent.HasValue) return null;
            var label = "Counter progress: " + CounterLabel(row);
            var panel = new StackPanel { Margin = new Thickness(0, 7, 0, 0), HorizontalAlignment = HorizontalAlignment.Stretch };
            var text = StatsText(CounterLabel(row));
            text.FontSize = 11;
            text.Foreground = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : UiBrushHelper.FromHex("#A8C6D3");
            panel.Children.Add(text);
            var bar = new ProgressBar
            {
                Minimum = 0, Maximum = 100, Value = percent.Value, Height = 5,
                Foreground = SystemParameters.HighContrast ? SystemColors.HighlightBrush : UiBrushHelper.FromHex("#6EAFBF"),
                Background = SystemParameters.HighContrast ? SystemColors.ControlBrush : UiBrushHelper.FromHex("#263C48"),
                BorderThickness = new Thickness(0), IsHitTestVisible = false,
                ToolTip = label + " · " + percent.Value.ToString("0.#", CultureInfo.CurrentCulture) + "%"
            };
            AutomationProperties.SetName(bar, label);
            panel.ToolTip = "Latest counter reported to Steam. Unlock status is reported separately."
                + (row.CounterMinimum > 0 ? " Progress range: " + row.CounterMinimum.Value.ToString("0.##", CultureInfo.CurrentCulture) + "–" + row.CounterTarget.Value.ToString("0.##", CultureInfo.CurrentCulture) + "." : "");
            panel.Children.Add(bar);
            return panel;
        }

        FrameworkElement BuildSortControls()
        {
            foreach (var label in new[] { "Provider / default", "Global unlock %: rarest first", "Global unlock %: most common first", "Unlock date: newest first", "Unlock date: oldest first", "Counter progress: highest first", "Counter progress: lowest first" })
                _sort.Items.Add(label);
            _sort.SelectedIndex = 0;
            _sort.ToolTip = "Sort within each group. Unavailable values appear last; ties retain default order.";
            AutomationProperties.SetName(_sort, "Achievement sort order");
            AccessibilityUi.TryApplyFocusVisualStyle(_sort);
            _sort.SelectionChanged += delegate
            {
                if (_loadedResult != null) RenderAchievementRows(_loadedResult.Rows, _userAgent, _loadedResult.SourceLabel);
            };
            var tools = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 8, 0, 0) };
            tools.Children.Add(new TextBlock { Text = "Sort", Foreground = UiBrushHelper.FromHex("#9CB1BC"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
            tools.Children.Add(_sort);
            return tools;
        }

        List<GameAchievementsFetchService.AchievementRow> VisibleSortedRows(IEnumerable<GameAchievementsFetchService.AchievementRow> rows) =>
            AchievementCollectionStats.Sort(FilterAchievementRows(rows, _remainingOnly.IsChecked == true), SelectedSort, DateTime.UtcNow.Ticks);

        List<AchievementGridSection> VisibleSections(List<GameAchievementsFetchService.AchievementRow> rows) =>
            SelectedSort == AchievementCollectionSort.Provider ? BuildAchievementGridSections(rows)
            : rows.Count == 0 ? new List<AchievementGridSection>()
            : new List<AchievementGridSection> { new AchievementGridSection { Key = "sorted", Title = "Achievements", Rows = rows } };

        void UpdateProgressDetails(List<GameAchievementsFetchService.AchievementRow> rows)
        {
            _stats = AchievementCollectionStats.Build(rows, DateTime.UtcNow.Ticks, _loadedResult?.ScoreTotals);
            var panel = new StackPanel { Margin = new Thickness(4, 8, 4, 8) };
            panel.Children.Add(StatsText(_stats.Latest == null ? (_stats.Earned > 0 ? "Latest unlock: date unavailable"
                : _stats.Unknown > 0 ? "Latest unlock: progress unavailable" : "Latest unlock: none yet")
                : "Latest unlock: " + _stats.Latest.Title + " · " + FormatAchievementEarnedDateLabel(_stats.Latest.UnlockUtcTicks)));
            panel.Children.Add(StatsText(_stats.Rarest == null ? (_stats.Earned > 0 ? "Rarest earned: global unlock percentage unavailable"
                : _stats.Unknown > 0 ? "Rarest earned: progress unavailable" : "Rarest earned: none yet")
                : "Rarest earned: " + _stats.Rarest.Title + " · " + _stats.Rarest.GlobalUnlockPercent.Value.ToString("0.##", CultureInfo.CurrentCulture) + "% global unlocks"));
            if (_stats.EarnedWithRarity > 0 && _stats.EarnedWithRarity < _stats.Earned)
                panel.Children.Add(StatsText("Rarity available for " + _stats.EarnedWithRarity + " of " + _stats.Earned + " earned achievements."));
            var coverage = _stats.DatedEarned + " of " + _stats.Earned + " earned achievements have valid unlock dates.";
            panel.Children.Add(StatsText(coverage + (_stats.Unknown > 0 ? " Player progress is incomplete." : string.Empty)));
            if (_stats.History.Count == 0)
                panel.Children.Add(StatsText("Unlock history unavailable."));
            else
            {
                panel.Children.Add(StatsText("Cumulative earned · dated unlocks only (UTC)"));
                var canvas = new Canvas { Height = 76, Margin = new Thickness(10, 6, 10, 4), ClipToBounds = true };
                var history = _stats.History;
                var historyText = string.Join(Environment.NewLine, history.Select(point => point.DayUtc.ToString("yyyy-MM-dd") + ": " + point.Earned + " earned"));
                AutomationProperties.SetName(canvas, "Cumulative dated unlock history. " + coverage);
                canvas.SizeChanged += delegate { DrawUnlockHistory(canvas, history); };
                panel.Children.Add(canvas);
                panel.Children.Add(StatsText(history[0].DayUtc.ToString("MMM d, yyyy") + " → " + history[history.Count - 1].DayUtc.ToString("MMM d, yyyy")));
                var values = new TextBox
                {
                    Text = historyText, IsReadOnly = true, TextWrapping = TextWrapping.Wrap, MaxHeight = 130,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Background = UiBrushHelper.FromHex("#11191E"), Foreground = Brushes.White, Padding = new Thickness(6)
                };
                AutomationProperties.SetName(values, "Unlock history dates and cumulative earned counts");
                panel.Children.Add(new Expander { Header = "History data", Content = values, Foreground = Brushes.White, Margin = new Thickness(0, 4, 0, 0) });
            }
            _progressDetails.Content = panel;
        }

        static TextBlock StatsText(string text) => new TextBlock
        {
            Text = text, TextWrapping = TextWrapping.Wrap, Foreground = UiBrushHelper.FromHex("#C8D8E2"), FontSize = 12,
            Margin = new Thickness(0, 0, 0, 5)
        };

        static void DrawUnlockHistory(Canvas canvas, List<AchievementCollectionStats.HistoryPoint> history)
        {
            canvas.Children.Clear();
            var width = Math.Max(1, canvas.ActualWidth - 28);
            var height = 64d;
            var first = history[0].DayUtc;
            var span = (history[history.Count - 1].DayUtc - first).TotalDays;
            var total = history[history.Count - 1].Earned;
            var line = new Polyline { Stroke = SystemParameters.HighContrast ? SystemColors.HighlightBrush : UiBrushHelper.FromHex("#88B8A0"), StrokeThickness = 2 };
            line.Points.Add(new Point(25, height));
            var previous = 0;
            foreach (var point in history)
            {
                var x = 25 + (span == 0 ? width / 2 : (point.DayUtc - first).TotalDays / span * width);
                line.Points.Add(new Point(x, height - previous * height / total));
                line.Points.Add(new Point(x, height - point.Earned * height / total));
                previous = point.Earned;
            }
            canvas.Children.Add(line);
            var top = StatsText(total.ToString()); Canvas.SetTop(top, 0); canvas.Children.Add(top);
            var bottom = StatsText("0"); Canvas.SetTop(bottom, height - 8); canvas.Children.Add(bottom);
        }
    }
}
