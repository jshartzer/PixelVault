using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        void OpenLibraryFolderIdEditor(LibraryFolderInfo folder, Action refreshLibrary)
        {
            if (folder == null)
            {
                MessageBox.Show("Choose a library folder first.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            if (string.IsNullOrWhiteSpace(libraryRoot) || !Directory.Exists(libraryRoot))
            {
                MessageBox.Show("Library folder not found. Check Settings before editing IDs.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var savedRows = librarySession.LoadSavedGameIndexRows();
            var savedRow = FindSavedGameIndexRow(savedRows, folder);
            var appIdBox = new TextBox
            {
                Text = savedRow == null ? DisplayExternalIdValue(folder.SteamAppId ?? string.Empty) : DisplayExternalIdValue(savedRow.SteamAppId ?? string.Empty),
                Padding = new Thickness(10, 7, 10, 7),
                Background = Brushes.White,
                BorderBrush = Brush("#D7E1E8"),
                BorderThickness = new Thickness(1),
                FontSize = 14
            };
            var steamGridDbIdBox = new TextBox
            {
                Text = savedRow == null ? DisplayExternalIdValue(folder.SteamGridDbId ?? string.Empty) : DisplayExternalIdValue(savedRow.SteamGridDbId ?? string.Empty),
                Padding = new Thickness(10, 7, 10, 7),
                Background = Brushes.White,
                BorderBrush = Brush("#D7E1E8"),
                BorderThickness = new Thickness(1),
                FontSize = 14
            };

            var editorWindow = new Window
            {
                Title = "PixelVault " + AppVersion + " Edit IDs",
                Width = 560,
                Height = 430,
                MinWidth = 540,
                MinHeight = 410,
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Background = Brush("#F3EEE4"),
                ResizeMode = ResizeMode.NoResize
            };

            var root = new Grid { Margin = new Thickness(22) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var header = new StackPanel { Margin = new Thickness(0, 0, 0, 18) };
            header.Children.Add(new TextBlock { Text = folder.Name ?? "Selected Folder", FontSize = 24, FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2A30"), TextWrapping = TextWrapping.Wrap });
            header.Children.Add(new TextBlock { Text = NormalizeConsoleLabel(folder.PlatformLabel), Margin = new Thickness(0, 6, 0, 0), Foreground = Brush("#5F6970"), FontSize = 13 });
            header.Children.Add(new TextBlock { Text = "Update the saved Steam App ID and SteamGridDB ID for this game record without leaving the Library view.", Margin = new Thickness(0, 10, 0, 0), Foreground = Brush("#5F6970"), FontSize = 13, TextWrapping = TextWrapping.Wrap });
            root.Children.Add(header);

            var form = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(form, 1);
            root.Children.Add(form);

            var appIdStack = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            appIdStack.Children.Add(new TextBlock { Text = "Steam App ID", FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2A30"), Margin = new Thickness(0, 0, 0, 6) });
            appIdStack.Children.Add(appIdBox);
            form.Children.Add(appIdStack);

            var steamGridDbIdStack = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            steamGridDbIdStack.Children.Add(new TextBlock { Text = "SteamGridDB ID", FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2A30"), Margin = new Thickness(0, 0, 0, 6) });
            steamGridDbIdStack.Children.Add(steamGridDbIdBox);
            Grid.SetRow(steamGridDbIdStack, 1);
            form.Children.Add(steamGridDbIdStack);

            var helperText = new TextBlock
            {
                Text = "Leave a field blank if you want to clear the saved value.",
                Foreground = Brush("#5F6970"),
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            };
            Grid.SetRow(helperText, 2);
            form.Children.Add(helperText);

            var actions = new Grid { HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            var cancelButton = Btn("Cancel", null, "#EEF2F5", Brush("#33424D"));
            cancelButton.Width = 122;
            cancelButton.Height = 44;
            cancelButton.Margin = new Thickness(0, 0, 10, 0);
            cancelButton.VerticalAlignment = VerticalAlignment.Top;
            var lookupButton = Btn("Look up IDs", null, "#20343A", Brushes.White);
            lookupButton.Width = 158;
            lookupButton.Height = 44;
            lookupButton.Margin = new Thickness(0, 0, 10, 0);
            lookupButton.VerticalAlignment = VerticalAlignment.Top;
            lookupButton.ToolTip = "Search Steam (pick from the match list when several games match) and SteamGridDB using the folder name above, then fill any empty ID fields (won’t replace text you already typed).";
            var saveButton = Btn("Save", null, "#275D47", Brushes.White);
            saveButton.Width = 122;
            saveButton.Height = 44;
            saveButton.Margin = new Thickness(0);
            saveButton.VerticalAlignment = VerticalAlignment.Top;
            actions.Children.Add(cancelButton);
            Grid.SetColumn(lookupButton, 1);
            actions.Children.Add(lookupButton);
            Grid.SetColumn(saveButton, 2);
            actions.Children.Add(saveButton);
            Grid.SetRow(actions, 2);
            root.Children.Add(actions);

            cancelButton.Click += delegate { editorWindow.Close(); };
            lookupButton.Click += async delegate
            {
                var queryTitle = CleanTag(folder.Name ?? string.Empty);
                if (string.IsNullOrWhiteSpace(queryTitle))
                {
                    MessageBox.Show("This folder has no name to search with.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                cancelButton.IsEnabled = false;
                lookupButton.IsEnabled = false;
                saveButton.IsEnabled = false;
                appIdBox.IsEnabled = false;
                steamGridDbIdBox.IsEnabled = false;
                try
                {
                    var existingApp = CleanTag(appIdBox.Text);
                    var existingGrid = CleanTag(steamGridDbIdBox.Text);
                    string resolvedApp = null;
                    string resolvedGrid = null;
                    var steamReturnedMatches = false;
                    var steamUserSelected = false;
                    if (string.IsNullOrWhiteSpace(existingApp))
                    {
                        var matches = await coverService.SearchSteamAppMatchesAsync(queryTitle, CancellationToken.None).ConfigureAwait(true);
                        if (matches != null && matches.Count > 0)
                        {
                            steamReturnedMatches = true;
                            var chosen = matches.Count == 1 ? matches[0] : ShowSteamAppMatchWindow(editorWindow, queryTitle, matches);
                            if (chosen != null)
                            {
                                resolvedApp = chosen.Item1;
                                steamUserSelected = true;
                            }
                        }
                    }
                    var appForGrid = !string.IsNullOrWhiteSpace(existingApp) ? existingApp : resolvedApp;
                    if (string.IsNullOrWhiteSpace(existingGrid) && HasSteamGridDbApiToken())
                    {
                        if (!string.IsNullOrWhiteSpace(appForGrid))
                            resolvedGrid = await coverService.TryResolveSteamGridDbIdBySteamAppIdAsync(appForGrid, CancellationToken.None).ConfigureAwait(true);
                        if (string.IsNullOrWhiteSpace(resolvedGrid))
                            resolvedGrid = await coverService.TryResolveSteamGridDbIdByNameAsync(queryTitle, CancellationToken.None).ConfigureAwait(true);
                    }
                    if (!string.IsNullOrWhiteSpace(resolvedApp)) appIdBox.Text = resolvedApp;
                    if (!string.IsNullOrWhiteSpace(resolvedGrid)) steamGridDbIdBox.Text = resolvedGrid;
                    var filledApp = !string.IsNullOrWhiteSpace(resolvedApp);
                    var filledGrid = !string.IsNullOrWhiteSpace(resolvedGrid);
                    if (!filledApp && !filledGrid)
                    {
                        string hint;
                        if (steamReturnedMatches && !steamUserSelected)
                            hint = "Steam lookup canceled. ID fields were not changed.";
                        else if (string.IsNullOrWhiteSpace(existingApp) && !steamReturnedMatches)
                            hint = "No Steam store results for \"" + queryTitle + "\".";
                        else
                            hint = "No new IDs were filled.";
                        if (string.IsNullOrWhiteSpace(existingGrid) && !HasSteamGridDbApiToken())
                            hint += Environment.NewLine + Environment.NewLine + "Add a SteamGridDB API token in Settings to enable SteamGridDB ID lookup.";
                        else if (string.IsNullOrWhiteSpace(existingGrid) && HasSteamGridDbApiToken() && !string.IsNullOrWhiteSpace(appForGrid))
                            hint += Environment.NewLine + Environment.NewLine + "SteamGridDB did not return a single confident match for that title.";
                        MessageBox.Show(hint, "Look up IDs", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        var lines = new List<string>();
                        if (filledApp) lines.Add("Filled Steam App ID: " + resolvedApp);
                        else if (string.IsNullOrWhiteSpace(existingApp) && !steamReturnedMatches) lines.Add("Steam App ID: no store results for \"" + queryTitle + "\".");
                        if (HasSteamGridDbApiToken())
                        {
                            if (filledGrid) lines.Add("Filled SteamGridDB ID: " + resolvedGrid);
                            else if (string.IsNullOrWhiteSpace(existingGrid)) lines.Add("SteamGridDB ID: no confident match (you can search the site manually).");
                        }
                        else if (string.IsNullOrWhiteSpace(existingGrid)) lines.Add("SteamGridDB ID: skipped (no API token in Settings).");
                        MessageBox.Show(string.Join(Environment.NewLine, lines), "Look up IDs", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                catch (Exception ex)
                {
                    LogException("Look up folder IDs", ex);
                    MessageBox.Show("Lookup failed." + Environment.NewLine + Environment.NewLine + ex.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                finally
                {
                    cancelButton.IsEnabled = true;
                    lookupButton.IsEnabled = true;
                    saveButton.IsEnabled = true;
                    appIdBox.IsEnabled = true;
                    steamGridDbIdBox.IsEnabled = true;
                }
            };
            saveButton.Click += delegate
            {
                try
                {
                    var steamAppId = CleanTag(appIdBox.Text);
                    var steamGridDbId = CleanTag(steamGridDbIdBox.Text);
                    var rows = librarySession.LoadSavedGameIndexRows();
                    var row = FindSavedGameIndexRow(rows, folder);
                    if (row == null)
                    {
                        if (string.IsNullOrWhiteSpace(steamAppId) && string.IsNullOrWhiteSpace(steamGridDbId))
                        {
                            editorWindow.Close();
                            return;
                        }
                        row = EnsureGameIndexRowForAssignment(rows, folder.Name, folder.PlatformLabel, folder.GameId);
                    }
                    row.Name = NormalizeGameIndexName(string.IsNullOrWhiteSpace(row.Name) ? folder.Name : row.Name, folder.FolderPath);
                    row.PlatformLabel = NormalizeConsoleLabel(string.IsNullOrWhiteSpace(row.PlatformLabel) ? folder.PlatformLabel : row.PlatformLabel);
                    row.FolderPath = string.IsNullOrWhiteSpace(folder.FolderPath) ? (row.FolderPath ?? string.Empty) : folder.FolderPath;
                    row.FileCount = folder.FileCount > 0 ? folder.FileCount : row.FileCount;
                    row.PreviewImagePath = string.IsNullOrWhiteSpace(folder.PreviewImagePath) ? (row.PreviewImagePath ?? string.Empty) : folder.PreviewImagePath;
                    row.FilePaths = folder.FilePaths == null || folder.FilePaths.Length == 0 ? (row.FilePaths ?? new string[0]) : folder.FilePaths;
                    var previousSteamAppId = row.SteamAppId;
                    var previousSteamGridDbId = row.SteamGridDbId;
                    var previousSuppressSteamAppId = row.SuppressSteamAppIdAutoResolve;
                    var previousSuppressSteamGridDbId = row.SuppressSteamGridDbIdAutoResolve;
                    row.SteamAppId = steamAppId;
                    row.SteamGridDbId = steamGridDbId;
                    row.SuppressSteamAppIdAutoResolve = ShouldSuppressExternalIdAutoResolve(steamAppId, previousSteamAppId, previousSuppressSteamAppId);
                    row.SuppressSteamGridDbIdAutoResolve = ShouldSuppressExternalIdAutoResolve(steamGridDbId, previousSteamGridDbId, previousSuppressSteamGridDbId);
                    SaveGameIndexEditorRows(libraryRoot, rows);
                    folder.SteamAppId = steamAppId;
                    folder.SteamGridDbId = steamGridDbId;
                    folder.SuppressSteamAppIdAutoResolve = row.SuppressSteamAppIdAutoResolve;
                    folder.SuppressSteamGridDbIdAutoResolve = row.SuppressSteamGridDbIdAutoResolve;
                    status.Text = "Folder IDs saved";
                    Log("Updated IDs for " + (folder.Name ?? "folder") + " | " + NormalizeConsoleLabel(folder.PlatformLabel) + " | AppID=" + (string.IsNullOrWhiteSpace(steamAppId) ? "(blank)" : steamAppId) + (row.SuppressSteamAppIdAutoResolve ? " [manual clear]" : string.Empty) + " | STID=" + (string.IsNullOrWhiteSpace(steamGridDbId) ? "(blank)" : steamGridDbId) + (row.SuppressSteamGridDbIdAutoResolve ? " [manual clear]" : string.Empty));
                    if (refreshLibrary != null) refreshLibrary();
                    editorWindow.Close();
                }
                catch (Exception ex)
                {
                    LogException("Save folder IDs", ex);
                    MessageBox.Show("Could not save the folder IDs." + Environment.NewLine + Environment.NewLine + ex.Message, "PixelVault", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            editorWindow.Content = root;
            editorWindow.ShowDialog();
        }
    }
}
