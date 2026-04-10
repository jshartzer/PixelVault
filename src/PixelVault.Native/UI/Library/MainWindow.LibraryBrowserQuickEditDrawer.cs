using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        void LibraryBrowserMountQuickEditDrawer(Grid root, LibraryBrowserWorkingSet ws)
        {
            if (root == null || ws == null) return;

            var host = new Grid
            {
                Visibility = Visibility.Collapsed,
                Background = new SolidColorBrush(Color.FromArgb(0x88, 15, 21, 25))
            };
            Panel.SetZIndex(host, 70);
            Grid.SetRowSpan(host, 2);
            host.MouseLeftButtonDown += delegate { LibraryBrowserSetQuickEditDrawerOpen(ws, false); };

            var panel = new Border
            {
                Width = 368,
                HorizontalAlignment = HorizontalAlignment.Right,
                Background = Brush("#151C22"),
                BorderBrush = Brush("#27313A"),
                BorderThickness = new Thickness(1, 0, 0, 0)
            };
            panel.MouseLeftButtonDown += delegate(object _, MouseButtonEventArgs e) { e.Handled = true; };

            var stack = new StackPanel { Margin = new Thickness(18, 20, 18, 20) };
            stack.Children.Add(new TextBlock
            {
                Text = "Quick edit",
                FontSize = 18,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 10)
            });
            ws.QuickEditDrawerTitleText = new TextBlock
            {
                Text = "No folder selected",
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 6)
            };
            stack.Children.Add(ws.QuickEditDrawerTitleText);
            ws.QuickEditDrawerSubtitleText = new TextBlock
            {
                Text = "Select a game folder in the list.",
                Foreground = Brush("#9CB1BC"),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 13,
                Margin = new Thickness(0, 0, 0, 14)
            };
            stack.Children.Add(ws.QuickEditDrawerSubtitleText);

            stack.Children.Add(new TextBlock
            {
                Text = "Quick fields",
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brush("#C8D8E0"),
                Margin = new Thickness(0, 0, 0, 6)
            });
            stack.Children.Add(new TextBlock
            {
                Text = "Steam App ID",
                Foreground = Brush("#9CB1BC"),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 4)
            });
            ws.QuickEditSteamAppIdBox = new TextBox
            {
                Padding = new Thickness(8, 6, 8, 6),
                Background = Brush("#0E1419"),
                Foreground = Brush("#E8F0F5"),
                BorderBrush = Brush("#33424D"),
                BorderThickness = new Thickness(1),
                FontSize = 13,
                CaretBrush = Brushes.White,
                Margin = new Thickness(0, 0, 0, 10),
                IsEnabled = false
            };
            AccessibilityUi.TryApplyFocusVisualStyle(ws.QuickEditSteamAppIdBox);
            AccessibilityUi.TrySetAutomationName(ws.QuickEditSteamAppIdBox, "Steam App ID");
            stack.Children.Add(ws.QuickEditSteamAppIdBox);

            stack.Children.Add(new TextBlock
            {
                Text = "Collection notes",
                Foreground = Brush("#9CB1BC"),
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 4)
            });
            ws.QuickEditCollectionNotesBox = new TextBox
            {
                Padding = new Thickness(8, 6, 8, 6),
                Background = Brush("#0E1419"),
                Foreground = Brush("#E8F0F5"),
                BorderBrush = Brush("#33424D"),
                BorderThickness = new Thickness(1),
                FontSize = 13,
                CaretBrush = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                MaxHeight = 72,
                Margin = new Thickness(0, 0, 0, 8),
                IsEnabled = false
            };
            AccessibilityUi.TryApplyFocusVisualStyle(ws.QuickEditCollectionNotesBox);
            AccessibilityUi.TrySetAutomationName(ws.QuickEditCollectionNotesBox, "Collection notes");
            stack.Children.Add(ws.QuickEditCollectionNotesBox);

            ws.QuickEditDrawerApplyInlineButton = new Button
            {
                Content = "Apply quick fields",
                Height = 34,
                MinWidth = 140,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 14),
                IsEnabled = false
            };
            ApplyLibraryPillChrome(ws.QuickEditDrawerApplyInlineButton, "#2A4A5E", "#3A5F78", "#355A72", "#243A4A", "#D7E2EA");
            ws.QuickEditDrawerApplyInlineButton.ToolTip = "Save Steam App ID and collection notes to the game index for this selection";
            ws.QuickEditDrawerApplyInlineButton.Click += delegate { LibraryBrowserApplyQuickEditDrawerFields(ws); };
            AccessibilityUi.TryApplyFocusVisualStyle(ws.QuickEditDrawerApplyInlineButton);
            AccessibilityUi.TrySetAutomationName(ws.QuickEditDrawerApplyInlineButton, "Apply quick fields");
            stack.Children.Add(ws.QuickEditDrawerApplyInlineButton);

            ws.QuickEditDrawerMetadataButton = new Button
            {
                Content = "Edit metadata",
                Height = 36,
                MinWidth = 140,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 8)
            };
            ApplyLibraryPillChrome(ws.QuickEditDrawerMetadataButton, "#20343A", "#2D4A55", "#2A3F48", "#18242A", "#D7E2EA");
            ws.QuickEditDrawerMetadataButton.ToolTip = "Same as the detail toolbar (batch manual metadata)";
            ws.QuickEditDrawerMetadataButton.Click += delegate
            {
                if (ws.Current == null)
                {
                    TryLibraryToast("Select a folder first.", MessageBoxImage.Information);
                    return;
                }
                ws.QuickEditOpenMetadata?.Invoke();
            };
            AccessibilityUi.TryApplyFocusVisualStyle(ws.QuickEditDrawerMetadataButton);
            AccessibilityUi.TrySetAutomationName(ws.QuickEditDrawerMetadataButton, "Edit metadata");
            stack.Children.Add(ws.QuickEditDrawerMetadataButton);

            ws.QuickEditDrawerOpenFolderButton = new Button
            {
                Content = "Open folder",
                Height = 36,
                MinWidth = 140,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Foreground = Brushes.White,
                Margin = new Thickness(0, 0, 0, 14)
            };
            ApplyLibraryPillChrome(ws.QuickEditDrawerOpenFolderButton, "#275D47", "#2F6B53", "#2E654D", "#214D39", "#D7E2EA");
            ws.QuickEditDrawerOpenFolderButton.ToolTip = "Open the on-disk folder in Explorer";
            ws.QuickEditDrawerOpenFolderButton.Click += delegate
            {
                if (ws.Current == null)
                {
                    TryLibraryToast("Select a folder first.", MessageBoxImage.Information);
                    return;
                }
                if (GetLibraryBrowserSourceFolderPaths(ws.Current).Count == 0)
                {
                    TryLibraryToast("No folder path on disk for this selection.", MessageBoxImage.Information);
                    return;
                }
                ws.QuickEditOpenFolders?.Invoke();
            };
            AccessibilityUi.TryApplyFocusVisualStyle(ws.QuickEditDrawerOpenFolderButton);
            AccessibilityUi.TrySetAutomationName(ws.QuickEditDrawerOpenFolderButton, "Open folder");
            stack.Children.Add(ws.QuickEditDrawerOpenFolderButton);

            stack.Children.Add(new TextBlock
            {
                Text = "Tip: Ctrl+Shift+P opens the command palette; Ctrl+Shift+E toggles this panel.",
                Foreground = Brush("#7A8F9A"),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 12)
            });

            var close = new Button
            {
                Content = "Close",
                Height = 34,
                MinWidth = 88,
                HorizontalAlignment = HorizontalAlignment.Left,
                Foreground = Brushes.White
            };
            ApplyLibraryPillChrome(close, "#232B35", "#33424D", "#2A3440", "#182028", "#D7E2EA");
            close.ToolTip = "Close (Esc)";
            close.Click += delegate { LibraryBrowserSetQuickEditDrawerOpen(ws, false); };
            AccessibilityUi.TryApplyFocusVisualStyle(close);
            AccessibilityUi.TrySetAutomationName(close, "Close quick edit");
            stack.Children.Add(close);
            panel.Child = stack;
            host.Children.Add(panel);
            root.Children.Add(host);
            ws.QuickEditDrawerHost = host;
        }

        void LibraryBrowserApplyQuickEditDrawerFields(LibraryBrowserWorkingSet ws)
        {
            if (ws?.Current == null || librarySession == null || !librarySession.HasLibraryRoot) return;
            if (IsLibraryBrowserTimelineView(ws.Current) || ws.IsPhotoWorkspaceMode) return;
            if (ws.Current.PendingGameAssignment)
            {
                TryLibraryToast("Assign a game title first (pending resolution).", MessageBoxImage.Information);
                return;
            }

            try
            {
                var root = librarySession.LibraryRoot;
                if (string.IsNullOrWhiteSpace(root)) return;
                var display = BuildLibraryBrowserDisplayFolder(ws.Current);
                if (display == null) return;
                var rows = GetSavedGameIndexRowsForRoot(root);
                var saved = FindSavedGameIndexRow(rows, display);
                var prevSteam = saved?.SteamAppId ?? display.SteamAppId ?? string.Empty;
                var prevSup = saved?.SuppressSteamAppIdAutoResolve ?? display.SuppressSteamAppIdAutoResolve;
                var steamInput = CleanTag(ws.QuickEditSteamAppIdBox?.Text ?? string.Empty);
                var notesInput = (ws.QuickEditCollectionNotesBox?.Text ?? string.Empty).Trim();
                display.SteamAppId = steamInput;
                display.SuppressSteamAppIdAutoResolve = ShouldSuppressExternalIdAutoResolve(steamInput, prevSteam, prevSup);
                display.CollectionNotes = notesInput;
                UpsertSavedGameIndexRow(root, display);
                librarySession.RefreshFolderCacheAfterGameIndexChange();
                ws.Current.SteamAppId = display.SteamAppId;
                ws.Current.SuppressSteamAppIdAutoResolve = display.SuppressSteamAppIdAutoResolve;
                ws.Current.CollectionNotes = display.CollectionNotes;
                foreach (var sf in ws.Current.SourceFolders)
                {
                    if (sf == null) continue;
                    sf.SteamAppId = display.SteamAppId;
                    sf.SuppressSteamAppIdAutoResolve = display.SuppressSteamAppIdAutoResolve;
                    sf.CollectionNotes = display.CollectionNotes;
                }

                if (ws.Current.PrimaryFolder != null)
                {
                    ws.Current.PrimaryFolder.SteamAppId = display.SteamAppId;
                    ws.Current.PrimaryFolder.SuppressSteamAppIdAutoResolve = display.SuppressSteamAppIdAutoResolve;
                    ws.Current.PrimaryFolder.CollectionNotes = display.CollectionNotes;
                }

                PopulateLibraryBrowserFolderViewSearchBlob(ws.Current);
                ws.QuickEditRefreshLibraryFolders?.Invoke();
                TryLibraryToast("Saved quick fields.", MessageBoxImage.Information);
                LibraryBrowserRefreshQuickEditDrawerContent(ws);
            }
            catch (Exception ex)
            {
                LogException("Quick edit drawer", ex);
                TryLibraryToast("Could not save: " + ex.Message, MessageBoxImage.Warning);
            }
        }

        internal void LibraryBrowserOpenSourceFoldersForCurrentSelection(LibraryBrowserWorkingSet ws)
        {
            if (ws?.Current == null) return;
            foreach (var path in GetLibraryBrowserSourceFolderPaths(ws.Current))
                OpenFolder(path);
        }

        /// <summary>PV-PLN-V1POL-001 Slice G — drawer title/subtitle and button state from <see cref="LibraryBrowserWorkingSet.Current"/>.</summary>
        void LibraryBrowserRefreshQuickEditDrawerContent(LibraryBrowserWorkingSet ws)
        {
            if (ws?.QuickEditDrawerTitleText == null || ws.QuickEditDrawerSubtitleText == null) return;
            var metaBtn = ws.QuickEditDrawerMetadataButton;
            var folderBtn = ws.QuickEditDrawerOpenFolderButton;
            var steamBox = ws.QuickEditSteamAppIdBox;
            var notesBox = ws.QuickEditCollectionNotesBox;
            var applyBtn = ws.QuickEditDrawerApplyInlineButton;

            bool showInlineFields;
            bool inlineEditable;

            if (ws.Current == null)
            {
                ws.QuickEditDrawerTitleText.Text = "No folder selected";
                ws.QuickEditDrawerSubtitleText.Text = "Select a game folder in the list (not Timeline).";
                if (metaBtn != null) metaBtn.IsEnabled = false;
                if (folderBtn != null) folderBtn.IsEnabled = false;
                showInlineFields = false;
                inlineEditable = false;
            }
            else if (IsLibraryBrowserTimelineView(ws.Current))
            {
                ws.QuickEditDrawerTitleText.Text = "Timeline";
                ws.QuickEditDrawerSubtitleText.Text = "Pick a date range in the toolbar. Switch to folder list for per-game quick edits.";
                if (metaBtn != null) metaBtn.IsEnabled = false;
                if (folderBtn != null)
                {
                    folderBtn.IsEnabled = GetLibraryBrowserSourceFolderPaths(ws.Current).Count > 0;
                    folderBtn.Content = BuildLibraryBrowserOpenFoldersLabel(ws.Current);
                }

                showInlineFields = false;
                inlineEditable = false;
            }
            else if (ws.IsPhotoWorkspaceMode)
            {
                if (folderBtn != null) folderBtn.Content = BuildLibraryBrowserOpenFoldersLabel(ws.Current);
                ws.QuickEditDrawerTitleText.Text = ws.Current.Name ?? "Photo workspace";
                ws.QuickEditDrawerSubtitleText.Text = "Capture grid — use the detail toolbar for stars, comments, and batch actions.";
                if (metaBtn != null) metaBtn.IsEnabled = true;
                if (folderBtn != null) folderBtn.IsEnabled = GetLibraryBrowserSourceFolderPaths(ws.Current).Count > 0;
                showInlineFields = false;
                inlineEditable = false;
            }
            else
            {
                if (folderBtn != null) folderBtn.Content = BuildLibraryBrowserOpenFoldersLabel(ws.Current);
                ws.QuickEditDrawerTitleText.Text = ws.Current.Name ?? "(untitled)";
                var platform = CleanTag(ws.Current.PrimaryPlatformLabel ?? string.Empty);
                var gid = CleanTag(ws.Current.GameId ?? string.Empty);
                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(platform)) parts.Add(platform);
                if (!string.IsNullOrWhiteSpace(gid)) parts.Add("Game ID " + gid);
                var sub = string.Join(" · ", parts);
                if (string.IsNullOrWhiteSpace(sub))
                    sub = ws.Current.PrimaryFolderPath ?? string.Empty;
                if (string.IsNullOrWhiteSpace(sub))
                    sub = ws.Current.FileCount + " capture" + (ws.Current.FileCount == 1 ? string.Empty : "s");
                ws.QuickEditDrawerSubtitleText.Text = sub;
                if (metaBtn != null) metaBtn.IsEnabled = true;
                if (folderBtn != null) folderBtn.IsEnabled = GetLibraryBrowserSourceFolderPaths(ws.Current).Count > 0;
                showInlineFields = true;
                inlineEditable = !ws.Current.PendingGameAssignment;
            }

            if (steamBox != null)
            {
                steamBox.IsEnabled = showInlineFields && inlineEditable;
                if (showInlineFields && ws.Current != null)
                    steamBox.Text = DisplayExternalIdValue(ws.Current.SteamAppId ?? string.Empty);
                else
                    steamBox.Text = string.Empty;
            }

            if (notesBox != null)
            {
                notesBox.IsEnabled = showInlineFields && inlineEditable;
                if (showInlineFields && ws.Current != null)
                    notesBox.Text = ws.Current.CollectionNotes ?? string.Empty;
                else
                    notesBox.Text = string.Empty;
            }

            if (applyBtn != null)
                applyBtn.IsEnabled = showInlineFields && inlineEditable;
        }

        void LibraryBrowserSetQuickEditDrawerOpen(LibraryBrowserWorkingSet ws, bool open)
        {
            if (ws == null) return;
            ws.QuickEditDrawerOpen = open;
            if (ws.QuickEditDrawerHost != null)
                ws.QuickEditDrawerHost.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
            if (open) LibraryBrowserRefreshQuickEditDrawerContent(ws);
        }

        void LibraryBrowserToggleQuickEditDrawer(LibraryBrowserWorkingSet ws)
        {
            if (ws == null) return;
            LibraryBrowserSetQuickEditDrawerOpen(ws, !ws.QuickEditDrawerOpen);
        }
    }
}
