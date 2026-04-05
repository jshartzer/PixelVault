using System;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace PixelVaultNative
{
    /// <summary>Callbacks supplied by <see cref="MainWindow"/> for the intake preview modal (Phase C1).</summary>
    sealed class IntakePreviewServices
    {
        public Action<bool, CancellationToken, Action<IntakePreviewSummary>, Action<Exception>> LoadSummaryAsync { get; set; }
        public Action OpenSourceFolders { get; set; }
        public Action OpenManualIntake { get; set; }
        public Action<IntakePreviewSummary> SyncSettingsDocument { get; set; }
        public Action<string> SyncSettingsDocumentError { get; set; }
        public Action<string> SetStatus { get; set; }
        public Action<string> Log { get; set; }
        public Action<IntakePreviewSummary> LogSummary { get; set; }
        public Func<string, RoutedEventHandler, string, Brush, Button> CreateButton { get; set; }
        public Func<string, Brush> PreviewBadge { get; set; }
        public Func<string, int> PlatformOrder { get; set; }
        public Func<DateTime, string> FormatTimestamp { get; set; }
        public Func<string, string> FilenameGuess { get; set; }
        public BitmapSource IntakeReviewQueueBitmap { get; set; }
    }

    /// <summary>Modal upload-queue preview (extracted from MainWindow, Phase C1).</summary>
    static class IntakePreviewWindow
    {
        static SolidColorBrush B(string hex) => UiBrushHelper.FromHex(hex);

        static FrameworkElement BuildGamepadGlyph(Brush stroke, double strokeThickness, double width, double height)
        {
            var art = new System.Windows.Controls.Canvas { Width = 108, Height = 48 };
            art.Children.Add(new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M 12 42 C 8 42 5 39 4 33 C 1 23 6 15 12 10 C 17 6 25 4 34 4 L 41 4 C 42 4 43 5 44 6 C 45 7 46 8 48 8 L 60 8 C 62 8 63 7 64 6 C 65 5 66 4 67 4 L 74 4 C 83 4 91 6 96 10 C 102 15 107 23 104 33 C 103 39 100 42 96 42 C 90 42 84 39 78 32 L 69 22 C 66 19 64 18 60 18 L 48 18 C 44 18 42 19 39 22 L 30 32 C 24 39 18 42 12 42 Z"),
                Stroke = stroke,
                StrokeThickness = strokeThickness,
                Fill = Brushes.Transparent,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            });
            art.Children.Add(new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M 28 40 L 40 28 C 44 24 47 22 52 22 L 56 22 C 61 22 64 24 68 28 L 80 40"),
                Stroke = stroke,
                StrokeThickness = strokeThickness,
                Fill = Brushes.Transparent,
                StrokeLineJoin = PenLineJoin.Round,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round
            });
            return new Viewbox
            {
                Width = width,
                Height = height,
                Stretch = Stretch.Uniform,
                Child = art
            };
        }

        static Border BuildIntakeMetricCard(string label, string value, string detail, string backgroundHex, string borderHex, string valueHex)
        {
            var stack = new StackPanel();
            stack.Children.Add(new TextBlock { Text = label, Foreground = B("#A7B5BD"), FontSize = 12, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 8) });
            stack.Children.Add(new TextBlock { Text = value, Foreground = B(valueHex), FontSize = 28, FontWeight = FontWeights.SemiBold });
            if (!string.IsNullOrWhiteSpace(detail))
            {
                stack.Children.Add(new TextBlock { Text = detail, Foreground = B("#C9D4DB"), FontSize = 12, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap });
            }
            return new Border
            {
                Background = B(backgroundHex),
                BorderBrush = B(borderHex),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(18),
                Margin = new Thickness(0, 0, 14, 0),
                Child = stack
            };
        }

        public static void Show(Window owner, string appVersion, bool recurseRename, IntakePreviewServices services)
        {
            if (owner == null) throw new ArgumentNullException(nameof(owner));
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (services.LoadSummaryAsync == null || services.CreateButton == null || services.PreviewBadge == null
                || services.PlatformOrder == null || services.FormatTimestamp == null || services.FilenameGuess == null)
            {
                throw new InvalidOperationException("IntakePreviewServices requires LoadSummaryAsync, CreateButton, PreviewBadge, PlatformOrder, FormatTimestamp, and FilenameGuess.");
            }

            Window previewWindow = null;
            TextBlock headerMeta = null;
            Grid statsGrid = null;
            StackPanel autoReadyPanel = null;
            StackPanel sidePanel = null;
            Button manualButton = null;
            Button refreshButton = null;
            Action renderWindow = null;
            int previewWindowRefreshVersion = 0;
            CancellationTokenSource previewWindowRefreshCancellation = null;

            Button Btn(string t, RoutedEventHandler click, string bg, Brush fg) => services.CreateButton(t, click, bg, fg);

            try
            {
                previewWindow = new Window
                {
                    Title = "PixelVault " + appVersion + " Intake Preview",
                    Width = 1400,
                    Height = 920,
                    MinWidth = 1160,
                    MinHeight = 760,
                    Owner = owner,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Background = B("#081015")
                };

                var root = new Grid { Margin = new Thickness(20) };
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

                var header = new Border
                {
                    Background = new LinearGradientBrush((Color)ColorConverter.ConvertFromString("#16222A"), (Color)ColorConverter.ConvertFromString("#0D161D"), new Point(0, 0), new Point(1, 1)),
                    BorderBrush = B("#2D3E48"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(24),
                    Padding = new Thickness(24),
                    Effect = new DropShadowEffect { Color = Color.FromArgb(48, 5, 10, 14), BlurRadius = 22, ShadowDepth = 6, Direction = 270, Opacity = 0.55 }
                };
                var headerGrid = new Grid();
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                FrameworkElement headerGlyph = services.IntakeReviewQueueBitmap != null
                    ? new Image
                    {
                        Source = services.IntakeReviewQueueBitmap,
                        Stretch = Stretch.Uniform,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                    : BuildGamepadGlyph(B("#F5F7FA"), 2.2, 42, 28);
                if (headerGlyph is Image hi) RenderOptions.SetBitmapScalingMode(hi, BitmapScalingMode.HighQuality);
                var iconShell = new Border
                {
                    Width = 74,
                    Height = 74,
                    Background = B("#0E171C"),
                    BorderBrush = B("#344851"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(22),
                    Padding = new Thickness(14),
                    Margin = new Thickness(0, 0, 18, 0),
                    Child = headerGlyph
                };
                headerGrid.Children.Add(iconShell);
                var headerStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                headerStack.Children.Add(new TextBlock { Text = "Upload queue preview", FontSize = 30, FontWeight = FontWeights.SemiBold, Foreground = B("#F5EFE4") });
                headerMeta = new TextBlock { Foreground = B("#AAB9C2"), FontSize = 14, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap };
                headerStack.Children.Add(headerMeta);
                Grid.SetColumn(headerStack, 1);
                headerGrid.Children.Add(headerStack);
                var actionRow = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
                var openSourcesButton = Btn("Open Uploads", services.OpenSourceFolders != null ? (RoutedEventHandler)delegate { services.OpenSourceFolders(); } : null, "#20343A", Brushes.White);
                openSourcesButton.Width = 152;
                openSourcesButton.Height = 42;
                openSourcesButton.Margin = new Thickness(12, 0, 0, 0);
                refreshButton = Btn("Refresh", null, "#275D47", Brushes.White);
                refreshButton.Width = 128;
                refreshButton.Height = 42;
                refreshButton.Margin = new Thickness(12, 0, 0, 0);
                manualButton = Btn("Manual Intake", null, "#7C5A34", Brushes.White);
                manualButton.Width = 154;
                manualButton.Height = 42;
                manualButton.Margin = new Thickness(12, 0, 0, 0);
                actionRow.Children.Add(openSourcesButton);
                actionRow.Children.Add(refreshButton);
                actionRow.Children.Add(manualButton);
                Grid.SetColumn(actionRow, 2);
                headerGrid.Children.Add(actionRow);
                header.Child = headerGrid;
                root.Children.Add(header);

                statsGrid = new Grid { Margin = new Thickness(0, 16, 0, 16) };
                for (int i = 0; i < 4; i++) statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Grid.SetRow(statsGrid, 1);
                root.Children.Add(statsGrid);

                var body = new Grid();
                body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.45, GridUnitType.Star) });
                body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Grid.SetRow(body, 2);
                root.Children.Add(body);

                var autoReadyCard = new Border { Background = B("#10181D"), BorderBrush = B("#24333D"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(22), Padding = new Thickness(18), Margin = new Thickness(0, 0, 16, 0) };
                var autoReadyGrid = new Grid();
                autoReadyGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                autoReadyGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
                autoReadyGrid.Children.Add(new TextBlock { Text = "Ready by console", FontSize = 19, FontWeight = FontWeights.SemiBold, Foreground = B("#F5EFE4"), Margin = new Thickness(0, 0, 0, 12) });
                var autoReadyScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                autoReadyPanel = new StackPanel();
                autoReadyScroll.Content = autoReadyPanel;
                Grid.SetRow(autoReadyScroll, 1);
                autoReadyGrid.Children.Add(autoReadyScroll);
                autoReadyCard.Child = autoReadyGrid;
                body.Children.Add(autoReadyCard);

                var sideScroll = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                Grid.SetColumn(sideScroll, 1);
                sidePanel = new StackPanel();
                sideScroll.Content = sidePanel;
                body.Children.Add(sideScroll);
                previewWindow.Content = root;

                renderWindow = delegate
                {
                    var refreshVersion = ++previewWindowRefreshVersion;
                    if (previewWindowRefreshCancellation != null)
                    {
                        previewWindowRefreshCancellation.Cancel();
                        previewWindowRefreshCancellation.Dispose();
                    }
                    previewWindowRefreshCancellation = new CancellationTokenSource();
                    var refreshCancellationToken = previewWindowRefreshCancellation.Token;
                    headerMeta.Text = "Refreshing the upload queue snapshot...";
                    statsGrid.Children.Clear();
                    for (int i = 0; i < 4; i++)
                    {
                        var loadingCard = BuildIntakeMetricCard(i == 0 ? "Queue" : (i == 1 ? "Auto-ready" : (i == 2 ? "Manual" : "Conflicts")), "...", "Refreshing preview data.", "#111B21", "#263842", "#F5EFE4");
                        if (i == 3) loadingCard.Margin = new Thickness(0);
                        Grid.SetColumn(loadingCard, i);
                        statsGrid.Children.Add(loadingCard);
                    }
                    autoReadyPanel.Children.Clear();
                    autoReadyPanel.Children.Add(new Border
                    {
                        Background = B("#121E24"),
                        BorderBrush = B("#243741"),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(18),
                        Padding = new Thickness(20),
                        Child = new TextBlock { Text = "Refreshing queue snapshot...", Foreground = B("#A7B5BD"), TextWrapping = TextWrapping.Wrap }
                    });
                    sidePanel.Children.Clear();
                    sidePanel.Children.Add(new Border
                    {
                        Background = B("#11181D"),
                        BorderBrush = B("#243742"),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(22),
                        Padding = new Thickness(18),
                        Child = new TextBlock { Text = "Gathering current upload folders, auto-ready items, and manual-intake candidates...", Foreground = B("#A7B5BD"), TextWrapping = TextWrapping.Wrap }
                    });
                    if (refreshButton != null) refreshButton.IsEnabled = false;
                    if (manualButton != null) manualButton.IsEnabled = false;
                    if (services.SetStatus != null) services.SetStatus("Refreshing preview");

                    services.LoadSummaryAsync(recurseRename, refreshCancellationToken, delegate(IntakePreviewSummary summary)
                    {
                        if (previewWindow == null || !previewWindow.IsVisible) return;
                        if (refreshVersion != previewWindowRefreshVersion) return;
                        if (services.SyncSettingsDocument != null) services.SyncSettingsDocument(summary);
                        if (services.SetStatus != null) services.SetStatus("Preview ready");
                        if (services.LogSummary != null) services.LogSummary(summary);

                        headerMeta.Text = summary.TopLevelMediaCount == 0
                            ? "No media files are waiting in the upload queue right now."
                            : summary.TopLevelMediaCount + " item(s) are waiting across " + summary.SourceRoots.Count + " upload folder(s). Automatic matches stay grouped by console, and anything unmatched stays in Manual Intake.";

                        statsGrid.Children.Clear();
                        var statCards = new[]
                        {
                            BuildIntakeMetricCard("Queue", summary.TopLevelMediaCount.ToString(), "Top-level media items currently waiting.", "#111B21", "#263842", "#F5EFE4"),
                            BuildIntakeMetricCard("Auto-ready", summary.MoveCandidateCount.ToString(), "Files that can move straight through metadata and import.", "#101923", "#244153", "#7DD3FC"),
                            BuildIntakeMetricCard("Manual", summary.ManualItemCount.ToString(), "Items that still need console or game context.", "#201912", "#5A3E24", "#F6C47A"),
                            BuildIntakeMetricCard("Conflicts", summary.ConflictCount.ToString(), "Destination filename collisions if moved right now.", "#1B1518", "#4A2A34", "#F88CA2")
                        };
                        for (int i = 0; i < statCards.Length; i++)
                        {
                            if (i == statCards.Length - 1) statCards[i].Margin = new Thickness(0);
                            Grid.SetColumn(statCards[i], i);
                            statsGrid.Children.Add(statCards[i]);
                        }

                        autoReadyPanel.Children.Clear();
                        if (summary.ReviewItems.Count == 0)
                        {
                            autoReadyPanel.Children.Add(new Border
                            {
                                Background = B("#121E24"),
                                BorderBrush = B("#243741"),
                                BorderThickness = new Thickness(1),
                                CornerRadius = new CornerRadius(18),
                                Padding = new Thickness(20),
                                Child = new TextBlock { Text = "No auto-ready captures are waiting. New uploads will show up here grouped by console.", Foreground = B("#A7B5BD"), TextWrapping = TextWrapping.Wrap }
                            });
                        }
                        else
                        {
                            var groupedReviewItems = summary.ReviewItems.GroupBy(item => item.PlatformLabel).OrderBy(group => services.PlatformOrder(group.Key)).ThenBy(group => group.Key);
                            foreach (var group in groupedReviewItems)
                            {
                                var accent = services.PreviewBadge(group.Key);
                                var section = new Border { Background = B("#121B21"), BorderBrush = accent, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18), Padding = new Thickness(16), Margin = new Thickness(0, 0, 0, 14) };
                                var sectionStack = new StackPanel();
                                var sectionHeader = new DockPanel { Margin = new Thickness(0, 0, 0, 12) };
                                sectionHeader.Children.Add(new TextBlock { Text = group.Key, Foreground = B("#F5EFE4"), FontSize = 17, FontWeight = FontWeights.SemiBold });
                                var countLabel = new TextBlock
                                {
                                    Text = group.Count() + " ready",
                                    Foreground = accent,
                                    FontSize = 12,
                                    FontWeight = FontWeights.SemiBold,
                                    HorizontalAlignment = HorizontalAlignment.Right,
                                    VerticalAlignment = VerticalAlignment.Center
                                };
                                DockPanel.SetDock(countLabel, Dock.Right);
                                sectionHeader.Children.Add(countLabel);
                                sectionStack.Children.Add(sectionHeader);
                                foreach (var item in group)
                                {
                                    var row = new Border { Background = B("#0D1419"), BorderBrush = B("#1C2B34"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 0, 8) };
                                    var rowText = new StackPanel();
                                    rowText.Children.Add(new TextBlock { Text = item.FileName, Foreground = B("#F4F7FA"), FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                                    rowText.Children.Add(new TextBlock { Text = "Captured " + services.FormatTimestamp(item.CaptureTime), Foreground = B("#8FA1AD"), FontSize = 11, Margin = new Thickness(0, 4, 0, 0) });
                                    row.Child = rowText;
                                    sectionStack.Children.Add(row);
                                }
                                section.Child = sectionStack;
                                autoReadyPanel.Children.Add(section);
                            }
                        }

                        sidePanel.Children.Clear();
                        var manualCard = new Border { Background = B("#1A1511"), BorderBrush = B("#5F4527"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(22), Padding = new Thickness(18), Margin = new Thickness(0, 0, 0, 14) };
                        var manualStack = new StackPanel();
                        manualStack.Children.Add(new TextBlock { Text = "Manual Intake", FontSize = 19, FontWeight = FontWeights.SemiBold, Foreground = B("#F8E7CF") });
                        manualStack.Children.Add(new TextBlock { Text = summary.ManualItemCount == 0 ? "Nothing is blocked in manual review right now." : summary.ManualItemCount + " item(s) need a game or platform decision before import.", Foreground = B("#D8C2A0"), Margin = new Thickness(0, 8, 0, 12), TextWrapping = TextWrapping.Wrap });
                        if (summary.ManualItems.Count == 0)
                        {
                            manualStack.Children.Add(new Border
                            {
                                Background = B("#120F0C"),
                                BorderBrush = B("#4A3821"),
                                BorderThickness = new Thickness(1),
                                CornerRadius = new CornerRadius(16),
                                Padding = new Thickness(14),
                                Child = new TextBlock { Text = "Unmatched uploads will land here when PixelVault cannot confidently place them.", Foreground = B("#BFA98A"), TextWrapping = TextWrapping.Wrap }
                            });
                        }
                        else
                        {
                            foreach (var item in summary.ManualItems)
                            {
                                var row = new Border { Background = B("#120F0C"), BorderBrush = B("#4A3821"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(14), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 0, 0, 8) };
                                var rowStack = new StackPanel();
                                rowStack.Children.Add(new TextBlock { Text = item.FileName, Foreground = B("#FFF1DE"), FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                                rowStack.Children.Add(new TextBlock { Text = "Best guess: " + services.FilenameGuess(item.FileName), Foreground = B("#D1B385"), FontSize = 11, Margin = new Thickness(0, 4, 0, 0) });
                                rowStack.Children.Add(new TextBlock { Text = "Captured " + services.FormatTimestamp(item.CaptureTime), Foreground = B("#B59E81"), FontSize = 11, Margin = new Thickness(0, 2, 0, 0) });
                                row.Child = rowStack;
                                manualStack.Children.Add(row);
                            }
                        }
                        manualCard.Child = manualStack;
                        sidePanel.Children.Add(manualCard);

                        var notesCard = new Border { Background = B("#11181D"), BorderBrush = B("#243742"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(22), Padding = new Thickness(18), Margin = new Thickness(0, 0, 0, 14) };
                        var notesStack = new StackPanel();
                        notesStack.Children.Add(new TextBlock { Text = "Pipeline notes", FontSize = 19, FontWeight = FontWeights.SemiBold, Foreground = B("#F5EFE4") });
                        notesStack.Children.Add(new TextBlock { Text = "Rename scope: " + summary.RenameCandidateCount + " confident rename candidate(s) across " + summary.RenameScopeCount + " file(s) checked.", Foreground = B("#A7B5BD"), Margin = new Thickness(0, 10, 0, 0), TextWrapping = TextWrapping.Wrap });
                        notesStack.Children.Add(new TextBlock { Text = "Metadata-ready: " + summary.MetadataCandidateCount + " file(s) can carry tags and comments automatically.", Foreground = B("#A7B5BD"), Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap });
                        notesStack.Children.Add(new TextBlock
                        {
                            Text = summary.ConflictCount == 0 ? "Move conflicts: none detected in the destination library." : "Move conflicts: " + summary.ConflictCount + " filename collision(s) need the current conflict rule.",
                            Foreground = summary.ConflictCount == 0 ? B("#98C7A2") : B("#F3A9B8"),
                            Margin = new Thickness(0, 8, 0, 0),
                            TextWrapping = TextWrapping.Wrap
                        });
                        notesCard.Child = notesStack;
                        sidePanel.Children.Add(notesCard);

                        var sourcesCard = new Border { Background = B("#10181D"), BorderBrush = B("#243742"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(22), Padding = new Thickness(18) };
                        var sourcesStack = new StackPanel();
                        sourcesStack.Children.Add(new TextBlock { Text = "Upload folders", FontSize = 19, FontWeight = FontWeights.SemiBold, Foreground = B("#F5EFE4") });
                        foreach (var rootPath in summary.SourceRoots)
                        {
                            sourcesStack.Children.Add(new Border
                            {
                                Background = B("#0C1317"),
                                BorderBrush = B("#1D2A32"),
                                BorderThickness = new Thickness(1),
                                CornerRadius = new CornerRadius(14),
                                Padding = new Thickness(12, 10, 12, 10),
                                Margin = new Thickness(0, 10, 0, 0),
                                Child = new TextBlock { Text = rootPath, Foreground = B("#B5C2CA"), TextWrapping = TextWrapping.Wrap }
                            });
                        }
                        sourcesCard.Child = sourcesStack;
                        sidePanel.Children.Add(sourcesCard);

                        if (refreshButton != null) refreshButton.IsEnabled = true;
                        if (manualButton != null) manualButton.IsEnabled = summary.ManualItemCount > 0;
                    }, delegate(Exception ex)
                    {
                        if (previewWindow == null || !previewWindow.IsVisible) return;
                        if (refreshVersion != previewWindowRefreshVersion) return;
                        if (ex is OperationCanceledException) return;
                        if (services.SyncSettingsDocumentError != null) services.SyncSettingsDocumentError(ex.Message);
                        if (services.SetStatus != null) services.SetStatus("Preview failed");
                        if (services.Log != null) services.Log(ex.Message);
                        headerMeta.Text = ex.Message;
                        statsGrid.Children.Clear();
                        autoReadyPanel.Children.Clear();
                        sidePanel.Children.Clear();
                        autoReadyPanel.Children.Add(new Border
                        {
                            Background = B("#231519"),
                            BorderBrush = B("#6B2E38"),
                            BorderThickness = new Thickness(1),
                            CornerRadius = new CornerRadius(18),
                            Padding = new Thickness(20),
                            Child = new TextBlock { Text = ex.Message, Foreground = B("#F3B8C2"), TextWrapping = TextWrapping.Wrap }
                        });
                        sidePanel.Children.Add(new Border
                        {
                            Background = B("#11181D"),
                            BorderBrush = B("#243742"),
                            BorderThickness = new Thickness(1),
                            CornerRadius = new CornerRadius(22),
                            Padding = new Thickness(18),
                            Child = new TextBlock { Text = "Preview refresh failed. You can try Refresh again after checking the source folders or settings.", Foreground = B("#A7B5BD"), TextWrapping = TextWrapping.Wrap }
                        });
                        if (refreshButton != null) refreshButton.IsEnabled = true;
                        if (manualButton != null) manualButton.IsEnabled = true;
                    });
                };

                refreshButton.Click += delegate { renderWindow(); };
                manualButton.Click += delegate
                {
                    if (services.OpenManualIntake != null) services.OpenManualIntake();
                    renderWindow();
                };

                previewWindow.Loaded += delegate { renderWindow(); };
                previewWindow.Closed += delegate
                {
                    if (previewWindowRefreshCancellation != null)
                    {
                        previewWindowRefreshCancellation.Cancel();
                        previewWindowRefreshCancellation.Dispose();
                        previewWindowRefreshCancellation = null;
                    }
                };
                previewWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                if (previewWindow != null) previewWindow.Close();
                if (services.SyncSettingsDocumentError != null) services.SyncSettingsDocumentError(ex.Message);
                if (services.SetStatus != null) services.SetStatus("Preview failed");
                if (services.Log != null) services.Log(ex.Message);
                MessageBox.Show(ex.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
