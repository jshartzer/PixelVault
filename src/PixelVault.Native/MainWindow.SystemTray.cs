using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        enum SystemTrayCloseDecision
        {
            Cancel,
            Exit,
            SendToTray
        }

        Forms.NotifyIcon _systemTrayIcon;
        Window _systemTrayStatusFlyout;
        Action _systemTrayStatusFlyoutReload;
        bool _allowActualWindowClose;
        bool _systemSessionEnding;
        bool _mainWindowHiddenToTray;
        bool _systemTrayHintShown;
        bool _systemTrayClosePromptOpen;
        WindowState _trayRestoreWindowState = WindowState.Normal;

        void InitializeSystemTraySupport()
        {
            StateChanged += MainWindow_SystemTrayStateChanged;
            Closing += MainWindow_SystemTrayClosing;
            Closed += MainWindow_SystemTrayClosed;
            if (Application.Current != null)
                Application.Current.SessionEnding += MainWindow_SystemTraySessionEnding;
        }

        bool ShouldSendToSystemTrayOnMinimize() => systemTrayMinimizeEnabled;

        bool ShouldPromptForSystemTrayOnClose() => systemTrayPromptOnCloseEnabled;

        string BuildSystemTrayTooltipText()
        {
            var text = backgroundAutoIntakeEnabled
                ? "PixelVault - background auto-intake active"
                : "PixelVault - running in the system tray";
            return text.Length <= 63 ? text : text.Substring(0, 63);
        }

        string BuildSystemTrayMonitorStatusText()
        {
            if (!backgroundAutoIntakeEnabled)
                return "Background auto-intake is off right now.";

            if (string.IsNullOrWhiteSpace(libraryRoot))
                return "Background auto-intake is on, but the library folder is not set.";

            var roots = GetSourceRoots();
            if (roots.Count == 0)
                return "Background auto-intake is on, but no source folders are configured.";

            return "Watching " + roots.Count + " source folder(s) for new imports.";
        }

        Drawing.Icon ResolveSystemTrayIcon()
        {
            var iconPath = Path.Combine(appRoot, "assets", "PixelVault.ico");
            try
            {
                if (File.Exists(iconPath))
                    return new Drawing.Icon(iconPath);
            }
            catch
            {
            }

            try
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrWhiteSpace(exe) && File.Exists(exe))
                {
                    var extracted = Drawing.Icon.ExtractAssociatedIcon(exe);
                    if (extracted != null) return extracted;
                }
            }
            catch
            {
            }

            return Drawing.SystemIcons.Application;
        }

        void EnsureSystemTrayIcon()
        {
            if (_systemTrayIcon != null) return;

            var recentStatusItem = new Forms.ToolStripMenuItem("Recent Import Status");
            recentStatusItem.Click += delegate { ToggleSystemTrayStatusFlyout(forceOpen: true); };

            var restoreItem = new Forms.ToolStripMenuItem("Restore PixelVault");
            restoreItem.Click += delegate { RestoreMainWindowFromSystemTray(); };

            var backgroundImportsItem = new Forms.ToolStripMenuItem("Background Imports");
            backgroundImportsItem.Click += delegate
            {
                RestoreMainWindowFromSystemTray();
                EnsureBackgroundIntakeActivityWindow();
            };

            var exitItem = new Forms.ToolStripMenuItem("Exit PixelVault");
            exitItem.Click += delegate { ExitApplicationFromSystemTray(); };

            _systemTrayIcon = new Forms.NotifyIcon
            {
                Text = BuildSystemTrayTooltipText(),
                Icon = ResolveSystemTrayIcon(),
                Visible = false,
                ContextMenuStrip = new Forms.ContextMenuStrip()
            };
            _systemTrayIcon.ContextMenuStrip.Items.Add(recentStatusItem);
            _systemTrayIcon.ContextMenuStrip.Items.Add(restoreItem);
            _systemTrayIcon.ContextMenuStrip.Items.Add(backgroundImportsItem);
            _systemTrayIcon.ContextMenuStrip.Items.Add(new Forms.ToolStripSeparator());
            _systemTrayIcon.ContextMenuStrip.Items.Add(exitItem);
            _systemTrayIcon.MouseClick += SystemTrayIcon_MouseClick;
        }

        void SystemTrayIcon_MouseClick(object sender, Forms.MouseEventArgs e)
        {
            if (e.Button == Forms.MouseButtons.Left)
                ToggleSystemTrayStatusFlyout();
        }

        void ShowSystemTrayIcon()
        {
            EnsureSystemTrayIcon();
            if (_systemTrayIcon == null) return;
            _systemTrayIcon.Text = BuildSystemTrayTooltipText();
            _systemTrayIcon.Visible = true;
        }

        void HideSystemTrayIcon()
        {
            CloseSystemTrayStatusFlyout();
            if (_systemTrayIcon == null) return;
            _systemTrayIcon.Visible = false;
        }

        void SendMainWindowToSystemTray(bool showBalloonTip)
        {
            if (_mainWindowHiddenToTray) return;

            _trayRestoreWindowState = WindowState == WindowState.Minimized ? WindowState.Normal : WindowState;
            if (_trayRestoreWindowState == WindowState.Minimized) _trayRestoreWindowState = WindowState.Normal;

            ShowSystemTrayIcon();
            _mainWindowHiddenToTray = true;
            ShowInTaskbar = false;
            Hide();

            if (showBalloonTip && !_systemTrayHintShown && _systemTrayIcon != null)
            {
                try
                {
                    _systemTrayIcon.BalloonTipTitle = "PixelVault is still running";
                    _systemTrayIcon.BalloonTipText = "Use the tray icon for recent import status, restore, or exit.";
                    _systemTrayIcon.ShowBalloonTip(4000);
                }
                catch
                {
                }

                _systemTrayHintShown = true;
            }

            try
            {
                Log("Main window hidden to system tray.");
            }
            catch
            {
            }
        }

        void RestoreMainWindowFromSystemTray()
        {
            if (!Dispatcher.CheckAccess())
            {
                _ = Dispatcher.BeginInvoke(new Action(RestoreMainWindowFromSystemTray));
                return;
            }

            CloseSystemTrayStatusFlyout();
            _mainWindowHiddenToTray = false;
            ShowInTaskbar = true;
            if (!IsVisible) Show();
            WindowState = _trayRestoreWindowState == WindowState.Maximized ? WindowState.Maximized : WindowState.Normal;
            Activate();
            Focus();
            HideSystemTrayIcon();
        }

        void ExitApplicationFromSystemTray()
        {
            if (!Dispatcher.CheckAccess())
            {
                _ = Dispatcher.BeginInvoke(new Action(ExitApplicationFromSystemTray));
                return;
            }

            CloseSystemTrayStatusFlyout();
            _allowActualWindowClose = true;
            _mainWindowHiddenToTray = false;
            HideSystemTrayIcon();
            Close();
        }

        void ToggleSystemTrayStatusFlyout(bool forceOpen = false)
        {
            if (!Dispatcher.CheckAccess())
            {
                _ = Dispatcher.BeginInvoke(new Action(() => ToggleSystemTrayStatusFlyout(forceOpen)));
                return;
            }

            if (_systemTrayStatusFlyout != null)
            {
                if (!forceOpen)
                {
                    CloseSystemTrayStatusFlyout();
                    return;
                }

                _systemTrayStatusFlyoutReload?.Invoke();
                PositionSystemTrayStatusFlyoutWindow(_systemTrayStatusFlyout);
                _systemTrayStatusFlyout.Activate();
                return;
            }

            var flyout = CreateSystemTrayStatusFlyoutWindow();
            _systemTrayStatusFlyout = flyout;
            flyout.Closed += delegate
            {
                if (ReferenceEquals(_systemTrayStatusFlyout, flyout))
                {
                    _systemTrayStatusFlyout = null;
                    _systemTrayStatusFlyoutReload = null;
                }
            };
            flyout.Show();
            flyout.UpdateLayout();
            PositionSystemTrayStatusFlyoutWindow(flyout);
            flyout.Activate();
        }

        void ReloadSystemTrayStatusFlyoutIfOpen()
        {
            _systemTrayStatusFlyoutReload?.Invoke();
        }

        void CloseSystemTrayStatusFlyout()
        {
            var flyout = _systemTrayStatusFlyout;
            if (flyout == null) return;
            try
            {
                flyout.Close();
            }
            catch
            {
            }
        }

        Window CreateSystemTrayStatusFlyoutWindow()
        {
            var flyout = new Window
            {
                Title = "PixelVault status",
                Width = 392,
                Height = 420,
                MinWidth = 392,
                MinHeight = 320,
                MaxHeight = 520,
                WindowStyle = WindowStyle.None,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                ShowActivated = true,
                Topmost = true,
                Background = Brush("#0F1519"),
                WindowStartupLocation = WindowStartupLocation.Manual
            };

            var shell = new Border
            {
                Background = Brush("#141B20"),
                BorderBrush = Brush("#27313A"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(18),
                Padding = new Thickness(16)
            };

            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var title = new TextBlock
            {
                Text = "PixelVault",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White
            };
            Grid.SetRow(title, 0);
            root.Children.Add(title);

            var subtitle = new TextBlock
            {
                Margin = new Thickness(0, 8, 0, 0),
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush("#A7B5BD"),
                FontSize = 12
            };
            Grid.SetRow(subtitle, 1);
            root.Children.Add(subtitle);

            var summary = new TextBlock
            {
                Margin = new Thickness(0, 10, 0, 12),
                TextWrapping = TextWrapping.Wrap,
                Foreground = Brush("#D8E4EA"),
                FontSize = 12
            };
            Grid.SetRow(summary, 2);
            root.Children.Add(summary);

            var recentHost = new StackPanel();
            var scroll = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = recentHost
            };
            Grid.SetRow(scroll, 3);
            root.Children.Add(scroll);

            var footer = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
            var actions = new WrapPanel();

            Button BuildFlyoutButton(string label, RoutedEventHandler click, string bg)
            {
                var button = Btn(label, click, bg, Brushes.White);
                button.Width = 108;
                button.Height = 38;
                button.Margin = new Thickness(0, 0, 8, 8);
                return button;
            }

            actions.Children.Add(BuildFlyoutButton("Restore", delegate
            {
                RestoreMainWindowFromSystemTray();
            }, "#2C7BE5"));
            actions.Children.Add(BuildFlyoutButton("History", delegate
            {
                RestoreMainWindowFromSystemTray();
                EnsureBackgroundIntakeActivityWindow();
            }, "#36566B"));
            actions.Children.Add(BuildFlyoutButton("Exit", delegate
            {
                ExitApplicationFromSystemTray();
            }, "#7A3340"));
            footer.Children.Add(actions);
            footer.Children.Add(new TextBlock
            {
                Text = "Right-click the tray icon for the quick menu.",
                Foreground = Brush("#7F919D"),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap
            });
            Grid.SetRow(footer, 4);
            root.Children.Add(footer);

            Border BuildRecentActivityCard(DateTime batchUtc, BackgroundIntakeActivityRow row)
            {
                var statusText = row != null && row.Undone ? "Undone" : "In library";
                var meta = batchUtc.ToLocalTime().ToString("h:mm tt");
                if (row != null && !string.IsNullOrWhiteSpace(row.RuleLabel) && !string.Equals(row.RuleLabel, "—", StringComparison.Ordinal))
                    meta += " · " + row.RuleLabel;
                var sourceLeaf = row == null ? string.Empty : LeafFolderLabel(row.SourceFolder);
                if (!string.IsNullOrWhiteSpace(sourceLeaf))
                    meta += " · " + sourceLeaf;
                meta += " · " + statusText;

                var stack = new StackPanel();
                stack.Children.Add(new TextBlock
                {
                    Text = row == null || string.IsNullOrWhiteSpace(row.FileLabel) ? "(unnamed import)" : row.FileLabel,
                    Foreground = row != null && row.Undone ? Brush("#A7B5BD") : Brushes.White,
                    FontWeight = FontWeights.SemiBold,
                    TextTrimming = TextTrimming.CharacterEllipsis
                });
                stack.Children.Add(new TextBlock
                {
                    Text = meta,
                    Foreground = Brush("#8FA1AD"),
                    FontSize = 11,
                    Margin = new Thickness(0, 4, 0, 0),
                    TextWrapping = TextWrapping.Wrap
                });

                return new Border
                {
                    Background = Brush("#182128"),
                    BorderBrush = Brush("#24313A"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(12, 10, 12, 10),
                    Margin = new Thickness(0, 0, 0, 8),
                    Child = stack
                };
            }

            void Rebuild()
            {
                var batches = _backgroundIntakeActivitySession
                    .GetBatchesSnapshot()
                    .OrderByDescending(b => b.CompletedUtc)
                    .ToList();
                var latestBatch = batches.FirstOrDefault();
                var recentRows = batches
                    .SelectMany(batch => batch.Rows.Select(row => new { batch.CompletedUtc, Row = row }))
                    .OrderByDescending(item => item.CompletedUtc)
                    .Take(6)
                    .ToList();

                subtitle.Text = BuildSystemTrayMonitorStatusText();
                summary.Text = latestBatch == null
                    ? "No background imports have run in this PixelVault session yet."
                    : "Last batch: " + latestBatch.CompletedUtc.ToLocalTime().ToString("MMM d, h:mm tt") + " · " + latestBatch.Rows.Count + " file(s)";

                recentHost.Children.Clear();
                recentHost.Children.Add(new TextBlock
                {
                    Text = "Recent imports",
                    Foreground = Brush("#D8E4EA"),
                    FontWeight = FontWeights.SemiBold,
                    Margin = new Thickness(0, 0, 0, 8)
                });

                if (recentRows.Count == 0)
                {
                    recentHost.Children.Add(new Border
                    {
                        Background = Brush("#182128"),
                        BorderBrush = Brush("#24313A"),
                        BorderThickness = new Thickness(1),
                        CornerRadius = new CornerRadius(12),
                        Padding = new Thickness(12, 10, 12, 10),
                        Child = new TextBlock
                        {
                            Text = backgroundAutoIntakeEnabled
                                ? "New automatic imports will appear here after PixelVault moves files into the library."
                                : "Turn on background auto-intake in Path Settings if you want new imports to appear here.",
                            Foreground = Brush("#A7B5BD"),
                            TextWrapping = TextWrapping.Wrap
                        }
                    });
                }
                else
                {
                    foreach (var recent in recentRows)
                        recentHost.Children.Add(BuildRecentActivityCard(recent.CompletedUtc, recent.Row));
                }
            }

            Rebuild();
            _systemTrayStatusFlyoutReload = delegate
            {
                Rebuild();
                if (_systemTrayStatusFlyout != null)
                {
                    _systemTrayStatusFlyout.UpdateLayout();
                    PositionSystemTrayStatusFlyoutWindow(_systemTrayStatusFlyout);
                }
            };

            shell.Child = root;
            flyout.Content = shell;
            flyout.Deactivated += delegate
            {
                if (ReferenceEquals(_systemTrayStatusFlyout, flyout))
                    CloseSystemTrayStatusFlyout();
            };
            flyout.PreviewKeyDown += delegate(object sender, KeyEventArgs args)
            {
                if (args.Key != Key.Escape) return;
                args.Handled = true;
                flyout.Close();
            };
            return flyout;
        }

        void PositionSystemTrayStatusFlyoutWindow(Window flyout)
        {
            if (flyout == null) return;
            flyout.UpdateLayout();
            var area = SystemParameters.WorkArea;
            var width = flyout.ActualWidth > 0 ? flyout.ActualWidth : flyout.Width;
            var height = flyout.ActualHeight > 0 ? flyout.ActualHeight : flyout.Height;
            flyout.Left = Math.Max(area.Left + 12, area.Right - width - 16);
            flyout.Top = Math.Max(area.Top + 12, area.Bottom - height - 16);
        }

        static string LeafFolderLabel(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            try
            {
                var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return Path.GetFileName(trimmed) ?? trimmed;
            }
            catch
            {
                return path;
            }
        }

        SystemTrayCloseDecision ShowSystemTrayClosePromptDialog()
        {
            var decision = SystemTrayCloseDecision.Cancel;
            var dialog = new Window
            {
                Title = "Close PixelVault?",
                Width = 520,
                Height = 250,
                MinWidth = 520,
                MinHeight = 250,
                MaxWidth = 520,
                MaxHeight = 250,
                ResizeMode = ResizeMode.NoResize,
                Owner = this,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Brush("#0F1519")
            };

            var root = new Grid { Margin = new Thickness(24) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            root.Children.Add(new TextBlock
            {
                Text = "Keep PixelVault running in the tray?",
                Foreground = Brushes.White,
                FontSize = 20,
                FontWeight = FontWeights.SemiBold
            });

            var body = new TextBlock
            {
                Margin = new Thickness(0, 12, 0, 0),
                Foreground = Brush("#C8D4DC"),
                TextWrapping = TextWrapping.Wrap,
                Text = backgroundAutoIntakeEnabled
                    ? "Background auto-intake can keep watching your source folders if PixelVault stays in the system tray."
                    : "PixelVault can stay available in the system tray so you can reopen it quickly later."
            };
            Grid.SetRow(body, 1);
            root.Children.Add(body);

            var note = new TextBlock
            {
                Margin = new Thickness(0, 14, 0, 0),
                Foreground = Brush("#8FA1AD"),
                TextWrapping = TextWrapping.Wrap,
                Text = "Change this behavior anytime in Path Settings under System tray."
            };
            Grid.SetRow(note, 2);
            root.Children.Add(note);

            var buttons = new WrapPanel { Margin = new Thickness(0, 18, 0, 0) };
            Button MakeDialogButton(string label, string bg, Action onClick)
            {
                var button = Btn(label, null, bg, Brushes.White);
                button.Width = 140;
                button.Height = 40;
                button.Margin = new Thickness(0, 0, 10, 0);
                button.Click += delegate { onClick(); };
                return button;
            }

            buttons.Children.Add(MakeDialogButton("Send to Tray", "#2C7BE5", delegate
            {
                decision = SystemTrayCloseDecision.SendToTray;
                dialog.Close();
            }));
            buttons.Children.Add(MakeDialogButton("Exit PixelVault", "#7A3340", delegate
            {
                decision = SystemTrayCloseDecision.Exit;
                dialog.Close();
            }));
            buttons.Children.Add(MakeDialogButton("Cancel", "#36566B", delegate
            {
                decision = SystemTrayCloseDecision.Cancel;
                dialog.Close();
            }));
            Grid.SetRow(buttons, 3);
            root.Children.Add(buttons);

            dialog.Content = root;
            dialog.ShowDialog();
            return decision;
        }

        void DisposeSystemTrayIcon()
        {
            CloseSystemTrayStatusFlyout();
            if (_systemTrayIcon == null) return;
            try
            {
                _systemTrayIcon.Visible = false;
                _systemTrayIcon.MouseClick -= SystemTrayIcon_MouseClick;
                _systemTrayIcon.Dispose();
            }
            catch
            {
            }
            finally
            {
                _systemTrayIcon = null;
            }
        }

        void MainWindow_SystemTrayStateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized && ShouldSendToSystemTrayOnMinimize())
                SendMainWindowToSystemTray(!_systemTrayHintShown);
        }

        void MainWindow_SystemTrayClosing(object sender, CancelEventArgs e)
        {
            if (_allowActualWindowClose || _systemSessionEnding) return;
            if (!ShouldPromptForSystemTrayOnClose()) return;

            e.Cancel = true;
            if (_systemTrayClosePromptOpen) return;

            _systemTrayClosePromptOpen = true;
            try
            {
                var decision = ShowSystemTrayClosePromptDialog();
                if (decision == SystemTrayCloseDecision.SendToTray)
                {
                    SendMainWindowToSystemTray(true);
                    return;
                }

                if (decision == SystemTrayCloseDecision.Exit)
                {
                    _allowActualWindowClose = true;
                    _ = Dispatcher.BeginInvoke(new Action(Close));
                }
            }
            finally
            {
                _systemTrayClosePromptOpen = false;
            }
        }

        void MainWindow_SystemTrayClosed(object sender, EventArgs e)
        {
            if (Application.Current != null)
                Application.Current.SessionEnding -= MainWindow_SystemTraySessionEnding;
            DisposeSystemTrayIcon();
        }

        void MainWindow_SystemTraySessionEnding(object sender, SessionEndingCancelEventArgs e)
        {
            _systemSessionEnding = true;
            _allowActualWindowClose = true;
            HideSystemTrayIcon();
        }
    }
}
