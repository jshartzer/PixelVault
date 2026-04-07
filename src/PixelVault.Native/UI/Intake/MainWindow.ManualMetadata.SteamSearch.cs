using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        void AttachManualMetadataSteamSearchHandler(ManualMetadataDialogHost h, Action refreshGameTitleChoices, Action syncSelectedGameNames, Action refreshTileBadges, Action refreshSelectionStatus, Action refreshSelectionUi)
        {
            h.SteamSearchButton.Click += delegate
            {
                if (h.SteamSearchCancellation != null)
                {
                    h.SteamLookupStatus.Text = "Canceling Steam search...";
                    h.SteamSearchCancellation.Cancel();
                    return;
                }
                if (h.SelectedItems.Count == 0)
                {
                    TryLibraryToast("Select one or more captures before searching Steam.");
                    return;
                }
                var query = CleanTag(h.SteamSearchBox.Text);
                string mappedName;
                if (h.KnownGameChoiceNameMap.TryGetValue(query, out mappedName)) query = mappedName;
                else query = ExtractGameNameFromChoiceLabel(query);
                if (string.IsNullOrWhiteSpace(query))
                {
                    query = CleanTag(h.GameNameBox.Text);
                    if (h.KnownGameChoiceNameMap.TryGetValue(query, out mappedName)) query = mappedName;
                    else query = ExtractGameNameFromChoiceLabel(query);
                }
                if (string.IsNullOrWhiteSpace(query))
                {
                    var firstItem = h.SelectedItems[0];
                    if (firstItem != null && !string.IsNullOrWhiteSpace(firstItem.GameName)) query = CleanTag(firstItem.GameName);
                }
                if (string.IsNullOrWhiteSpace(query))
                {
                    TryLibraryToast("Enter a game title or a numeric Steam AppID in the search box, then click Search Steam.");
                    return;
                }

                h.SteamLookupStatus.Text = "Searching Steam for \"" + query + "\"...";
                var searchQuery = query;
                var searchCancellation = new CancellationTokenSource();
                h.SteamSearchCancellation = searchCancellation;
                var searchVersion = ++h.SteamSearchRequestVersion;
                h.SteamSearchButton.Content = "Cancel Search";
                Task.Run(async () =>
                {
                    searchCancellation.Token.ThrowIfCancellationRequested();
                    var matches = await coverService.SearchSteamAppMatchesAsync(searchQuery, searchCancellation.Token).ConfigureAwait(false);
                    return Tuple.Create(searchQuery, matches);
                }, searchCancellation.Token).ContinueWith(delegate(Task<Tuple<string, List<Tuple<string, string>>>> searchTask)
                {
                    Dispatcher.BeginInvoke(new Action(delegate
                    {
                        if (ReferenceEquals(h.SteamSearchCancellation, searchCancellation))
                        {
                            h.SteamSearchCancellation.Dispose();
                            h.SteamSearchCancellation = null;
                        }
                        h.SteamSearchButton.Content = "Search Steam";
                        if (!h.ManualWindow.IsLoaded || searchVersion != h.SteamSearchRequestVersion) return;
                        if (searchTask.IsCanceled || searchCancellation.IsCancellationRequested)
                        {
                            h.SteamLookupStatus.Text = "Steam search canceled. Pick a match or paste the AppID directly.";
                            return;
                        }
                        if (searchTask.IsFaulted)
                        {
                            h.SteamLookupStatus.Text = "Steam lookup failed. Try again or paste the AppID directly.";
                            return;
                        }
                        var result = searchTask.Result;
                        var matches = result == null || result.Item2 == null ? new List<Tuple<string, string>>() : result.Item2;
                        if (matches.Count == 0)
                        {
                            h.SteamLookupStatus.Text = "No Steam AppID match found for \"" + searchQuery + "\".";
                            return;
                        }

                        var chosenMatch = matches.Count == 1 ? matches[0] : ShowSteamAppMatchWindow(h.ManualWindow, searchQuery, matches);
                        if (chosenMatch == null)
                        {
                            h.SteamLookupStatus.Text = "Steam search canceled. Pick a match or paste the AppID directly.";
                            return;
                        }

                        var resolvedAppId = chosenMatch.Item1 ?? string.Empty;
                        var resolvedTitle = chosenMatch.Item2 ?? string.Empty;

                        h.SuppressSync = true;
                        h.SteamSearchBox.Text = string.IsNullOrWhiteSpace(resolvedTitle) ? searchQuery : resolvedTitle;
                        h.SteamAppIdBox.Text = resolvedAppId;
                        h.SuppressSync = false;
                        foreach (var item in h.SelectedItems)
                        {
                            item.SteamAppId = resolvedAppId;
                            if (!string.IsNullOrWhiteSpace(resolvedTitle)) item.GameName = resolvedTitle;
                        }
                        ApplyConsolePlatformToManualMetadataItems(h.SelectedItems, "Steam");
                        refreshTileBadges();
                        if (!string.IsNullOrWhiteSpace(resolvedTitle))
                        {
                            var choiceName = NormalizeGameIndexName(resolvedTitle, null);
                            if (!string.IsNullOrWhiteSpace(choiceName) && h.KnownGameChoiceSet.Add(choiceName)) h.KnownGameChoices.Add(choiceName);
                            refreshGameTitleChoices();
                            h.SuppressSync = true;
                            h.GameNameBox.Text = resolvedTitle;
                            h.SuppressSync = false;
                            syncSelectedGameNames();
                        }
                        else
                        {
                            h.SteamLookupStatus.Text = "Selected Steam AppID " + resolvedAppId + ".";
                            refreshSelectionStatus();
                        }
                        refreshSelectionUi();
                    }));
                }, TaskScheduler.Default);
            };
        }
    }
}
