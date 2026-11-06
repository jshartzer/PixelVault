using System;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using Microsoft.Win32;

namespace PixelVaultNative
{
    sealed class AchievementArtworkImportWindow : Window
    {
        readonly AchievementArtworkImport importer;
        readonly Action imported;
        readonly StackPanel inputs = new();
        readonly TextBox page = new(), file = new() { IsReadOnly = true };
        readonly ComboBox source = new();
        readonly TextBlock status = AchievementTrackerWindow.Text("Preview the matches before importing. Images are installed on this PC.", 14);
        readonly TextBox results = new() { IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        readonly ProgressBar progress = new() { Minimum = 0, Maximum = 100, Height = 14, Visibility = Visibility.Collapsed };
        readonly TextBlock progressText = AchievementTrackerWindow.Text("", 13);
        readonly Button preview = AchievementTrackerWindow.Button("Preview matches", "Find matching achievement artwork");
        readonly Button install = AchievementTrackerWindow.Button("Import matched images", "Download and install the accepted matches");
        readonly Button close = AchievementTrackerWindow.Button("Close", "Close the artwork importer");
        CancellationTokenSource cancellation;
        AchievementArtworkImport.Plan plan;
        bool busy, closed;
        int operation;
        internal Task Pending { get; private set; } = Task.CompletedTask;
        internal Func<string, string> ChooseFile;

        internal AchievementArtworkImportWindow(Window owner, string gameTitle, AchievementArtworkImport importer, Action onImported = null)
        {
            this.importer = importer; imported = onImported;
            if (owner?.IsVisible == true) Owner = owner;
            Title = "Import achievement artwork — " + gameTitle; Width = 760; Height = 700; MinWidth = 480; MinHeight = 540;
            WindowStartupLocation = Owner == null ? WindowStartupLocation.CenterScreen : WindowStartupLocation.CenterOwner;
            Background = SystemParameters.HighContrast ? SystemColors.WindowBrush : AchievementTrackerWindow.Color("#11181D");
            var root = new DockPanel { Margin = new Thickness(22) }; Content = root;
            var footer = new WrapPanel { Margin = new Thickness(0, 14, 0, 0) }; DockPanel.SetDock(footer, Dock.Bottom); root.Children.Add(footer);
            footer.Children.Add(preview); footer.Children.Add(install); footer.Children.Add(close); install.IsEnabled = false;
            var header = new StackPanel(); DockPanel.SetDock(header, Dock.Top); root.Children.Add(header);
            header.Children.Add(AchievementTrackerWindow.Text(gameTitle, 23, true));
            header.Children.Add(AchievementTrackerWindow.Text("Import higher-resolution images and fill missing descriptions from Exophase", 17));
            header.Children.Add(inputs);
            Label("Trophy page URL"); page.Text = importer.DefaultPage(); page.Margin = new Thickness(0, 4, 0, 4); inputs.Children.Add(page);
            AutomationProperties.SetName(page, "Trophy page URL"); AchievementTrackerWindow.DressEditor(page);
            var links = new WrapPanel(); inputs.Children.Add(links);
            var search = AchievementTrackerWindow.Button("Find trophy page", "Search for this game's Exophase trophy list"); search.MinWidth = 0; links.Children.Add(search);
            var open = AchievementTrackerWindow.Button("Open source page", "Open the entered trophy page in your browser"); open.MinWidth = 0; links.Children.Add(open);
            search.Click += (_, __) => Open("https://www.google.com/search?q=" + Uri.EscapeDataString("site:exophase.com/game/ " + gameTitle + " trophies"));
            open.Click += (_, __) => { if (AchievementArtworkImport.ValidPage(page.Text.Trim())) Open(page.Text.Trim()); else status.Text = "Enter a valid Exophase trophy list URL first."; };
            Label("Source");
            foreach (var choice in new[] { ("page", "Public trophy page"), ("html", "Saved HTML page"), ("mapping", "Reviewed mapping file") })
                source.Items.Add(new ComboBoxItem { Tag = choice.Item1, Content = choice.Item2 });
            if (importer.HasKnownArtwork) source.Items.Add(new ComboBoxItem { Tag = "known", Content = "Bundled reviewed artwork" });
            source.SelectedIndex = 0; source.Margin = new Thickness(0, 4, 0, 8); inputs.Children.Add(source); AutomationProperties.SetName(source, "Artwork source");
            var fileRow = new DockPanel(); inputs.Children.Add(fileRow);
            var browse = AchievementTrackerWindow.Button("Choose file…", "Select saved HTML or a reviewed mapping"); browse.MinWidth = 0; DockPanel.SetDock(browse, Dock.Right); fileRow.Children.Add(browse); fileRow.Children.Add(file);
            file.MinHeight = 32; AutomationProperties.SetName(file, "Saved source file"); AchievementTrackerWindow.DressEditor(file);
            void UpdateFile() { fileRow.Visibility = Kind is "html" or "mapping" ? Visibility.Visible : Visibility.Collapsed; }
            UpdateFile();
            ChooseFile = kind =>
            {
                var dialog = new OpenFileDialog { Title = kind == "html" ? "Choose saved trophy page" : "Choose reviewed artwork mapping",
                    Filter = kind == "html" ? "HTML page (*.html;*.htm)|*.html;*.htm|All files|*.*" : "JSON mapping (*.json)|*.json", CheckFileExists = true };
                return dialog.ShowDialog(this) == true ? dialog.FileName : null;
            };
            browse.Click += (_, __) => { var selected = ChooseFile(Kind); if (selected != null) { file.Text = selected; Invalidate(); } };
            source.SelectionChanged += (_, __) => { UpdateFile(); Invalidate(); }; page.TextChanged += (_, __) => Invalidate();
            status.Margin = new Thickness(0, 14, 0, 10); status.TextWrapping = TextWrapping.Wrap; header.Children.Add(status);
            header.Children.Add(progress);
            progressText.Margin = new Thickness(0, 5, 0, 10); progressText.TextWrapping = TextWrapping.Wrap; progressText.MaxHeight = 44;
            progressText.Visibility = Visibility.Collapsed; header.Children.Add(progressText);
            AutomationProperties.SetName(progress, "Achievement artwork import progress");
            importer.ProgressChanged += OnProgress;
            results.Padding = new Thickness(10); results.FontSize = 14; AchievementTrackerWindow.DressEditor(results); root.Children.Add(results);
            AutomationProperties.SetName(results, "Artwork import results");
            preview.Click += (_, __) => Pending = Preview(); install.Click += (_, __) => Pending = Import();
            close.Click += (_, __) => { if (busy) cancellation?.Cancel(); else Close(); };
            Closing += (_, __) => { closed = true; cancellation?.Cancel(); importer.ProgressChanged -= OnProgress; };
            PreviewKeyDown += (_, e) => { if (e.Key == System.Windows.Input.Key.Escape) { e.Handled = true; if (busy) cancellation?.Cancel(); else Close(); } };
            TrackerFormStyle.Apply(this); AchievementCollectionUi.ApplyHighContrast(this);
        }
        string Kind => (source.SelectedItem as ComboBoxItem)?.Tag as string ?? "page";
        void Label(string text) { var label = AchievementTrackerWindow.Text(text, 14, true); label.Margin = new Thickness(0, 12, 0, 0); inputs.Children.Add(label); }
        void Open(string url) { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch (Exception ex) { status.Text = "Could not open the browser: " + ex.Message; } }
        void Invalidate() { if (busy) return; plan = null; install.IsEnabled = false; results.Clear(); status.Text = "Preview the selected source before importing. Images are installed on this PC."; }
        void Busy(bool value)
        {
            Interlocked.Increment(ref operation);
            busy = value; inputs.IsEnabled = !value; preview.IsEnabled = !value; install.IsEnabled = !value && plan?.Report.Matched.Length > 0;
            close.Content = value ? "Stop import" : "Close";
            if (value)
            {
                cancellation?.Dispose(); cancellation = new();
                progress.Visibility = progressText.Visibility = Visibility.Visible; progress.Value = 0; progress.IsIndeterminate = true; progressText.Text = "Starting…";
            }
            else progress.IsIndeterminate = false;
        }
        void OnProgress(AchievementArtworkImport.ProgressUpdate update)
        {
            var current = Volatile.Read(ref operation);
            void Apply()
            {
                if (closed || !busy || current != operation) return;
                progress.IsIndeterminate = update.Total == 0 && update.Stage != "install";
                if (update.Stage == "install") progress.Value = 95;
                else if (update.Total > 0) progress.Value = 90d * update.Completed / update.Total;
                if (!string.IsNullOrWhiteSpace(update.Message)) status.Text = update.Message;
                progressText.Text = update.Total > 0 ? $"{update.Completed} / {update.Total} processed" + (string.IsNullOrWhiteSpace(update.Title) ? "" : " — " + update.Title) : "Working…";
                progressText.ToolTip = update.Title;
            }
            if (Dispatcher.CheckAccess()) Apply(); else Dispatcher.BeginInvoke(new Action(Apply));
        }
        void ProgressFinished(string text, bool complete = false)
        {
            progress.IsIndeterminate = false; if (complete) progress.Value = 100; progressText.Text = text;
        }
        async Task Preview()
        {
            if (busy) return;
            plan = null; Busy(true); status.Text = "Checking achievement matches…";
            try
            {
                plan = await importer.Preview(page.Text, Kind, file.Text, cancellation.Token);
                if (closed) return;
                status.Text = $"{plan.Report.Matched.Length} matched · {plan.Report.Review.Length} need review";
                results.Text = Describe(plan.Report, false);
                ProgressFinished("Preview complete", true);
            }
            catch (OperationCanceledException) { if (!closed) { status.Text = "Import stopped. Installed artwork is unchanged."; ProgressFinished("Stopped"); } }
            catch (Exception ex) { if (!closed) { status.Text = ex.Message; ProgressFinished("Preview failed"); } }
            finally { if (!closed) Busy(false); }
        }
        async Task Import()
        {
            if (busy || plan == null) return;
            Busy(true); status.Text = "Downloading and checking higher-resolution images…";
            try
            {
                var report = await importer.Import(plan, cancellation.Token);
                if (closed) return;
                status.Text = $"{report.Imported.Length} imported · {report.Failed.Length} failed · {plan.Report.Review.Length} left for review";
                results.Text = Describe(report, true);
                ProgressFinished("Import complete", true);
                if (report.Imported.Length > 0) imported?.Invoke();
                plan = null;
            }
            catch (OperationCanceledException) { if (!closed) { status.Text = "Import stopped. Installed artwork is unchanged."; ProgressFinished("Stopped"); } }
            catch (Exception ex) { if (!closed) { status.Text = ex.Message; ProgressFinished("Import failed"); } }
            finally { if (!closed) Busy(false); }
        }
        static string Value(JsonElement entry, string key) => entry.TryGetProperty(key, out var value) ? value.GetString() : "";
        static string Describe(AchievementArtworkImport.Report report, bool applied)
        {
            var accepted = applied ? report.Imported : report.Matched;
            var lines = accepted.Select(e => (applied ? "Imported: " : "Matched: ") + Value(e, "title"))
                .Concat(report.Review.Select(e => "Review: " + Value(e, "title") + " — " + Value(e, "reason")))
                .Concat(report.Failed.Select(e => "Failed: " + Value(e, "title") + " — " + Value(e, "error")));
            return string.Join(Environment.NewLine + Environment.NewLine, lines);
        }
    }
}
