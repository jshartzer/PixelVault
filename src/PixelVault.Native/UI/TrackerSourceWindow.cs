using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using Microsoft.Win32;

namespace PixelVaultNative
{
    sealed class TrackerSourceWindow : Window
    {
        internal string SourcePath { get; private set; }
        internal string Profile { get; private set; }
        internal string AdapterId { get; private set; }
        internal string GameDirectory { get; private set; }
        internal string SpecificSave { get; private set; }
        internal bool KeepSnapshots { get; private set; }
        internal bool Remove { get; private set; }
        internal TrackerSpreadsheetMapping Spreadsheet { get; private set; }
        internal TrackerSourceWindow(Window owner, TrackerSourceBinding binding, TrackerSourceStatus status, AchievementTrackerDefinition definition = null, string account = null)
        {
            if (owner?.IsVisible == true) Owner = owner;
            Title = "Tracker source"; Width = 700; Height = 760; MinWidth = 480; MinHeight = 390;
            WindowStartupLocation = Owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
            Background = AchievementTrackerWindow.Color("#11181D");
            var root = new DockPanel { Margin = new Thickness(22), Background = Background }; Content = root;
            var footer = new WrapPanel { HorizontalAlignment = HorizontalAlignment.Right }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
            var body = new StackPanel(); root.Children.Add(new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            body.Children.Add(AchievementTrackerWindow.Text("Tracker data source", 23, true));
            var adapter = new ComboBox { Margin = new Thickness(0, 10, 0, 10) };
            adapter.Items.Add(new ComboBoxItem { Content = "Progress JSON file", Tag = JsonFileTrackerSource.AdapterId });
            if (definition != null) adapter.Items.Add(new ComboBoxItem { Content = "Excel workbook / CSV progress", Tag = SpreadsheetTrackerSource.AdapterId });
            bool supportsGame = definition?.Id == AchievementTrackingCatalog.DeathStrandingId || DeathStrandingWorldReader.Supports(definition?.Id) || DeathStrandingOrderReader.Supports(definition?.Id) || DeathStrandingChipReader.Supports(definition?.Id) || binding?.AdapterId == DeathStrandingTrackerSource.AdapterId;
            if (supportsGame) adapter.Items.Add(new ComboBoxItem { Content = "Death Stranding Director's Cut save", Tag = DeathStrandingTrackerSource.AdapterId });
            adapter.SelectedItem = adapter.Items.OfType<ComboBoxItem>().FirstOrDefault(i => (string)i.Tag == (binding?.AdapterId ?? (supportsGame ? DeathStrandingTrackerSource.AdapterId : JsonFileTrackerSource.AdapterId))) ?? adapter.Items[0];
            AutomationProperties.SetName(adapter, "Tracker source type"); body.Children.Add(adapter);
            var explanation = AchievementTrackerWindow.Text("", 13); body.Children.Add(explanation);
            var locationLabel = AchievementTrackerWindow.Text("", 13); locationLabel.Margin = new Thickness(0, 12, 0, 0); body.Children.Add(locationLabel);
            TextBox Editor(string text, string name)
            {
                var editor = new TextBox { Text = text ?? "", Margin = new Thickness(0, 6, 0, 6), Padding = new Thickness(6), MaxLength = 4000 };
                AchievementTrackerWindow.DressEditor(editor); AutomationProperties.SetName(editor, name); return editor;
            }
            var path = Editor(binding?.Location, "Tracker source location"); body.Children.Add(path);
            var browse = AchievementTrackerWindow.Button("Browse…", "Choose the tracker source location"); body.Children.Add(browse);
            var gameFields = new StackPanel(); body.Children.Add(gameFields);
            var spreadsheetForm = definition == null ? null : new TrackerSpreadsheetForm(this,new TextBox(),definition);
            if (spreadsheetForm != null) body.Children.Add(spreadsheetForm);
            gameFields.Children.Add(AchievementTrackerWindow.Text("Game installation folder (contains ds.exe)", 13));
            var game = Editor(binding?.GameDirectory ?? (supportsGame ? DeathStrandingTrackerSource.DiscoverGameDirectory() : ""), "Death Stranding game installation"); gameFields.Children.Add(game);
            void Folder(TextBox field) { var picker = new OpenFolderDialog { Title = "Choose folder", Multiselect = false }; if (picker.ShowDialog(this) == true) field.Text = picker.FolderName; }
            var browseGame = AchievementTrackerWindow.Button("Browse game folder…", "Choose the installed Director's Cut folder"); browseGame.Click += (_, __) => Folder(game); gameFields.Children.Add(browseGame);
            gameFields.Children.Add(AchievementTrackerWindow.Text("Specific checkpoint (optional; blank selects newest valid save)", 13));
            var specific = Editor(binding?.SpecificSave, "Specific Death Stranding checkpoint override"); gameFields.Children.Add(specific);
            var saveButtons = new WrapPanel(); gameFields.Children.Add(saveButtons);
            var browseSave = AchievementTrackerWindow.Button("Choose checkpoint…", "Select a checkpoint.dat within the account's save folder"); saveButtons.Children.Add(browseSave);
            browseSave.Click += (_, __) => { var picker = new OpenFileDialog { Filter = "Death Stranding checkpoint (checkpoint.dat)|checkpoint.dat", CheckFileExists = true }; if (picker.ShowDialog(this) == true) specific.Text = picker.FileName; };
            var clear = AchievementTrackerWindow.Button("Use newest save", "Clear the specific-save override"); clear.Click += (_, __) => specific.Text = ""; saveButtons.Children.Add(clear);
            var keep = new CheckBox { Content = "Keep the latest 3 private diagnostic snapshots", IsChecked = binding?.KeepSnapshots ?? true, Foreground = AchievementTrackerWindow.Color("#D9E6ED"), Margin = new Thickness(0, 6, 0, 12) };
            AutomationProperties.SetName(keep, "Retain up to three copied checkpoints for diagnostics"); gameFields.Children.Add(keep);
            body.Children.Add(AchievementTrackerWindow.Text("Profile label (optional)", 13));
            var profile = Editor(binding?.Profile, "Source profile label"); profile.MaxLength = 200; body.Children.Add(profile);
            body.Children.Add(AchievementTrackerWindow.Text(Describe(binding, status), 12));
            var error = AchievementTrackerWindow.Text("", 12); error.Margin = new Thickness(0, 12, 0, 12); body.Children.Add(error);
            string jsonPath = binding?.AdapterId == JsonFileTrackerSource.AdapterId ? binding.Location : "";
            string saveRoot = binding?.AdapterId == DeathStrandingTrackerSource.AdapterId ? binding.Location
                : supportsGame && DeathStrandingTrackerSource.IsSteamAccount(account) ? DeathStrandingTrackerSource.DefaultSaveRoot(account) : "";
            string previous = null;
            void Mode()
            {
                if (previous == JsonFileTrackerSource.AdapterId) jsonPath = path.Text;
                if (previous == DeathStrandingTrackerSource.AdapterId) saveRoot = path.Text;
                var id = (string)((ComboBoxItem)adapter.SelectedItem).Tag; bool ds = id == DeathStrandingTrackerSource.AdapterId;
                bool spreadsheet = id == SpreadsheetTrackerSource.AdapterId;
                path.Text = ds ? saveRoot : jsonPath; previous = id;
                explanation.Text = ds ? "Refresh reads a private copy of the newest valid checkpoint for your Steam account. Nothing is written to your game saves. Review differences before applying. Uses the compression library in your game installation."
                    : spreadsheet ? "Match spreadsheet progress using stable item IDs. Missing rows stay unchanged. Unknown IDs block import; item metadata is not replaced. Save source, then Refresh to review."
                    : "Refresh rereads an exported checklist progress JSON file and previews differences. Choose a JSON observation file here.";
                locationLabel.Text = ds ? "Save folder for this Steam account" : spreadsheet ? "Workbook and saved column mapping" : "Progress JSON file";
                gameFields.Visibility = ds ? Visibility.Visible : Visibility.Collapsed;
                path.Visibility = browse.Visibility = spreadsheet ? Visibility.Collapsed : Visibility.Visible;
                if (spreadsheetForm != null) spreadsheetForm.Visibility = spreadsheet ? Visibility.Visible : Visibility.Collapsed;
            }
            adapter.SelectionChanged += (_, __) => Mode(); Mode();
            Loaded += async (_, __) =>
            {
                if (binding?.AdapterId != SpreadsheetTrackerSource.AdapterId || spreadsheetForm == null) return;
                try { await spreadsheetForm.LoadFileAsync(binding.Location,binding.Spreadsheet); }
                catch(Exception ex) { error.Text=ex.Message; }
            };
            browse.Click += (_, __) =>
            {
                if (previous == DeathStrandingTrackerSource.AdapterId) Folder(path);
                else { var picker = new OpenFileDialog { Filter = "Checklist progress (*.json)|*.json", CheckFileExists = true }; if (picker.ShowDialog(this) == true) path.Text = picker.FileName; }
            };
            var remove = AchievementTrackerWindow.Button("Disconnect source", "Return to manual-only use without deleting progress"); remove.IsEnabled = binding != null; footer.Children.Add(remove);
            remove.Click += (_, __) => { Remove = true; DialogResult = true; };
            var cancel = AchievementTrackerWindow.Button("Cancel", "Cancel source configuration"); cancel.Click += (_, __) => DialogResult = false; footer.Children.Add(cancel);
            var save = AchievementTrackerWindow.Button("Save source", "Save the source without importing observations"); footer.Children.Add(save);
            save.Click += (_, __) =>
            {
                if (previous == SpreadsheetTrackerSource.AdapterId)
                {
                    try
                    {
                        var map=spreadsheetForm.BuildMapping();_ = spreadsheetForm.BuildDraft();
                        var candidate=new TrackerSourceBinding { AdapterId=previous,AdapterVersion="1",Location=spreadsheetForm.FilePath,Spreadsheet=map };
                        new SpreadsheetTrackerSource().Validate(candidate,definition);
                        SourcePath=candidate.Location;Spreadsheet=map;Profile=profile.Text.Trim();AdapterId=previous;DialogResult=true;
                    }
                    catch(Exception ex) { error.Text=ex.Message; }
                    return;
                }
                bool ds = previous == DeathStrandingTrackerSource.AdapterId;
                if (!Path.IsPathFullyQualified(path.Text.Trim()) || !ds && !Path.GetExtension(path.Text.Trim()).Equals(".json", StringComparison.OrdinalIgnoreCase)
                    || ds && (!Path.IsPathFullyQualified(game.Text.Trim()) || !string.IsNullOrWhiteSpace(specific.Text) && !Path.IsPathFullyQualified(specific.Text.Trim())))
                { error.Text = ds ? "Choose full paths for the save folder, installation and optional checkpoint." : "Enter the full path to a progress JSON file."; return; }
                SourcePath = path.Text.Trim(); Profile = profile.Text.Trim(); AdapterId = previous; GameDirectory = ds ? game.Text.Trim() : null;
                SpecificSave = ds ? specific.Text.Trim() : null; KeepSnapshots = ds && keep.IsChecked == true; DialogResult = true;
            };
            TrackerFormStyle.Apply(this);AchievementCollectionUi.ApplyHighContrast(this);
        }
        internal static string Time(long ticks) => ticks > 0 ? new DateTime(ticks, DateTimeKind.Utc).ToLocalTime().ToString("g", CultureInfo.CurrentCulture) : "Never";
        internal static string Describe(TrackerSourceBinding binding, TrackerSourceStatus status) => binding == null
            ? "No source configured. Manual progress and notes remain available."
            : "Source: " + binding.Location + "\nProfile: " + (string.IsNullOrWhiteSpace(binding.Profile) ? "Default" : binding.Profile)
                + (binding.AdapterId == DeathStrandingTrackerSource.AdapterId ? "\nGame: " + binding.GameDirectory + "\nCheckpoint: " + (string.IsNullOrWhiteSpace(binding.SpecificSave) ? "Newest valid" : binding.SpecificSave) : "")
                + "\nAdapter: " + binding.AdapterId + " v" + binding.AdapterVersion + "\nLast attempt: " + Time(status?.LastAttemptUtc ?? 0)
                + "\nLast success: " + Time(status?.LastSuccessUtc ?? 0) + "\nSource observation: " + Time(status?.SourceObservedUtc ?? 0)
                + "\nResult: " + (status?.Outcome.ToString() ?? "None") + " · " + status?.Message
                + (string.IsNullOrEmpty(status?.Fingerprint) ? "" : "\nFingerprint: " + status.Fingerprint);
    }
}
