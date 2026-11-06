using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;

namespace PixelVaultNative
{
    sealed class GameProfilePlatformBar : Border
    {
        internal readonly ComboBox Selector = new() { MinWidth = 180, Height = 38, FontSize = 13, HorizontalContentAlignment = HorizontalAlignment.Left };
        internal readonly Button Sync = ActionButton("Sync game", 116, true);
        internal readonly Button Link = ActionButton("Link Xbox game…", 146);
        internal readonly TextBlock Status = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 8, 0, 0) };
        internal GameProfilePlatformBar()
        {
            TrackerFormStyle.ApplyScoped(this);
            System.Windows.Documents.TextElement.SetFontFamily(this, new FontFamily("Segoe UI"));
            Background = Surface; BorderBrush = Line; BorderThickness = new Thickness(1);
            CornerRadius = new CornerRadius(14); Padding = new Thickness(16, 12, 16, 12); Margin = new Thickness(0, 0, 0, 18);
            var panel = new StackPanel(); Child = panel;
            panel.Children.Add(new TextBlock { Text = "Default Platform", FontSize = 12, FontWeight = FontWeights.SemiBold, Foreground = Muted, Margin = new Thickness(0, 0, 0, 7) });
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition());
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Selector.Margin = new Thickness(0, 0, 12, 0); Sync.Margin = new Thickness(0, 0, 8, 0);
            row.Children.Add(Selector); Grid.SetColumn(Sync, 1); row.Children.Add(Sync); Grid.SetColumn(Link, 2); row.Children.Add(Link);
            panel.Children.Add(row); Status.Foreground = Muted; panel.Children.Add(Status);
            AutomationProperties.SetName(Selector, "Default Platform");
            AutomationProperties.SetName(Sync, "Sync this game's achievements on the default platform");
            AutomationProperties.SetLiveSetting(Status, AutomationLiveSetting.Polite);
            AccessibilityUi.TryApplyFocusVisualStyle(Selector);
        }
        internal static Brush Surface => SystemParameters.HighContrast ? SystemColors.WindowBrush : UiBrushHelper.FromHex("#111D24");
        internal static Brush Text => SystemParameters.HighContrast ? SystemColors.WindowTextBrush : UiBrushHelper.FromHex("#E0EBF0");
        internal static Brush Muted => SystemParameters.HighContrast ? SystemColors.WindowTextBrush : UiBrushHelper.FromHex("#9DB0BC");
        internal static Brush Line => SystemParameters.HighContrast ? SystemColors.WindowTextBrush : UiBrushHelper.FromHex("#2B3D48");
        internal static Button ActionButton(string label, double width, bool primary = false)
        {
            var button = new Button { Content = label, Width = width, Height = 38, Foreground = Text, FontSize = 13, FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand };
            if (!SystemParameters.HighContrast) button.Template = LibraryButtonChrome.BuildLibraryToolbarButtonTemplate(
                primary ? "#244A3C" : "#1A2B35", primary ? "#477A64" : "#344C5A", primary ? "#315F4C" : "#293F4C", "#15262E");
            AccessibilityUi.TryApplyFocusVisualStyle(button);
            return button;
        }
    }

    sealed class XboxProfileLinkWindow : Window
    {
        internal readonly TextBox Search = new() { Height = 38, Padding = new Thickness(10, 7, 10, 7) };
        internal readonly ListBox Games = new() { DisplayMemberPath = nameof(XboxAchievementService.XboxTitle.LinkDisplay), HorizontalContentAlignment = HorizontalAlignment.Stretch, BorderThickness = new Thickness(0) };
        internal readonly Button Link = GameProfilePlatformBar.ActionButton("Link selected game", 162, true);
        internal readonly Button ShowAll = GameProfilePlatformBar.ActionButton("Show all games", 128);
        internal XboxAchievementService.XboxTitle SelectedTitle { get; private set; }
        readonly List<XboxAchievementService.XboxTitle> titles;
        readonly TextBlock results = new() { FontSize = 12, Margin = new Thickness(0, 8, 0, 12), TextWrapping = TextWrapping.Wrap };

        internal XboxProfileLinkWindow(Window owner, string profile, IEnumerable<XboxAchievementService.XboxTitle> titles)
        {
            this.titles = titles.OrderBy(t => t.Name).ToList();
            Owner = owner; Title = "Link Xbox game"; Width = 660; Height = 590; MinWidth = 520; MinHeight = 420;
            WindowStartupLocation = owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
            TrackerFormStyle.Apply(this); Background = GameProfilePlatformBar.Surface; Foreground = GameProfilePlatformBar.Text;
            var panel = new Grid { Margin = new Thickness(24) }; Content = panel;
            panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition()); panel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var heading = new StackPanel(); panel.Children.Add(heading);
            heading.Children.Add(new TextBlock { Text = "Link to " + profile, FontSize = 22, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap });
            heading.Children.Add(new TextBlock { Text = "Choose the matching release. Originals and remakes can share a name; check the Xbox generation before linking.", Foreground = GameProfilePlatformBar.Muted, FontSize = 13, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 18) });
            heading.Children.Add(new TextBlock { Text = "Search your Xbox games", FontSize = 12, Foreground = GameProfilePlatformBar.Muted, Margin = new Thickness(0, 0, 0, 6) });
            var searchRow = new Grid(); searchRow.ColumnDefinitions.Add(new ColumnDefinition()); searchRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Search.Foreground = GameProfilePlatformBar.Text; Search.Background = GameProfilePlatformBar.Surface; Search.BorderBrush = GameProfilePlatformBar.Line;
            Search.Margin = new Thickness(0, 0, 10, 0); Search.Text = profile ?? "";
            searchRow.Children.Add(Search); Grid.SetColumn(ShowAll, 1); searchRow.Children.Add(ShowAll); heading.Children.Add(searchRow);
            results.Foreground = GameProfilePlatformBar.Muted; heading.Children.Add(results);
            Games.Background = GameProfilePlatformBar.Surface; Games.Foreground = GameProfilePlatformBar.Text;
            ScrollViewer.SetHorizontalScrollBarVisibility(Games, ScrollBarVisibility.Disabled);
            var itemStyle = new Style(typeof(ListBoxItem));
            itemStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(12, 10, 12, 10)));
            itemStyle.Setters.Add(new Setter(Control.HorizontalContentAlignmentProperty, HorizontalAlignment.Stretch));
            if (!SystemParameters.HighContrast)
            {
                var frame = new FrameworkElementFactory(typeof(Border), "Frame"); frame.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
                frame.SetValue(Border.BackgroundProperty, Brushes.Transparent); frame.SetBinding(Border.PaddingProperty, new Binding("Padding") { RelativeSource = RelativeSource.TemplatedParent });
                var content = new FrameworkElementFactory(typeof(ContentPresenter)); frame.AppendChild(content);
                var template = new ControlTemplate(typeof(ListBoxItem)) { VisualTree = frame };
                var hover = new Trigger { Property = IsMouseOverProperty, Value = true }; hover.Setters.Add(new Setter(Border.BackgroundProperty, UiBrushHelper.FromHex("#203640"), "Frame")); template.Triggers.Add(hover);
                var selected = new Trigger { Property = ListBoxItem.IsSelectedProperty, Value = true }; selected.Setters.Add(new Setter(Border.BackgroundProperty, UiBrushHelper.FromHex("#2A5145"), "Frame")); template.Triggers.Add(selected);
                itemStyle.Setters.Add(new Setter(Control.TemplateProperty, template));
            }
            Games.ItemContainerStyle = itemStyle; Grid.SetRow(Games, 1); panel.Children.Add(Games);
            var footer = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            var cancel = GameProfilePlatformBar.ActionButton("Cancel", 94); cancel.IsCancel = true; cancel.Margin = new Thickness(0, 0, 10, 0);
            Link.IsDefault = true; Link.IsEnabled = false; footer.Children.Add(cancel); footer.Children.Add(Link); Grid.SetRow(footer, 2); panel.Children.Add(footer);
            Search.TextChanged += (_, __) => Filter(); ShowAll.Click += (_, __) => Search.Clear();
            Games.SelectionChanged += (_, __) => Link.IsEnabled = Games.SelectedItem is XboxAchievementService.XboxTitle;
            Link.Click += (_, __) => { if (Games.SelectedItem is XboxAchievementService.XboxTitle title) { SelectedTitle = title; DialogResult = true; } };
            AutomationProperties.SetName(Search, "Search Xbox titles"); AutomationProperties.SetName(Games, "Choose the matching Xbox edition");
            AutomationProperties.SetLiveSetting(results, AutomationLiveSetting.Polite);
            Filter();
        }
        void Filter()
        {
            var matches = GameProfilePlatformStore.SearchXbox(titles, Search.Text).ToList();
            Games.ItemsSource = matches; Link.IsEnabled = false;
            results.Text = matches.Count == 0 ? "No matches. Try a shorter name or choose Show all games." : matches.Count + (matches.Count == 1 ? " matching game" : " matching games") + " · Select a title to link.";
        }
    }
}
