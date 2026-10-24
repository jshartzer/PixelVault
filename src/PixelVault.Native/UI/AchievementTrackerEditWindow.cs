using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;

namespace PixelVaultNative
{
    sealed class AchievementTrackerEditWindow : Window
    {
        internal AchievementTrackerDefinition Definition { get; private set; }
        internal List<AchievementTrackerProgress> InitialProgress { get; private set; }
        internal long InitialObservedUtc { get; private set; }
        internal string InitialFingerprint { get; private set; }
        internal AchievementTrackerEditWindow(Window owner, IEnumerable<AchievementHubGame> games, AchievementTrackerDefinition selected, string achievementId,
            IEnumerable<GameAchievementsFetchService.AchievementRow> achievements = null, bool spreadsheet = false)
        {
            if (owner?.IsVisible == true) Owner = owner;
            Title = spreadsheet ? "Import spreadsheet checklist" : "New achievement checklist"; Width = 680; Height = 760; MinWidth = 520; MinHeight = 500;
            WindowStartupLocation = Owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
            Background = UiBrushHelper.FromHex("#11181D");
            var root = new DockPanel { Margin = new Thickness(22) }; Content = root;
            var fields = new StackPanel();
            fields.Children.Add(AchievementTrackerWindow.Text(spreadsheet ? "Import spreadsheet" : "New checklist", 23, true));
            fields.Children.Add(AchievementTrackerWindow.Text("Game", 13));
            var game = new ComboBox { Margin = new Thickness(0, 5, 0, 14) };
            foreach (var option in games.OrderBy(g => g.Title)) game.Items.Add(new ComboBoxItem { Content = option.Title, Tag = option });
            game.SelectedItem = game.Items.OfType<ComboBoxItem>().FirstOrDefault(i => i.Tag is AchievementHubGame g && g.Provider == selected?.Provider && g.Id == selected?.GameId)
                ?? game.Items.OfType<ComboBoxItem>().FirstOrDefault();
            AutomationProperties.SetName(game, "Checklist game"); fields.Children.Add(game);
            fields.Children.Add(AchievementTrackerWindow.Text("Linked achievement (optional)", 13));
            var link = new TrackerAchievementPicker(achievements) { Margin = new Thickness(0, 5, 0, 14) };
            fields.Children.Add(link);
            void UpdateLink(string id = null)
            {
                var choice = (game.SelectedItem as ComboBoxItem)?.Tag as AchievementHubGame;
                link.SelectGame(choice?.Provider, choice?.Id, id);
            }
            // Only carry the originating achievement while its provider and game still match.
            var initial = (game.SelectedItem as ComboBoxItem)?.Tag as AchievementHubGame;
            UpdateLink(initial?.Provider == selected?.Provider && initial?.Id == selected?.GameId ? achievementId : null);
            game.SelectionChanged += (_, __) => UpdateLink();
            fields.Children.Add(AchievementTrackerWindow.Text("Checklist title", 13));
            var title = new TextBox { Margin = new Thickness(0, 5, 0, 14), MaxLength = 200, Padding = new Thickness(6) };
            AchievementTrackerWindow.DressEditor(title);
            AutomationProperties.SetName(title, "Checklist title"); fields.Children.Add(title);
            var footer = new StackPanel(); DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
            var error = AchievementTrackerWindow.Text("", 12); error.Margin = new Thickness(0, 8, 0, 8); footer.Children.Add(error);
            var buttons = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; footer.Children.Add(buttons);
            var cancel = AchievementTrackerWindow.Button("Cancel", "Cancel new checklist"); cancel.Click += (_, __) => { DialogResult = false; }; buttons.Children.Add(cancel);
            var save = AchievementTrackerWindow.Button("Create checklist", "Create the checklist"); buttons.Children.Add(save);
            var items = new TextBox { AcceptsReturn = true, Height = 210, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, TextWrapping = TextWrapping.Wrap, MaxLength = 500000, Margin = new Thickness(0, 5, 0, 0) };
            AchievementTrackerWindow.DressEditor(items);
            AutomationProperties.SetName(items, "Checklist items, one per line");
            TrackerSpreadsheetForm importForm = null;
            TrackerMilestoneEditor completion = null;
            if (spreadsheet) { importForm = new TrackerSpreadsheetForm(this, title); fields.Children.Add(importForm); }
            else { completion=new TrackerMilestoneEditor();fields.Children.Add(completion);fields.Children.Add(AchievementTrackerWindow.Text("Items — one per line", 14)); fields.Children.Add(items); }
            root.Children.Add(new ScrollViewer { Content = fields, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled });
            save.Click += (_, __) =>
            {
                var choice = (game.SelectedItem as ComboBoxItem)?.Tag as AchievementHubGame;
                var names = items.Text.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                if (choice == null || string.IsNullOrWhiteSpace(title.Text) || !spreadsheet && (names.Count == 0 || names.Count > 10000))
                { error.Text = "Choose a game, enter a title, and add at least one item."; return; }
                if (!spreadsheet && names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Count) { error.Text = "Give each item a distinct name."; return; }
                List<AchievementTrackerItem> imported = null;
                var milestones=new List<string>();
                if (spreadsheet)
                {
                    try { var draft=importForm.BuildDraft(); imported=draft.Items;milestones=draft.Milestones; InitialProgress=draft.Progress; InitialObservedUtc=importForm.ObservedUtc; InitialFingerprint=importForm.Fingerprint; }
                    catch (Exception ex) { error.Text = ex.Message; return; }
                }
                else
                {
                    try { milestones=completion.Labels;imported=TrackerMilestones.Expand(names.Select((name,index)=>new AchievementTrackerItem { Id=Guid.NewGuid().ToString("N"),Name=name,Category="Checklist",Order=index }),milestones); }
                    catch(Exception ex){error.Text=ex.Message;return;}
                }
                Definition = new AchievementTrackerDefinition
                {
                    Id = "custom." + Guid.NewGuid().ToString("N"), Provider = choice.Provider, GameId = choice.Id, GameTitle = choice.Title,
                    AchievementId = link.Achievement?.ProviderAchievementId, Title = title.Text.Trim(), Description = "Your checklist for " + choice.Title,
                    Items = imported,Milestones=milestones
                };
                DialogResult = true;
            };
            TrackerFormStyle.Apply(this);AchievementCollectionUi.ApplyHighContrast(this);
        }
    }
}
