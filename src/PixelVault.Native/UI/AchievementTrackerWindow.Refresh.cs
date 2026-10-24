using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace PixelVaultNative
{
    sealed partial class AchievementTrackerWindow
    {
        TrackerRefreshService refreshService;
        Button refreshSource, configureSource, history, cancelRead;
        TrackerSourceBinding activeSource;
        TrackerSourceStatus activeSourceStatus;
        CancellationTokenSource refreshCancellation;
        WrapPanel definitionActions;
        bool refreshing;
        internal Task PendingRefresh { get; private set; } = Task.CompletedTask;
        internal Func<TrackerReconcileWindow, bool?> ShowReconciliation = dialog => dialog.ShowDialog();
        internal Func<TrackerSourceWindow, bool?> ShowSource = dialog => dialog.ShowDialog();
        readonly TextBlock refreshHint = Text("",14);

        FrameworkElement BuildRefreshActions(Button close)
        {
            refreshService = new TrackerRefreshService(store);
            var panel = new WrapPanel();
            configureSource = Button("Source…", "Configure or inspect this tracker's refresh source");
            configureSource.Click += async (_, __) => await ConfigureSourceAsync(); panel.Children.Add(configureSource);
            refreshSource = Button("Refresh", "Reread the configured source and review changes");
            refreshSource.Click += async (_, __) => await (PendingRefresh = RefreshAsync()); panel.Children.Add(refreshSource);
            BuildAutomationActions(panel);
            history = Button("History / undo", "Inspect and undo manual changes or an entire import or refresh");
            history.Click += async (_, __) =>
            {
                var definition = selected; if (definition == null || refreshing) return;
                await PendingEdit; if (HasDraft()) return;
                new TrackerHistoryWindow(this, store, definition.Id, Account(definition)).ShowDialog(); await ReloadAsync();
            };
            panel.Children.Add(history);
            cancelRead = Button("Cancel refresh", "Cancel the active source read without changing progress");
            cancelRead.Visibility = Visibility.Collapsed; cancelRead.Click += (_, __) => { if (refreshCancellation != null) refreshCancellation.Cancel(); else store.AutomationMonitor?.Cancel(); }; panel.Children.Add(cancelRead);
            panel.Children.Add(close); UpdateRefreshControls(); return panel;
        }
        void UpdateRefreshControls()
        {
            if (refreshSource == null) return;
            var connection=selected==null?null:refreshService.Connection(selected,activeSource,activeAutomation,activeAutomaticState);
            connectionIndicator.Text=connection?.Label??"";connectionIndicator.ToolTip=connection?.Detail;
            connectionIndicator.Foreground=SystemParameters.HighContrast?SystemColors.WindowTextBrush:Color(connection?.GameFilesConnected==true?"#91D9B5":"#E5C58F");
            bool account = selected != null && !string.IsNullOrWhiteSpace(Account(selected));
            configureSource.IsEnabled = history.IsEnabled = account && !refreshing;
            // A refresh is also the recovery/setup entry point; a missing cached binding must not strand the user.
            refreshSource.IsEnabled = selected != null && !refreshing;
            var reason=selected==null?"Choose a tracker to refresh its progress.":!account?"Connect this game's account in Settings before refreshing progress."
                :activeSource==null?"No source connected. Refresh opens source setup; supported game trackers offer their save reader.":"";
            refreshHint.Text=reason;refreshHint.Visibility=reason.Length>0?Visibility.Visible:Visibility.Collapsed;
            refreshSource.ToolTip = reason.Length>0?reason:TrackerSourceWindow.Describe(activeSource, activeSourceStatus);
            System.Windows.Automation.AutomationProperties.SetHelpText(refreshSource,reason.Length>0?reason:"Read the connected source again and review progress changes.");
            cancelRead.Visibility = refreshing || activeSourceStatus?.Outcome == TrackerRefreshOutcome.Reading ? Visibility.Visible : Visibility.Collapsed;
            UpdateAutomationControls();
        }
        void ReadBusy(bool busy)
        {
            refreshing = busy; trackers.IsEnabled = list.IsEnabled = definitionActions.IsEnabled = !busy;
            if (busy) status.Foreground = Color("#D9E6ED");
            if (busy) saveNote.IsEnabled = saveCount.IsEnabled = reset.IsEnabled = notes.IsEnabled = current.IsEnabled = false;
            else ShowItem(selectedItem);
            UpdateRefreshControls();
        }
        bool HasDraft()
        {
            if (selectedItem != null && notes.Text != selectedItem.Note) { Error("Save your note before continuing with source changes or history."); return true; }
            if (selectedItem?.Item.Target > 1 && current.Text != (selectedItem.Current?.ToString() ?? "")) { Error("Save the progress count before continuing with source changes or history."); return true; }
            return false;
        }
        async Task ConfigureSourceAsync()
        {
            var definition = selected; if (definition == null || refreshing || HasDraft()) return;
            var dialog = new TrackerSourceWindow(this, activeSource, activeSourceStatus, definition, Account(definition));
            if (ShowSource(dialog) != true) return;
            try
            {
                await Task.Run(() =>
                {
                    if (dialog.Remove) store.RemoveSource(definition.Id, Account(definition));
                    else refreshService.Configure(definition.Id, Account(definition), dialog.AdapterId, "1", dialog.SourcePath, dialog.Profile, dialog.GameDirectory, dialog.SpecificSave, dialog.KeepSnapshots, dialog.Spreadsheet);
                });
                await ReloadAsync();
            }
            catch (Exception ex) { Error("Could not configure source. " + ex.Message); }
        }
        internal async Task RefreshAsync()
        {
            if (refreshing || selected == null) return;
            await PendingEdit; if (HasDraft()) return;
            var definition = selected; var account = Account(definition); TrackerRefreshPlan plan = null;
            if(string.IsNullOrWhiteSpace(account)){Error("Connect this game's account in Settings before refreshing progress.");return;}
            try
            {
                var binding=await Task.Run(()=>store.Source(definition.Id,account));
                if(closed||selected!=definition)return;
                activeSource=binding;UpdateRefreshControls();
                if(activeSource==null)
                {
                    await ConfigureSourceAsync();
                    if(closed||selected!=definition||activeSource==null)return;
                }
            }
            catch(Exception ex){Error("Could not load the saved source connection. "+ex.Message);UpdateRefreshControls();return;}
            using var cancellation = new CancellationTokenSource(); refreshCancellation = cancellation; ReadBusy(true); status.Text = "Reading source…";
            try
            {
                plan = await Task.Run(() => refreshService.PrepareAsync(definition.Id, account, cancellation.Token));
                if (closed || cancellation.IsCancellationRequested) { refreshService.Cancel(plan); return; }
                var dialog = new TrackerReconcileWindow(this, plan);
                if (ShowReconciliation(dialog) != true) { dialog.Close(); refreshService.Cancel(plan); await ReloadAsync(); status.Text = "Refresh cancelled. Saved progress was preserved."; return; }
                var older = dialog.AllowOlder; var accept = dialog.UseSourceItems; dialog.Close();
                var count = await Task.Run(() => refreshService.Apply(plan, older, accept, cancellation.Token));
                if (!closed) { await ReloadAsync(); status.Text = count + " source observations applied. Review History / undo to inspect or reverse the batch."; }
            }
            catch (OperationCanceledException) { if (!closed) { await ReloadAsync(); status.Text = "Refresh cancelled. Saved progress was preserved."; } }
            catch (Exception ex) { if (!closed) { await ReloadAsync(); Error("Could not refresh. " + ex.Message); } }
            finally { refreshCancellation = null; if (!closed) ReadBusy(false); }
        }

        async Task ImportWithReviewAsync()
        {
            if (refreshing || selected == null) return;
            await PendingEdit; if (HasDraft()) return;
            var file = new OpenFileDialog { Title = "Import checklist progress", Filter = "Checklist progress (*.json)|*.json", CheckFileExists = true };
            if (file.ShowDialog(this) != true) return;
            var definition = selected; var account = Account(definition);
            using var cancellation = new CancellationTokenSource(); refreshCancellation = cancellation; ReadBusy(true);
            try
            {
                var observation = await new JsonFileTrackerSource().ObserveAsync(new TrackerSourceBinding { AdapterId = JsonFileTrackerSource.AdapterId, AdapterVersion = "1", Location = file.FileName }, definition, cancellation.Token);
                var plan = await Task.Run(() => store.Preview(observation.SnapshotJson, definition.Id, account, "json-import", "1", observation.Fingerprint));
                if (closed || cancellation.IsCancellationRequested) return;
                var dialog = new TrackerReconcileWindow(this, plan);
                if (ShowReconciliation(dialog) != true) { dialog.Close(); status.Text = "Import cancelled. Saved progress was preserved."; return; }
                var older = dialog.AllowOlder; var accept = dialog.UseSourceItems; dialog.Close();
                var count = await Task.Run(() => refreshService.Apply(plan, older, accept, cancellation.Token));
                if (!closed) { await ReloadAsync(); status.Text = count + " observations imported. This batch can be undone in History / undo."; }
            }
            catch (OperationCanceledException) { if (!closed) status.Text = "Import cancelled. Saved progress was preserved."; }
            catch (Exception ex) { if (!closed) Error("Could not import progress. " + ex.Message); }
            finally { refreshCancellation = null; if (!closed) ReadBusy(false); }
        }
    }
}
