using System;
using System.Linq;
using System.Threading;
using System.Windows;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        void AttachManualMetadataFinishHandler(ManualMetadataDialogHost h, Action saveSelectedDateTime, Action refreshGameTitleChoices, Action refreshSelectionUi, Action refreshTileBadges)
        {
            h.FinishButton.Click += async delegate
            {
                if (!h.DialogReady || !h.ManualWindow.IsLoaded) return;
                var pendingItems = h.SelectedItems.Distinct().ToList();
                if (pendingItems.Count == 0)
                {
                    MessageBox.Show(importService.GetManualMetadataFinishEmptySelectionMessage(h.LibraryMode, h.ImportAndEditMode), "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                if (h.UseCustomTimeBox.IsChecked == true) saveSelectedDateTime();
                importService.ApplyManualMetadataTagTextToPlatformFlags(pendingItems);
                if (importService.ManualMetadataItemsMissingOtherPlatformName(pendingItems))
                {
                    MessageBox.Show("Enter a platform name in the Other box before applying changes.", "PixelVault", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                if (h.ImportAndEditMode)
                {
                    await importService.ApplyImportAndEditSteamStoreTitlesWhenGameNameUnchangedAsync(pendingItems.Where(i => i != null && !i.DeleteBeforeProcessing), CancellationToken.None).ConfigureAwait(true);
                }
                var gameRows = librarySession.LoadSavedGameIndexRows();
                var unresolvedMasterRecords = importService.BuildUnresolvedManualMetadataMasterRecordLabels(gameRows, pendingItems);
                if (unresolvedMasterRecords.Count > 0)
                {
                    var addChoice = MessageBox.Show(
                        importService.BuildManualMetadataAddNewGamePrompt(unresolvedMasterRecords, 8),
                        "Add New Game",
                        MessageBoxButton.OKCancel,
                        MessageBoxImage.Question);
                    if (addChoice != MessageBoxResult.OK) return;
                    foreach (var title in unresolvedMasterRecords.Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        if (h.KnownGameChoiceSet.Add(title)) h.KnownGameChoices.Add(title);
                    }
                    refreshGameTitleChoices();
                    importService.EnsureNewManualMetadataMasterRecordsInGameIndex(gameRows, pendingItems);
                }
                var confirm = MessageBox.Show(
                    importService.GetManualMetadataFinishConfirmBody(pendingItems.Count, h.LibraryMode, h.ImportAndEditMode),
                    h.ConfirmCaption,
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Question);
                if (confirm != MessageBoxResult.OK) return;
                importService.FinalizeManualMetadataItemsAgainstGameIndex(libraryRoot, gameRows, pendingItems);
                if (h.LibraryMode && !h.ImportAndEditMode)
                {
                    if (ContinueManualMetadataAfterLibraryApply(h, pendingItems, refreshGameTitleChoices, refreshSelectionUi, refreshTileBadges))
                        return;
                }
                else
                {
                    var distinctLabels = pendingItems
                        .Select(it => NormalizeGameIndexName(it.GameName, null))
                        .Where(l => !string.IsNullOrWhiteSpace(l))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList();
                    if (distinctLabels.Count > 0) PushManualMetadataRecentTitleLabels(distinctLabels);
                }
                h.Items.Clear();
                h.Items.AddRange(pendingItems);
                h.ManualWindow.DialogResult = true;
                h.ManualWindow.Close();
            };
        }
    }
}
