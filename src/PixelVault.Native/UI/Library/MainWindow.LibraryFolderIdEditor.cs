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
                TryLibraryToast("Choose a library folder first.");
                return;
            }
            if (string.IsNullOrWhiteSpace(libraryRoot) || !Directory.Exists(libraryRoot))
            {
                TryLibraryToast("Library folder not found. Check Path Settings before editing IDs.");
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
            var nonSteamIdBox = new TextBox
            {
                Text = savedRow == null ? DisplayExternalIdValue(folder.NonSteamId ?? string.Empty) : DisplayExternalIdValue(savedRow.NonSteamId ?? string.Empty),
                Padding = new Thickness(10, 7, 10, 7),
                Background = Brushes.White,
                BorderBrush = Brush("#D7E1E8"),
                BorderThickness = new Thickness(1),
                FontSize = 14
            };
            var retroAchievementsGameIdBox = new TextBox
            {
                Text = savedRow == null ? DisplayExternalIdValue(folder.RetroAchievementsGameId ?? string.Empty) : DisplayExternalIdValue(savedRow.RetroAchievementsGameId ?? string.Empty),
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
                Height = 590,
                MinWidth = 540,
                MinHeight = 560,
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
            header.Children.Add(new TextBlock { Text = "Update the saved Steam App ID, SteamGridDB ID, and RetroAchievements game ID for this game + platform row. Another console is a separate index row; several rows can share one disk folder when their storage group matches (see Game Index).", Margin = new Thickness(0, 10, 0, 0), Foreground = Brush("#5F6970"), FontSize = 13, TextWrapping = TextWrapping.Wrap });
            root.Children.Add(header);

            var form = new Grid { Margin = new Thickness(0, 0, 0, 12) };
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            form.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
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

            var nonSteamIdStack = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            nonSteamIdStack.Children.Add(new TextBlock { Text = "Non-Steam ID", FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2A30"), Margin = new Thickness(0, 0, 0, 6) });
            nonSteamIdStack.Children.Add(new TextBlock { Text = "Steam shortcut ID for imported non-Steam and emulator entries.", Foreground = Brush("#5F6970"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
            nonSteamIdStack.Children.Add(nonSteamIdBox);
            Grid.SetRow(nonSteamIdStack, 2);
            form.Children.Add(nonSteamIdStack);

            var retroStack = new StackPanel { Margin = new Thickness(0, 0, 0, 14) };
            retroStack.Children.Add(new TextBlock { Text = "RetroAchievements game ID", FontWeight = FontWeights.SemiBold, Foreground = Brush("#1F2A30"), Margin = new Thickness(0, 0, 0, 6) });
            retroStack.Children.Add(new TextBlock { Text = "Numeric game id from retroachievements.org (same id their API uses).", Foreground = Brush("#5F6970"), FontSize = 12, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6) });
            retroStack.Children.Add(retroAchievementsGameIdBox);
            Grid.SetRow(retroStack, 3);
            form.Children.Add(retroStack);

            var helperText = new TextBlock
            {
                Text = "Leave a field blank if you want to clear the saved value. Look up IDs fills empty Steam, SteamGridDB, and RetroAchievements fields when those services are configured.",
                Foreground = Brush("#5F6970"),
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 4, 0, 0)
            };
            Grid.SetRow(helperText, 4);
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
            lookupButton.ToolTip = "Fill empty ID fields using the folder name: Steam store search, SteamGridDB (needs token in Settings), and RetroAchievements game list search (needs API key in Settings). Won’t replace text you already typed. You may get a picker when multiple sites return several matches.";
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
                    TryLibraryToast("This folder has no name to search with.");
                    return;
                }
                cancelButton.IsEnabled = false;
                lookupButton.IsEnabled = false;
                saveButton.IsEnabled = false;
                appIdBox.IsEnabled = false;
                steamGridDbIdBox.IsEnabled = false;
                nonSteamIdBox.IsEnabled = false;
                retroAchievementsGameIdBox.IsEnabled = false;
                try
                {
                    var existingApp = CleanTag(appIdBox.Text);
                    var existingGrid = CleanTag(steamGridDbIdBox.Text);
                    var existingRa = CleanTag(retroAchievementsGameIdBox.Text);
                    string resolvedApp = null;
                    string resolvedGrid = null;
                    string resolvedRa = null;
                    var steamReturnedMatches = false;
                    var steamUserSelected = false;
                    var raReturnedMatches = false;
                    var raUserSelected = false;
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
                    if (string.IsNullOrWhiteSpace(existingRa) && HasRetroAchievementsApiKey())
                    {
                        var raMatches = await coverService.SearchRetroAchievementsGameMatchesAsync(queryTitle, folder.PlatformLabel ?? string.Empty, CancellationToken.None).ConfigureAwait(true);
                        if (raMatches != null && raMatches.Count > 0)
                        {
                            raReturnedMatches = true;
                            var chosenRa = raMatches.Count == 1 ? raMatches[0] : ShowRetroAchievementsGameMatchWindow(editorWindow, queryTitle, raMatches);
                            if (chosenRa != null)
                            {
                                resolvedRa = chosenRa.Item1;
                                raUserSelected = true;
                            }
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(resolvedApp)) appIdBox.Text = resolvedApp;
                    if (!string.IsNullOrWhiteSpace(resolvedGrid)) steamGridDbIdBox.Text = resolvedGrid;
                    if (!string.IsNullOrWhiteSpace(resolvedRa)) retroAchievementsGameIdBox.Text = resolvedRa;
                    var filledApp = !string.IsNullOrWhiteSpace(resolvedApp);
                    var filledGrid = !string.IsNullOrWhiteSpace(resolvedGrid);
                    var filledRa = !string.IsNullOrWhiteSpace(resolvedRa);
                    if (!filledApp && !filledGrid && !filledRa)
                    {
                        string hint;
                        if (steamReturnedMatches && !steamUserSelected)
                            hint = "Steam lookup canceled. ID fields were not changed.";
                        else if (raReturnedMatches && !raUserSelected)
                            hint = "RetroAchievements lookup canceled. ID fields were not changed.";
                        else if (string.IsNullOrWhiteSpace(existingApp) && !steamReturnedMatches)
                            hint = "No Steam store results for \"" + queryTitle + "\".";
                        else
                            hint = "No new IDs were filled.";
                        if (string.IsNullOrWhiteSpace(existingGrid) && !HasSteamGridDbApiToken())
                            hint += Environment.NewLine + Environment.NewLine + "Add a SteamGridDB API token in Settings to enable SteamGridDB ID lookup.";
                        else if (string.IsNullOrWhiteSpace(existingGrid) && HasSteamGridDbApiToken() && !string.IsNullOrWhiteSpace(appForGrid))
                            hint += Environment.NewLine + Environment.NewLine + "SteamGridDB did not return a single confident match for that title.";
                        if (string.IsNullOrWhiteSpace(existingRa) && !HasRetroAchievementsApiKey())
                            hint += Environment.NewLine + Environment.NewLine + "Add a RetroAchievements API key in Settings to enable RA game ID lookup.";
                        else if (string.IsNullOrWhiteSpace(existingRa) && HasRetroAchievementsApiKey() && !raReturnedMatches)
                            hint += Environment.NewLine + Environment.NewLine + "RetroAchievements had no title matches for \"" + queryTitle + "\" on consoles guessed from platform \"" + (folder.PlatformLabel ?? string.Empty) + "\".";
                        TryLibraryToast(hint.Replace(Environment.NewLine, " "));
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
                        if (HasRetroAchievementsApiKey())
                        {
                            if (filledRa) lines.Add("Filled RetroAchievements game ID: " + resolvedRa);
                            else if (string.IsNullOrWhiteSpace(existingRa) && !raReturnedMatches) lines.Add("RetroAchievements: no matches for this title/platform.");
                            else if (string.IsNullOrWhiteSpace(existingRa) && raReturnedMatches && !raUserSelected) lines.Add("RetroAchievements: picker canceled.");
                        }
                        else if (string.IsNullOrWhiteSpace(existingRa)) lines.Add("RetroAchievements ID: skipped (no API key in Settings).");
                        TryLibraryToast(string.Join(" · ", lines));
                    }
                }
                catch (Exception ex)
                {
                    LogException("Look up folder IDs", ex);
                    TryLibraryToast("ID lookup failed: " + ex.Message, MessageBoxImage.Warning);
                }
                finally
                {
                    cancelButton.IsEnabled = true;
                    lookupButton.IsEnabled = true;
                    saveButton.IsEnabled = true;
                    appIdBox.IsEnabled = true;
                    steamGridDbIdBox.IsEnabled = true;
                    nonSteamIdBox.IsEnabled = true;
                    retroAchievementsGameIdBox.IsEnabled = true;
                }
            };
            saveButton.Click += delegate
            {
                try
                {
                    var steamAppId = CleanTag(appIdBox.Text);
                    var nonSteamId = CleanTag(nonSteamIdBox.Text);
                    var steamGridDbId = CleanTag(steamGridDbIdBox.Text);
                    var retroAchievementsGameId = CleanTag(retroAchievementsGameIdBox.Text);
                    var rows = librarySession.LoadSavedGameIndexRows();
                    var row = FindSavedGameIndexRow(rows, folder);
                    if (row == null)
                    {
                        if (string.IsNullOrWhiteSpace(steamAppId) && string.IsNullOrWhiteSpace(nonSteamId) && string.IsNullOrWhiteSpace(steamGridDbId) && string.IsNullOrWhiteSpace(retroAchievementsGameId))
                        {
                            editorWindow.Close();
                            return;
                        }
                        row = EnsureGameIndexRowForAssignment(rows, folder.Name, folder.PlatformLabel, folder.GameId);
                    }
                    if (row == null)
                    {
                        status.Text = "Cannot add IDs: set a game title or open an indexed game folder first.";
                        return;
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
                    row.NonSteamId = nonSteamId;
                    row.SteamGridDbId = steamGridDbId;
                    row.SuppressSteamAppIdAutoResolve = ShouldSuppressExternalIdAutoResolve(steamAppId, previousSteamAppId, previousSuppressSteamAppId);
                    row.SuppressSteamGridDbIdAutoResolve = ShouldSuppressExternalIdAutoResolve(steamGridDbId, previousSteamGridDbId, previousSuppressSteamGridDbId);
                    row.RetroAchievementsGameId = retroAchievementsGameId;
                    SaveGameIndexEditorRows(libraryRoot, rows);
                    folder.SteamAppId = steamAppId;
                    folder.NonSteamId = nonSteamId;
                    folder.SteamGridDbId = steamGridDbId;
                    folder.RetroAchievementsGameId = retroAchievementsGameId;
                    folder.SuppressSteamAppIdAutoResolve = row.SuppressSteamAppIdAutoResolve;
                    folder.SuppressSteamGridDbIdAutoResolve = row.SuppressSteamGridDbIdAutoResolve;
                    status.Text = "Folder IDs saved";
                    Log("Updated IDs for " + (folder.Name ?? "folder") + " | " + NormalizeConsoleLabel(folder.PlatformLabel) + " | AppID=" + (string.IsNullOrWhiteSpace(steamAppId) ? "(blank)" : steamAppId) + (row.SuppressSteamAppIdAutoResolve ? " [manual clear]" : string.Empty) + " | Non-Steam=" + (string.IsNullOrWhiteSpace(nonSteamId) ? "(blank)" : nonSteamId) + " | STID=" + (string.IsNullOrWhiteSpace(steamGridDbId) ? "(blank)" : steamGridDbId) + (row.SuppressSteamGridDbIdAutoResolve ? " [manual clear]" : string.Empty) + " | RA=" + (string.IsNullOrWhiteSpace(retroAchievementsGameId) ? "(blank)" : retroAchievementsGameId));
                    if (refreshLibrary != null) refreshLibrary();
                    editorWindow.Close();
                }
                catch (Exception ex)
                {
                    LogException("Save folder IDs", ex);
                    TryLibraryToast("Could not save folder IDs: " + ex.Message, MessageBoxImage.Warning);
                }
            };

            editorWindow.Content = root;
            editorWindow.ShowDialog();
        }
    }
}
