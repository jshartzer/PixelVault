using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace PixelVaultNative
{
    static class TrackerSourceFiles
    {
        internal static string Signature(IEnumerable<FileInfo> candidates, int maximumBytes, bool allowConcurrentWrites = false)
        {
            var files = candidates.ToList(); if (files.Count == 0) throw new InvalidDataException("No source files found.");
            var first = files[0]; long size = first.Length; var stamp = first.LastWriteTimeUtc;
            if (size > maximumBytes) throw new InvalidDataException("The source file exceeds the adapter's size limit.");
            string hash;
            using (var input = new FileStream(first.FullName, FileMode.Open, FileAccess.Read,
                allowConcurrentWrites ? FileShare.ReadWrite | FileShare.Delete : FileShare.Read))
            {
                hash = Convert.ToHexString(SHA256.HashData(input)); first.Refresh();
                if (input.Length != size || first.Length != size || first.LastWriteTimeUtc != stamp) throw new IOException("The source is still being saved. Waiting before retrying.");
            }
            // Hash the newest candidate too: cloud replacement can retain size and modification time.
            var text = hash + "\n" + string.Join("\n", files.Select(f => f.FullName + "|" + f.Length + "|" + f.LastWriteTimeUtc.Ticks));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        }
    }
    sealed record TrackerAutomationNotice(string TrackerId, string Account, string Title, string Message, bool NeedsReview);

    sealed partial class AchievementTrackingStore
    {
        internal TrackerAutomationMonitor AutomationMonitor { get; set; }


    }

    /// <summary>App-owned scheduler. Metadata polling is a hint; adapters still copy, hash and validate stable inputs.</summary>
    sealed class TrackerAutomationMonitor : IDisposable
    {
        sealed class Schedule
        {
            internal string SettingsId, Signature, Handled;
            internal DateTime Changed, Next;
        }
        readonly AchievementTrackingStore store;
        readonly TrackerRefreshService refresh;
        readonly Func<DateTime> clock;
        readonly Func<TrackerSourceBinding, string> probe;
        readonly Dictionary<string, Schedule> schedules = new();
        readonly ConcurrentDictionary<string, byte> opened = new();
        readonly SemaphoreSlim tickGate = new(1, 1);
        readonly object sync = new();
        CancellationTokenSource lifetime = new(), scope = new();
        string steam = "", retro = "";
        DateTime cancelledUntil;
        bool disposed;
        internal event Action<TrackerAutomationNotice> Notice;
        internal TrackerAutomationMonitor(AchievementTrackingStore store, TrackerRefreshService refresh = null, Func<DateTime> clock = null, Func<TrackerSourceBinding, string> probe = null)
        {
            this.store = store; this.refresh = refresh ?? new TrackerRefreshService(store); this.clock = clock ?? (() => DateTime.UtcNow); this.probe = probe ?? this.refresh.ChangeSignature;
            store.AutomationChanged += Invalidate;
        }
        static string Key(string tracker, string account) => tracker + "\n" + account;
        void Invalidate() { lock (sync) { if (disposed) return; scope.Cancel(); scope.Dispose(); scope = new(); } }
        internal void SetAccounts(string steamAccount, string retroAccount)
        {
            steamAccount = AchievementTrackingCatalog.AccountKey("steam", steamAccount); retroAccount = AchievementTrackingCatalog.AccountKey("retroachievements", retroAccount);
            lock (sync) { if (steam == steamAccount && retro == retroAccount) return; steam = steamAccount; retro = retroAccount; Invalidate(); }
        }
        internal void Opened(string tracker, string account) { TrackerSourceLease.SignalOpen(store.DatabasePath,tracker,account); opened[Key(tracker, account)] = 0; }
        internal void Cancel() { lock (sync) { cancelledUntil = clock().AddSeconds(30); Invalidate(); } }
        internal async Task TickAsync(CancellationToken cancellation = default)
        {
            if (!await tickGate.WaitAsync(0, cancellation).ConfigureAwait(false)) return;
            try
            {
                string activeSteam, activeRetro; CancellationToken scopeToken;
                lock (sync) { if (disposed || clock() < cancelledUntil) return; activeSteam = steam; activeRetro = retro; scopeToken = scope.Token; }
                using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellation, lifetime.Token, scopeToken); var token = linked.Token;
                var definitions = store.Definitions().ToDictionary(d => d.Id);
                foreach (var binding in store.Sources())
                {
                    token.ThrowIfCancellationRequested();
                    if(binding.AdapterId is "death-stranding-dc.fabrication" or "pixelvault.json-file" && TrackerSourceLease.BackgroundOwned(store.DatabasePath))continue;
                    if (!definitions.TryGetValue(binding.TrackerId, out var definition) || string.IsNullOrWhiteSpace(binding.Account)
                        || binding.Account != (definition.Provider == "steam" ? activeSteam : activeRetro)) continue;
                    var (settings, state) = store.Automation(binding.TrackerId, binding.Account);
                    if (settings.Paused || settings.Mode == TrackerAutomationMode.Manual) continue;
                    var key = Key(binding.TrackerId, binding.Account); var now = clock();
                    if (!schedules.TryGetValue(key, out var schedule) || schedule.SettingsId != settings.Id)
                    { schedule = new Schedule { SettingsId = settings.Id, Next = state.Failures > 0 ? new DateTime(state.LastCheckUtc, DateTimeKind.Utc).Add(Backoff(state.Failures)) : settings.Mode == TrackerAutomationMode.Periodic && state.LastCheckUtc > 0 ? new DateTime(state.LastCheckUtc, DateTimeKind.Utc).AddMinutes(settings.IntervalMinutes) : DateTime.MinValue }; schedules[key] = schedule; }
                    if (now < schedule.Next) continue;
                    var openSignal=TrackerSourceLease.OpenSignal(store.DatabasePath,binding.TrackerId,binding.Account);
                    bool requested = opened.ContainsKey(key) || openSignal.Length>0 && state.HandledOpenSignal!=openSignal;
                    if (settings.Mode == TrackerAutomationMode.OnOpen && !requested && state.Failures == 0) continue;
                    TrackerRefreshPlan plan = null;
                    try
                    {
                        var signature = probe(binding); token.ThrowIfCancellationRequested();
                        if (schedule.Signature != signature) { schedule.Signature = signature; schedule.Changed = now; continue; }
                        if (now - schedule.Changed < TimeSpan.FromSeconds(3)) continue;
                        if (settings.Mode == TrackerAutomationMode.SaveChanges && (signature == schedule.Handled || signature == state.HandledSignature) && state.Failures == 0 && store.SourceStatus(binding.TrackerId,binding.Account)?.Outcome != TrackerRefreshOutcome.Undone) continue;
                        var prior = store.SourceStatus(binding.TrackerId, binding.Account);
                        plan = await refresh.PrepareAsync(binding.TrackerId, binding.Account, token, automatic: true).ConfigureAwait(false);
                        if (plan == null) continue;
                        token.ThrowIfCancellationRequested();
                        // Reconfiguration is also checked within the atomic apply transaction.
                        if (store.Automation(binding.TrackerId, binding.Account).Settings.Id != settings.Id) { refresh.Cancel(plan); continue; }
                        bool review = state.ReviewPending || prior?.Outcome == TrackerRefreshOutcome.Undone || TrackerRefreshPolicy.HasRollback(plan,binding) || prior?.SourceObservedUtc > plan.ObservedUtc || TrackerRefreshPolicy.HasManualConflict(plan,binding);
                        int changed = TrackerRefreshPolicy.ChangedCount(plan,binding);
                        state.LastCheckUtc = now.Ticks; state.Failures = 0;
                        state.ReviewPending = review;
                        state.Message = review ? "Automatic refresh needs review. Open Refresh to inspect manual conflicts, an older save or an undone batch."
                            : changed == 0 ? "Automatic refresh: no progress changes." : "Automatic refresh updated " + changed + " items. " + (binding.ReplaceManualProgress ? "Verified save values control checkmarks; notes stay saved." : "Manual progress and notes were preserved.");
                        if (!review && changed == 0) state.NoticeKey = null;
                        bool recorded;
                        if (review) recorded = store.RecordAutomatic(binding, settings.Id, state, plan, TrackerRefreshOutcome.ReviewRequired);
                        else if (changed == 0 || prior?.Fingerprint == plan.Fingerprint && !TrackerRefreshPolicy.ClearsManual(plan,binding))
                            recorded = store.RecordAutomatic(binding, settings.Id, state, plan, TrackerRefreshOutcome.Unchanged, acknowledge: true);
                        else { refresh.Apply(plan, false, Array.Empty<string>(), token, settings.Id); recorded = store.RecordAutomatic(binding, settings.Id, state, plan); }
                        if (recorded && (review || changed > 0)) Publish(binding, definition, settings, state, review ? "review:" + plan.Fingerprint : "changed:" + plan.Fingerprint, review);
                        state.HandledSignature = signature; state.HandledOpenSignal=openSignal; store.RecordAutomatic(binding, settings.Id, state);
                        schedule.Handled = signature; schedule.Next = settings.Mode == TrackerAutomationMode.Periodic ? now.AddMinutes(settings.IntervalMinutes) : DateTime.MinValue;
                        opened.TryRemove(key, out _);
                    }
                    catch (OperationCanceledException) { if (plan != null) refresh.Cancel(plan); throw; }
                    catch (Exception ex)
                    {
                        state.LastCheckUtc = now.Ticks; state.Failures = Math.Min(20, state.Failures + 1);
                        state.Message = "Automatic refresh failed: " + ex.Message;
                        schedule.Next = now.Add(Backoff(state.Failures));
                        if (store.RecordAutomatic(binding, settings.Id, state)) Publish(binding, definition, settings, state, "error:" + ex.GetType().Name + ":" + ex.Message, true);
                    }
                }
            }
            catch (OperationCanceledException) { }
            finally { tickGate.Release(); }
        }
        internal static TimeSpan Backoff(int failures) => TimeSpan.FromSeconds(Math.Min(1800, 30 * Math.Pow(2, Math.Clamp(failures - 1, 0, 10))));
        void Publish(TrackerSourceBinding binding, AchievementTrackerDefinition definition, TrackerAutomationSettings settings, TrackerAutomationState state, string key, bool review)
        {
            if (state.NoticeKey == key) return; state.NoticeKey = key;
            if (store.RecordAutomatic(binding, settings.Id, state)) Notice?.Invoke(new(binding.TrackerId, binding.Account, definition.Title, state.Message, review));
        }
        public void Dispose()
        {
            lock (sync) { if (disposed) return; disposed = true; store.AutomationChanged -= Invalidate; lifetime.Cancel(); scope.Cancel(); }
            // Do not wait for continuations on the UI thread; worker cancellation initiates owned-process termination immediately.
        }
    }
}
