using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using PixelVaultNative.UI.Design;

namespace PixelVaultNative
{
    public sealed partial class SettingsShellHost
    {
        UIElement BuildAchievementsSettings()
        {
            var text = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : d.Brush(DesignTokens.TextOnInput);
            var muted = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : d.Brush(DesignTokens.TextLabelMuted);
            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star), MinHeight = 70 });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var heading = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            heading.Children.Add(new TextBlock { Text = "Subcategory order", FontSize = 22, FontWeight = FontWeights.SemiBold, Foreground = text });
            heading.Children.Add(new TextBlock
            {
                Text = "Choose the order used within Base Game and DLC for all games.",
                TextWrapping = TextWrapping.Wrap, Foreground = muted, Margin = new Thickness(0, 4, 0, 0)
            });
            layout.Children.Add(heading);
            var names = new ObservableCollection<string>();
            var saved = new List<string>();
            var status = new TextBlock { Foreground = muted, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(14, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
            var list = new ListBox
            {
                ItemsSource = names, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                ToolTip = "Select a category, then use Move up or Move down.",
                FontSize = 14, Foreground = text,
                Background = SystemParameters.HighContrast ? SystemColors.WindowBrush : d.Brush(DesignTokens.InputBackground),
                BorderBrush = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : d.Brush(DesignTokens.BorderDefault)
            };
            var template = new DataTemplate();
            var label = new FrameworkElementFactory(typeof(TextBlock));
            label.SetBinding(TextBlock.TextProperty, new Binding());
            label.SetBinding(TextBlock.ToolTipProperty, new Binding());
            label.SetValue(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis);
            label.SetValue(FrameworkElement.MarginProperty, new Thickness(8, 6, 8, 6));
            template.VisualTree = label;
            list.ItemTemplate = template;
            ScrollViewer.SetHorizontalScrollBarVisibility(list, ScrollBarVisibility.Disabled);
            AutomationProperties.SetName(list, "Achievement subcategory order");
            AccessibilityUi.TryApplyFocusVisualStyle(list);
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.Children.Add(list);
            var moves = new StackPanel { Margin = new Thickness(16, 0, 0, 0) };
            var up = SettingsButton("Move up", null, DesignTokens.ActionSecondaryFill, Brushes.White);
            var down = SettingsButton("Move down", null, DesignTokens.ActionSecondaryFill, Brushes.White);
            up.Height = down.Height = 32;
            up.Padding = down.Padding = new Thickness(14, 4, 14, 4);
            up.Margin = new Thickness(0, 0, 0, 6); down.Margin = new Thickness(0);
            moves.Children.Add(up); moves.Children.Add(down);
            Grid.SetColumn(moves, 1); grid.Children.Add(moves);
            Grid.SetRow(grid, 1); layout.Children.Add(grid);
            var save = SettingsButton("Save order", null, DesignTokens.ActionPrimaryFill, Brushes.White);
            save.HorizontalAlignment = HorizontalAlignment.Left;
            var footer = new Grid { Margin = new Thickness(0, 10, 0, 0) };
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            footer.Children.Add(save); Grid.SetColumn(status, 1); footer.Children.Add(status);
            Grid.SetRow(footer, 2); layout.Children.Add(footer);
            if (!SystemParameters.HighContrast)
            {
                up.Template = LibraryButtonChrome.BuildLibraryToolbarButtonTemplate(DesignTokens.ActionSecondaryFill, DesignTokens.BorderDefault, "#32434D", "#172127");
                down.Template = up.Template;
                save.Template = LibraryButtonChrome.BuildLibraryToolbarButtonTemplate(DesignTokens.ActionPrimaryFill, "#4F9E81", "#427C64", "#244E3E");
            }
            void Refresh()
            {
                up.IsEnabled = list.SelectedIndex > 0;
                down.IsEnabled = list.SelectedIndex >= 0 && list.SelectedIndex < names.Count - 1;
                save.IsEnabled = d.SaveAchievementCategoryOrder != null && !names.SequenceEqual(saved, StringComparer.OrdinalIgnoreCase);
            }
            void Move(int delta)
            {
                var index = list.SelectedIndex;
                var target = index + delta;
                if (index < 0 || target < 0 || target >= names.Count) return;
                names.Move(index, target);
                list.SelectedIndex = target;
                list.ScrollIntoView(list.SelectedItem);
                status.Text = names.SequenceEqual(saved, StringComparer.OrdinalIgnoreCase) ? "Order unchanged." : "Order not saved.";
                status.Foreground = muted;
                Refresh();
            }
            list.SelectionChanged += (_, __) => Refresh();
            up.Click += (_, __) => Move(-1); down.Click += (_, __) => Move(1);
            save.Click += (_, __) =>
            {
                try
                {
                    var selection = list.SelectedItem as string;
                    d.SaveAchievementCategoryOrder(names.ToList());
                    var current = d.LoadAchievementCategories?.Invoke() ?? names.ToList();
                    saved = current.ToList(); names.Clear(); foreach (var name in saved) names.Add(name);
                    list.SelectedItem = selection;
                    status.Text = "Saved. This order applies to all games."; status.Foreground = muted;
                    Refresh();
                }
                catch (Exception ex) { status.Text = "Could not save category order: " + ex.Message; status.Foreground = SystemParameters.HighContrast ? text : d.Brush("#E8A598"); }
            };
            try
            {
                saved = (d.LoadAchievementCategories?.Invoke() ?? Array.Empty<string>()).ToList();
                foreach (var name in saved) names.Add(name);
                list.SelectedIndex = names.Count > 0 ? 0 : -1;
                status.Text = names.Count > 0 ? "This order is shared across all games." : "Achievement categories are unavailable.";
            }
            catch (Exception ex) { status.Text = "Could not load categories: " + ex.Message; }
            Refresh();
            return new Border
            {
                Background = SystemParameters.HighContrast ? SystemColors.WindowBrush : d.Brush(DesignTokens.PanelElevated),
                BorderBrush = SystemParameters.HighContrast ? SystemColors.WindowTextBrush : d.Brush(DesignTokens.BorderDefault),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(8), Padding = new Thickness(16),
                Margin = new Thickness(0, 12, 0, 0), Child = layout
            };
        }
    }
}
