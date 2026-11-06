using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;

namespace PixelVaultNative
{
    sealed class XboxAchievementsWindow : Window
    {
        readonly XboxAchievementService service = new();
        readonly CancellationTokenSource lifetime = new();
        readonly ListBox games = new() { DisplayMemberPath = "Display", MinHeight = 150 };
        readonly TextBox search = new() { Margin = new Thickness(0, 12, 0, 12), Padding = new Thickness(8) };
        readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 12, 0, 12) };
        readonly Button connect = Button("Connect Xbox");
        readonly Button disconnect = Button("Disconnect");
        readonly Button refresh = Button("Refresh games");
        readonly Button view = Button("View achievements");
        XboxAchievementService.Library library = new();
        bool busy;

        internal static void ShowFor(Window owner, string searchText = null)
        {
            var existing = owner?.OwnedWindows.OfType<XboxAchievementsWindow>().FirstOrDefault();
            if (existing != null) { existing.Activate(); return; }
            new XboxAchievementsWindow(owner, searchText).ShowDialog();
        }
        static Button Button(string text) => new() { Content = text, Padding = new Thickness(12, 7, 12, 7), Margin = new Thickness(0, 0, 8, 0) };
        XboxAchievementsWindow(Window owner, string searchText)
        {
            Owner = owner; Title = "Xbox achievements"; Width = 780; Height = 680; MinWidth = 620; MinHeight = 420;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = SystemColors.WindowBrush; Foreground = SystemColors.WindowTextBrush;
            var root = new DockPanel { Margin = new Thickness(22) }; Content = root;
            var top = new StackPanel(); DockPanel.SetDock(top, Dock.Top); root.Children.Add(top);
            top.Children.Add(new TextBlock { Text = "Xbox achievements", FontSize = 26, FontWeight = FontWeights.SemiBold });
            top.Children.Add(new TextBlock { Text = "Connect your Microsoft account to browse your Xbox games and achievements.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 14) });
            var actions = new WrapPanel(); actions.Children.Add(connect); actions.Children.Add(disconnect); actions.Children.Add(refresh); top.Children.Add(actions);
            top.Children.Add(status);
            top.Children.Add(new TextBlock { Text = "Find a game" }); top.Children.Add(search);
            DockPanel.SetDock(view, Dock.Bottom); view.HorizontalAlignment = HorizontalAlignment.Left; view.Margin = new Thickness(0, 12, 0, 0); root.Children.Add(view);
            root.Children.Add(games);
            AutomationProperties.SetName(search, "Search Xbox games"); AutomationProperties.SetName(games, "Xbox games");
            AutomationProperties.SetLiveSetting(status, AutomationLiveSetting.Polite);
            search.Text = searchText ?? "";
            search.TextChanged += (_, __) => Filter();
            games.SelectionChanged += (_, __) => UpdateButtons();
            connect.Click += async (_, __) => await Work(async () => {
                status.Text = "Complete sign-in in your browser. This window will update when you return.";
                await service.ConnectAsync(lifetime.Token); library = service.SavedLibrary(); Filter();
                library = await service.LoadLibraryAsync(lifetime.Token); Filter(); Summary();
            });
            disconnect.Click += async (_, __) => await Work(async () => { await service.DisconnectAsync(lifetime.Token); library = new(); Filter(); status.Text = "Xbox disconnected."; });
            refresh.Click += async (_, __) => await Work(async () => { status.Text = "Loading Xbox games…"; library = await service.LoadLibraryAsync(lifetime.Token); Filter(); Summary(); });
            view.Click += (_, __) => Open();
            games.MouseDoubleClick += (_, __) => Open();
            games.KeyDown += (_, e) => { if (e.Key == Key.Enter) { Open(); e.Handled = true; } };
            Closed += (_, __) => lifetime.Cancel();
            Loaded += (_, __) => {
                library = service.SavedLibrary(); Filter();
                status.Text = service.Connected ? "Showing saved games. Select Refresh games to update." : service.Installed ? "Select Connect Xbox to sign in." : "Run scripts/Install-PixelVaultXbox.ps1 from the source folder to install Xbox support.";
                UpdateButtons();
            };
        }
        void Summary() => status.Text = library.Titles.Count + " games · Xbox account " + library.Xuid;
        void Filter()
        {
            games.ItemsSource = library.Titles.Where(t => t.Name.Contains(search.Text.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
            UpdateButtons();
        }
        void UpdateButtons()
        {
            connect.IsEnabled = !busy && service.Installed;
            disconnect.IsEnabled = refresh.IsEnabled = !busy && service.Connected;
            view.IsEnabled = !busy && service.Connected && games.SelectedItem != null;
            search.IsEnabled = games.IsEnabled = !busy;
        }
        async Task Work(Func<Task> work)
        {
            if (busy) return; busy = true; UpdateButtons();
            try { await work(); }
            catch (OperationCanceledException) { status.Text = "Xbox request cancelled or timed out."; }
            catch (Exception ex) { status.Text = ex.Message; }
            finally { busy = false; UpdateButtons(); }
        }
        void Open()
        {
            if (busy || !service.Connected || games.SelectedItem is not XboxAchievementService.XboxTitle title) return;
            var xuid = library.Xuid;
            if (string.IsNullOrWhiteSpace(xuid)) return;
            AchievementsInfoWindow.ShowXbox(this, title.Name, xuid, token => service.FetchAsync(xuid, title, token));
        }
    }
}
