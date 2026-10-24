using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace PixelVaultNative
{
    sealed partial class AchievementTrackerWindow
    {
        Button automationButton, pauseAutomatic;
        DispatcherTimer automaticStatusTimer;
        string lastAutomaticStatus;
        TrackerAutomationSettings activeAutomation;
        TrackerAutomationState activeAutomaticState;
        internal Func<TrackerAutomationWindow, bool?> ShowAutomation = dialog => dialog.ShowDialog();

        void BuildAutomationActions(WrapPanel panel)
        {
            automationButton = Button("Automatic refresh…", "Choose when this source refreshes while PixelVault runs");
            automationButton.Click += async (_, __) => await ConfigureAutomationAsync(); panel.Children.Add(automationButton);
            pauseAutomatic = Button("Pause automatic", "Pause or resume this source's automatic refresh");
            pauseAutomatic.Click += async (_, __) =>
            {
                if (selected == null || activeSource == null) return;
                var id = selected.Id; var account = Account(selected); var binding = activeSource;
                try
                {
                    await Task.Run(() => { var settings = store.Automation(id, account).Settings; store.SaveAutomation(id, account, binding.Id, settings.Mode, settings.IntervalMinutes, !settings.Paused); });
                    await ReloadAsync();
                }
                catch (Exception ex) { Error(ex.Message); }
            };
            panel.Children.Add(pauseAutomatic);
            automaticStatusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            automaticStatusTimer.Tick += async (_, __) => await PollAutomaticStatusAsync();
            Loaded += (_, __) => automaticStatusTimer.Start(); Closed += (_, __) => automaticStatusTimer.Stop();
        }
        internal async Task PollAutomaticStatusAsync()
        {
            if (closed || refreshing || selected == null || !PendingEdit.IsCompleted || DraftPending()) return;
            var definition = selected; var account = Account(definition); if (string.IsNullOrWhiteSpace(account)) return;
            try
            {
                var stamp = await Task.Run(() => { var a = store.Automation(definition.Id, account); var s = store.SourceStatus(definition.Id, account); return a.Settings.Id + "|" + a.State.LastCheckUtc + "|" + s?.Outcome + "|" + s?.AttemptId; });
                if (!closed && !DraftPending() && selected == definition && lastAutomaticStatus != stamp)
                { await ReloadAsync(preserveDraft: true); if (!DraftPending()) lastAutomaticStatus = stamp; }
            }
            catch (Exception ex) { if (!closed) Error("Could not read automatic refresh status. " + ex.Message); }
        }
        bool DraftPending() => selectedItem != null && (notes.Text != selectedItem.Note || selectedItem.Item.Target > 1 && current.Text != (selectedItem.Current?.ToString() ?? ""));
        void UpdateAutomationControls()
        {
            if (automationButton == null) return;
            automationButton.IsEnabled = activeSource != null && !refreshing;
            var settings = activeAutomation;
            pauseAutomatic.Visibility = activeSource != null && settings != null && settings.Mode != TrackerAutomationMode.Manual ? Visibility.Visible : Visibility.Collapsed;
            pauseAutomatic.IsEnabled = activeSource != null && !refreshing;
            pauseAutomatic.Content = settings?.Paused == true ? "Resume automatic" : "Pause automatic";
            automationButton.ToolTip = "Automatic: " + (settings?.Mode.ToString() ?? "Manual") + (settings?.Paused == true ? " (paused)" : "") + "\n" + activeAutomaticState?.Message;
        }
        internal async Task ConfigureAutomationAsync()
        {
            if (refreshing || selected == null || activeSource == null || HasDraft()) return;
            var definition = selected; var account = Account(definition); var binding = activeSource;
            var currentSettings = await Task.Run(() => (Automatic: store.Automation(definition.Id, account), History: store.AutomationHistory(definition.Id, account)));
            if (closed) return;
            var dialog = new TrackerAutomationWindow(this, currentSettings.Automatic.Settings, currentSettings.Automatic.State, currentSettings.History);
            if (ShowAutomation(dialog) != true) { dialog.Close(); return; }
            try
            {
                await Task.Run(() => store.SaveAutomation(definition.Id, account, binding.Id, dialog.Mode, dialog.IntervalMinutes, dialog.Paused));
                store.AutomationMonitor?.Opened(definition.Id, account); await ReloadAsync();
            }
            catch (Exception ex) { Error("Could not save automatic refresh. " + ex.Message); }
            finally { dialog.Close(); }
        }
    }
}
