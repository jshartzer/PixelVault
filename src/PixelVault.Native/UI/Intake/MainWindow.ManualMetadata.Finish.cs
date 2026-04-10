using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Windows;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        void AttachManualMetadataFinishHandler(ManualMetadataDialogHost h, Action flushPendingFieldEditsToSelectedItems, Action saveSelectedDateTime, Action refreshGameTitleChoices, Action refreshSelectionUi, Action refreshTileBadges)
        {
            h.FinishButton.Click += async delegate
            {
                if (!h.DialogReady || !h.ManualWindow.IsLoaded) return;
                var pendingItems = h.SelectedItems.Distinct().ToList();
                if (pendingItems.Count == 0)
                {
                    TryLibraryToast(importService.GetManualMetadataFinishEmptySelectionMessage(h.LibraryMode, h.ImportAndEditMode));
                    return;
                }
                // Ensure ComboBox/TextBox edits are copied to ManualMetadataItem before finalize (focus order can skip LostFocus/TextChanged).
                flushPendingFieldEditsToSelectedItems?.Invoke();
                if (h.UseCustomTimeBox.IsChecked == true) saveSelectedDateTime();
                importService.ApplyManualMetadataTagTextToPlatformFlags(pendingItems);
                if (importService.ManualMetadataItemsMissingOtherPlatformName(pendingItems))
                {
                    TryLibraryToast("Enter a platform name in the Other box before applying changes.");
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
                    {
                        // Partial apply: dialog stays open, so the normal post-close RunLibraryMetadataWorkflowWithProgress never runs.
                        // Persist EXIF, organize, and photo index for this batch now; otherwise GameId/title changes only hit the game index on disk.
                        RunLibraryMetadataWorkflowWithProgress(null, new List<ManualMetadataItem>(pendingItems), null);
                        return;
                    }
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
