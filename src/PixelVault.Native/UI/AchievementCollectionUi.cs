using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace PixelVaultNative
{
    static class AchievementCollectionUi
    {
        internal static bool ShowMissable(GameAchievementsFetchService.AchievementRow row) => row?.IsMissable == true && !row.Unlocked;

        internal static FrameworkElement WithMissableOverlay(Border icon, GameAchievementsFetchService.AchievementRow row)
        {
            if (!ShowMissable(row)) return icon;
            var host = new Grid { Width = icon.Width, Height = icon.Height, Margin = icon.Margin,
                HorizontalAlignment = icon.HorizontalAlignment, VerticalAlignment = icon.VerticalAlignment };
            icon.Margin = new Thickness(0);
            host.Children.Add(icon);
            var symbol = new Path
            {
                Data = Geometry.Parse("M 2,1 L 12,1 L 12,4 L 8,8 L 12,12 L 12,15 L 2,15 L 2,12 L 6,8 L 2,4 Z M 2,3 L 12,3 M 2,13 L 12,13"),
                Width = 14, Height = 16, StrokeThickness = 1.2,
                Stroke = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : UiBrushHelper.FromHex("#EFBD63"),
                HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
            };
            var marker = new Border { Width = 25, Height = 25, CornerRadius = new CornerRadius(13),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, -4, -4), Child = symbol, Tag = "AchievementMissable",
                Background = SystemParameters.HighContrast ? SystemColors.WindowBrush : UiBrushHelper.FromHex("#3C3020"),
                BorderBrush = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : UiBrushHelper.FromHex("#11191E"),
                BorderThickness = new Thickness(2), ToolTip = "Missable — can be missed during a playthrough." };
            AutomationProperties.SetName(marker, "Missable achievement");
            host.Children.Add(marker);
            return host;
        }

        internal static TextBlock MissableLabel(GameAchievementsFetchService.AchievementRow row)
        {
            if (!ShowMissable(row)) return null;
            return new TextBlock { Text = "Missable", FontSize = 11, Margin = new Thickness(0, 4, 0, 0),
                Tag = "AchievementMissable", Foreground = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : UiBrushHelper.FromHex("#EFBD63"),
                ToolTip = "Can be missed during a playthrough." };
        }

        internal static Border ScoreChip(GameAchievementsFetchService.AchievementRow row)
        {
            var chip = TagChip(AchievementScoring.PointText(row));
            chip.Margin = new Thickness(0, 3, 0, 0);
            chip.ToolTip = AchievementScoring.Explanation(row);
            AutomationProperties.SetName(chip, AchievementScoring.Label(row));
            return chip;
        }

        internal static Border TagChip(string tag) => new Border
        {
            CornerRadius = new CornerRadius(4), Padding = new Thickness(6, 2, 6, 2), Margin = new Thickness(0, 0, 4, 4),
            Background = SystemParameters.HighContrast ? SystemColors.WindowBrush : UiBrushHelper.FromHex("#1B2B25"),
            BorderBrush = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : UiBrushHelper.FromHex("#355746"),
            BorderThickness = new Thickness(1), ToolTip = tag,
            Child = new TextBlock { Text = tag, FontSize = 11, TextWrapping = TextWrapping.Wrap,
                Foreground = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : UiBrushHelper.FromHex("#B8D5C4") }
        };

        // No indeterminate animation: absence of a bar is paired with explicit unavailable text.
        internal static FrameworkElement GroupProgress(AchievementsInfoWindow.AchievementGroupSummary group)
        {
            var track = new Grid { Height = 3, Margin = new Thickness(0, 6, 0, 0),
                Visibility = group?.Percent.HasValue == true ? Visibility.Visible : Visibility.Collapsed,
                Background = SystemParameters.HighContrast ? SystemColors.WindowBrush : UiBrushHelper.FromHex("#30423F") };
            var fraction = group?.ProgressKnown == true && group.Total > 0 ? group.Earned / (double)group.Total : 0;
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(fraction, GridUnitType.Star) });
            track.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1 - fraction, GridUnitType.Star) });
            track.Children.Add(new Border { Background = SystemParameters.HighContrast ? SystemColors.HighlightBrush : UiBrushHelper.FromHex("#88B8A0"), Tag = "AchievementAccent" });
            AutomationProperties.SetName(track, group?.Percent.HasValue == true ? group.Name + ": " + group.Percent.Value + "% complete" : "Progress unavailable");
            return track;
        }

        internal static void ApplyHighContrast(DependencyObject root)
        {
            if (!SystemParameters.HighContrast || root == null) return;
            if (root is FrameworkElement element && (element.Tag as string == "AchievementPin" || element.Tag as string == "AchievementAccent")) return;
            if (root is Control control)
            {
                control.SetResourceReference(Control.ForegroundProperty, SystemColors.WindowTextBrushKey);
                control.SetResourceReference(Control.BackgroundProperty, SystemColors.WindowBrushKey);
                control.SetResourceReference(Control.BorderBrushProperty, SystemColors.WindowTextBrushKey);
            }
            else if (root is TextBlock text) text.SetResourceReference(TextBlock.ForegroundProperty, SystemColors.WindowTextBrushKey);
            else if (root is Border border)
            {
                border.SetResourceReference(Border.BackgroundProperty, SystemColors.WindowBrushKey);
                border.SetResourceReference(Border.BorderBrushProperty, SystemColors.WindowTextBrushKey);
            }
            else if (root is Panel panel && panel.Background != null) panel.SetResourceReference(Panel.BackgroundProperty, SystemColors.WindowBrushKey);
            else if (root is Shape shape)
            {
                if (shape.Stroke != null) shape.SetResourceReference(Shape.StrokeProperty, SystemColors.WindowTextBrushKey);
                if (shape.Fill != null) shape.SetResourceReference(Shape.FillProperty, SystemColors.WindowTextBrushKey);
            }
            foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>()) ApplyHighContrast(child);
        }
    }
}
