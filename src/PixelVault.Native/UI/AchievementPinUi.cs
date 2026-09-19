using System;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Data;
using System.Windows.Shapes;

namespace PixelVaultNative
{
    static class AchievementPinUi
    {
        internal static bool Matches(AchievementGuideEntry entry, GameAchievementsFetchService.AchievementRow row) =>
            entry != null && row != null
            && string.Equals(entry.Provider?.Trim(), row.Provider?.Trim(), StringComparison.OrdinalIgnoreCase)
            && string.Equals(entry.ProviderGameId?.Trim(), row.ProviderGameId?.Trim(), StringComparison.Ordinal)
            && string.Equals(entry.ProviderAchievementId?.Trim(), row.ProviderAchievementId?.Trim(), StringComparison.Ordinal);

        internal static GameAchievementsFetchService.AchievementRow StoredRow(AchievementGuideEntry entry) => new GameAchievementsFetchService.AchievementRow
        {
            Provider = entry.Provider, ProviderGameId = entry.ProviderGameId, ProviderAchievementId = entry.ProviderAchievementId,
            Title = entry.Title, Description = entry.Description, IconUrlColor = entry.IconUrl,
            GroupName = entry.GroupName, GroupSortOrder = entry.GroupSortOrder, ProgressKnown = false
        };

        internal static Button Button(GameAchievementsFetchService.AchievementRow row, bool pinned, Action action)
        {
            var label = pinned ? "Unpin from Next Up" : "Pin to Next Up";
            var enabled = pinned || (row?.HasStableProviderIdentity == true && row.ProgressKnown && !row.Unlocked);
            var button = new Button
            {
                Content = Icon(pinned),
                Width = 24, Height = 24, Padding = new Thickness(3), Tag = "AchievementPin",
                Background = Brushes.Transparent,
                Foreground = SystemParameters.HighContrast ? SystemColors.ControlTextBrush : UiBrushHelper.FromHex(pinned ? "#9FC7B0" : "#9CB1BC"),
                BorderBrush = Brushes.Transparent,
                BorderThickness = new Thickness(1),
                ToolTip = enabled ? label : row?.Unlocked == true && row.ProgressKnown
                    ? "Already earned" : "Refresh player progress before pinning this achievement.",
                IsEnabled = enabled
            };
            var template = new ControlTemplate(typeof(Button));
            var frame = new FrameworkElementFactory(typeof(Border));
            frame.SetValue(Border.CornerRadiusProperty, new CornerRadius(5));
            frame.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
            frame.SetValue(Border.BorderBrushProperty, new TemplateBindingExtension(Control.BorderBrushProperty));
            frame.SetValue(Border.BorderThicknessProperty, new TemplateBindingExtension(Control.BorderThicknessProperty));
            frame.SetValue(Border.PaddingProperty, new TemplateBindingExtension(Control.PaddingProperty));
            var presenter = new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            presenter.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
            frame.AppendChild(presenter);
            template.VisualTree = frame;
            button.Template = template;
            var style = new Style(typeof(Button));
            var hover = new Trigger { Property = UIElement.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Control.BackgroundProperty, SystemParameters.HighContrast ? SystemColors.HighlightBrush : UiBrushHelper.FromHex("#263A40")));
            hover.Setters.Add(new Setter(Control.ForegroundProperty, SystemParameters.HighContrast ? SystemColors.HighlightTextBrush : Brushes.White));
            style.Triggers.Add(hover);
            var focus = new Trigger { Property = UIElement.IsKeyboardFocusedProperty, Value = true };
            focus.Setters.Add(new Setter(Control.BorderBrushProperty, SystemParameters.HighContrast ? SystemColors.WindowTextBrush : UiBrushHelper.FromHex("#9FC7B0")));
            style.Triggers.Add(focus);
            // Set defaults through the style so hover/focus triggers can override them.
            foreach (var property in new[] { Control.BackgroundProperty, Control.ForegroundProperty, Control.BorderBrushProperty })
            {
                style.Setters.Add(new Setter(property, button.GetValue(property)));
                button.ClearValue(property);
            }
            button.Style = style;
            ToolTipService.SetShowOnDisabled(button, true);
            AutomationProperties.SetName(button, label + ": " + (row?.Title ?? "Achievement"));
            AccessibilityUi.TryApplyFocusVisualStyle(button);
            button.Click += (_, e) => { e.Handled = true; action(); };
            return button;
        }

        static FrameworkElement Icon(bool pinned)
        {
            var icon = new Path
            {
                Data = Geometry.Parse("M 5,1 L 11,1 L 10,6 L 13,9 L 3,9 L 6,6 Z M 8,9 L 8,15"),
                StrokeThickness = 1.4, StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
                Width = 16, Height = 16, RenderTransformOrigin = new Point(.5, .5),
                RenderTransform = new RotateTransform(35), IsHitTestVisible = false
            };
            var foreground = new Binding("Foreground") { RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor, typeof(Button), 1) };
            icon.SetBinding(Shape.StrokeProperty, foreground);
            if (pinned) icon.SetBinding(Shape.FillProperty, foreground);
            return icon;
        }

        internal static bool Change(Window owner, IAchievementGuideService service, GameAchievementsFetchService.AchievementRow row, bool pinned)
        {
            try
            {
                var result = service.SetAchievementPinned(row, pinned);
                if (result == AchievementPinChange.Changed || result == AchievementPinChange.Unchanged) return true;
                var message = result == AchievementPinChange.LimitReached
                    ? "Next Up already has three targets for this game. Unpin a target before adding another."
                    : result == AchievementPinChange.AlreadyEarned ? "This achievement is already earned."
                    : result == AchievementPinChange.ProgressUnavailable ? "Refresh player progress before pinning this achievement."
                    : "This achievement is not currently available. Refresh the collection and try again.";
                MessageBox.Show(owner, message, "Next Up", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, "Could not save the Next Up change.\n\n" + ex.Message, "Next Up", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            return false;
        }
    }
}
