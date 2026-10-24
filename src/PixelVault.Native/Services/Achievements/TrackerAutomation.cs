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
        internal static string Signature(IEnumerable<FileInfo> candidates, int maximumBytes)
        {
            var files = candidates.ToList(); if (files.Count == 0) throw new InvalidDataException("No source files found.");
            var first = files[0]; long size = first.Length; var stamp = first.LastWriteTimeUtc;
            if (size > maximumBytes) throw new InvalidDataException("The source file exceeds the adapter's size limit.");
            string hash;
            using (var input = new FileStream(first.FullName, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                hash = Convert.ToHexString(SHA256.HashData(input)); first.Refresh();
                if (input.Length != size || first.Length != size || first.LastWriteTimeUtc != stamp) throw new IOException("The source is still being saved. Waiting before retrying.");
            }
            // Hash the newest candidate too: cloud replacement can retain size and modification time.
            var text = hash + "\n" + string.Join("\n", files.Select(f => f.FullName + "|" + f.Length + "|" + f.LastWriteTimeUtc.Ticks));
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
        }
    }
    enum TrackerAutomationMode { Manual, OnOpen, SaveChanges, Periodic }
    sealed class TrackerAutomationSettings
    {
        public string Id { get; set; }
        public string BindingId { get; set; }
        public TrackerAutomationMode Mode { get; set; }
        public int IntervalMinutes { get; set; } = 5;
        public bool Paused { get; set; }
    }
    sealed class TrackerAutomationState
    {
        public long LastCheckUtc { get; set; }
        public string Message { get; set; } = "Automatic refresh is off.";
        public string NoticeKey { get; set; }
        public int Failures { get; set; }
        public bool ReviewPending { get; set; }
    }
    sealed record TrackerAutomationNotice(string TrackerId, string Account, string Title, string Message, bool NeedsReview);

    sealed partial class AchievementTrackingStore
    {
        internal TrackerAutomationMonitor AutomationMonitor { get; set; }
        internal event Action AutomationChanged;

        internal IReadOnlyList<TrackerSourceBinding> Sources()
        {
            using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT binding FROM tracker_source";
            using var r = cmd.ExecuteReader(); var result = new List<TrackerSourceBinding>();
            while (r.Read()) result.Add(FromJson<TrackerSourceBinding>(r.GetString(0))); return result;
        }
        static (TrackerAutomationSettings Settings, TrackerAutomationState State) ReadAutomation(SqliteConnection c, SqliteTransaction tx, string tracker, string account)
        {
            using var cmd = c.CreateCommand(); cmd.Transaction = tx;
            cmd.CommandText = "SELECT settings,state FROM tracker_automation WHERE tracker_id=$t AND account=$a";
            cmd.Parameters.AddWithValue("$t", tracker); cmd.Parameters.AddWithValue("$a", account); using var r = cmd.ExecuteReader();
            return r.Read() ? (FromJson<TrackerAutomationSettings>(r.GetString(0)), FromJson<TrackerAutomationState>(r.GetString(1))) : (new(), new());
        }
        internal (TrackerAutomationSettings Settings, TrackerAutomationState State) Automation(string tracker, string account)
        {
            account = RequireAccount(Definition(tracker), account); using var c = Open();
            var result = ReadAutomation(c, null, tracker, account); var binding = ReadSource(c, null, tracker, account).Binding;
            return binding?.Id == result.Settings.BindingId ? result : (new(), new());
        }
        internal IReadOnlyList<string> AutomationHistory(string tracker, string account)
        {
            account = RequireAccount(Definition(tracker), account); using var c = Open(); using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT created_utc,message FROM tracker_automation_result WHERE tracker_id=$t AND account=$a ORDER BY id DESC LIMIT 10";
            cmd.Parameters.AddWithValue("$t", tracker); cmd.Parameters.AddWithValue("$a", account); using var r = cmd.ExecuteReader(); var rows = new List<string>();
            while (r.Read()) rows.Add(new DateTime(r.GetInt64(0), DateTimeKind.Utc).ToLocalTime().ToString("g") + " · " + r.GetString(1)); return rows;
        }
        internal void SaveAutomation(string tracker, string account, string bindingId, TrackerAutomationMode mode, int intervalMinutes, bool paused)
        {
            account = RequireAccount(Definition(tracker), account);
            if (!Enum.IsDefined(mode) || intervalMinutes < 1 || intervalMinutes > 1440) throw new InvalidDataException("Choose a refresh interval from 1 to 1440 minutes.");
            using var c = Open(); using var tx = c.BeginTransaction();
            if (ReadSource(c, tx, tracker, account).Binding?.Id != bindingId || bindingId == null) throw new InvalidOperationException("The source changed. Reopen Automatic refresh.");
            var previous = ReadAutomation(c, tx, tracker, account);
            var settings = new TrackerAutomationSettings { Id = Guid.NewGuid().ToString("N"), BindingId = bindingId, Mode = mode, IntervalMinutes = intervalMinutes, Paused = paused };
            var state = previous.Settings.BindingId == bindingId ? previous.State : new TrackerAutomationState();
            state.Message = mode == TrackerAutomationMode.Manual ? "Automatic refresh is off." : paused ? "Automatic refresh paused." : "Automatic refresh enabled. Waiting for a stable source.";
            using var cmd = c.CreateCommand(); cmd.Transaction = tx;
            cmd.CommandText = "INSERT INTO tracker_automation(tracker_id,account,settings,state) VALUES($t,$a,$s,$r) ON CONFLICT(tracker_id,account) DO UPDATE SET settings=$s,state=$r";
            cmd.Parameters.AddWithValue("$t", tracker); cmd.Parameters.AddWithValue("$a", account); cmd.Parameters.AddWithValue("$s", Json(settings)); cmd.Parameters.AddWithValue("$r", Json(state)); cmd.ExecuteNonQuery(); tx.Commit();
            AutomationChanged?.Invoke();
        }
        static void RequireAutomation(SqliteConnection c, SqliteTransaction tx, string tracker, string account, string bindingId, string id)
        {
            var settings = ReadAutomation(c, tx, tracker, account).Settings;
            if (settings.Id != id || settings.BindingId != bindingId || settings.Paused || settings.Mode == TrackerAutomationMode.Manual)
                throw new OperationCanceledException("Automatic refresh settings changed. Saved progress was preserved.");
        }
        static void ClearAutomationReview(SqliteConnection c, SqliteTransaction tx, string tracker, string account)
        {
            var previous = ReadAutomation(c, tx, tracker, account);
            if (!previous.State.ReviewPending) return;
            previous.State.ReviewPending = false; previous.State.NoticeKey = null;
            previous.State.Message = "Manual refresh reviewed. Automatic refresh can resume.";
            using var cmd = c.CreateCommand(); cmd.Transaction = tx;
            cmd.CommandText = "UPDATE tracker_automation SET state=$s WHERE tracker_id=$t AND account=$a";
            cmd.Parameters.AddWithValue("$s", Json(previous.State)); cmd.Parameters.AddWithValue("$t", tracker); cmd.Parameters.AddWithValue("$a", account); cmd.ExecuteNonQuery();
        }
        internal bool RecordAutomatic(TrackerSourceBinding binding, string settingsId, TrackerAutomationState state, TrackerRefreshPlan plan = null,
            TrackerRefreshOutcome? outcome = null, bool acknowledge = false)
        {
            using var c = Open(); using var tx = c.BeginTransaction();
            var source = ReadSource(c, tx, binding.TrackerId, binding.Account);
            if (source.Binding?.Id != binding.Id) return false;
            var previous = ReadAutomation(c, tx, binding.TrackerId, binding.Account); var settings = previous.Settings;
            if (settings.Id != settingsId) return false;
            if (plan != null && source.Status?.AttemptId != plan.AttemptId) return false;
            if (outcome.HasValue)
            {
                source.Status.Outcome = outcome.Value; source.Status.Message = state.Message;
                if (acknowledge) { source.Status.LastSuccessUtc = state.LastCheckUtc; source.Status.Fingerprint = plan.Fingerprint; source.Status.SourceObservedUtc = plan.ObservedUtc; }
                WriteStatus(c, tx, binding, source.Status);
            }
            using var cmd = c.CreateCommand(); cmd.Transaction = tx;
            cmd.CommandText = "UPDATE tracker_automation SET state=$s WHERE tracker_id=$t AND account=$a";
            cmd.Parameters.AddWithValue("$s", Json(state)); cmd.Parameters.AddWithValue("$t", binding.TrackerId); cmd.Parameters.AddWithValue("$a", binding.Account); cmd.ExecuteNonQuery();
            if (state.LastCheckUtc > 0 && (previous.State.LastCheckUtc != state.LastCheckUtc || previous.State.Message != state.Message))
            {
                using var history = c.CreateCommand(); history.Transaction = tx;
                history.CommandText = "INSERT INTO tracker_automation_result(tracker_id,account,created_utc,message) VALUES($t,$a,$u,$m); DELETE FROM tracker_automation_result WHERE tracker_id=$t AND account=$a AND id NOT IN (SELECT id FROM tracker_automation_result WHERE tracker_id=$t AND account=$a ORDER BY id DESC LIMIT 100)";
                history.Parameters.AddWithValue("$t", binding.TrackerId); history.Parameters.AddWithValue("$a", binding.Account); history.Parameters.AddWithValue("$u", state.LastCheckUtc); history.Parameters.AddWithValue("$m", state.Message); history.ExecuteNonQuery();
            }
            tx.Commit(); return true;
        }
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
        internal void Opened(string tracker, string account) => opened[Key(tracker, account)] = 0;
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
                    if (!definitions.TryGetValue(binding.TrackerId, out var definition) || string.IsNullOrWhiteSpace(binding.Account)
                        || binding.Account != (definition.Provider == "steam" ? activeSteam : activeRetro)) continue;
                    var (settings, state) = store.Automation(binding.TrackerId, binding.Account);
                    if (settings.Paused || settings.Mode == TrackerAutomationMode.Manual) continue;
                    var key = Key(binding.TrackerId, binding.Account); var now = clock();
                    if (!schedules.TryGetValue(key, out var schedule) || schedule.SettingsId != settings.Id)
                    { schedule = new Schedule { SettingsId = settings.Id, Next = state.Failures > 0 ? new DateTime(state.LastCheckUtc, DateTimeKind.Utc).Add(Backoff(state.Failures)) : DateTime.MinValue }; schedules[key] = schedule; }
                    if (now < schedule.Next) continue;
                    bool requested = opened.ContainsKey(key);
                    if (settings.Mode == TrackerAutomationMode.OnOpen && !requested && state.Failures == 0) continue;
                    TrackerRefreshPlan plan = null;
                    try
                    {
                        var signature = probe(binding); token.ThrowIfCancellationRequested();
                        if (schedule.Signature != signature) { schedule.Signature = signature; schedule.Changed = now; continue; }
                        if (now - schedule.Changed < TimeSpan.FromSeconds(3)) continue;
                        if (settings.Mode == TrackerAutomationMode.SaveChanges && signature == schedule.Handled && state.Failures == 0) continue;
                        var prior = store.SourceStatus(binding.TrackerId, binding.Account);
                        plan = await refresh.PrepareAsync(binding.TrackerId, binding.Account, token, automatic: true).ConfigureAwait(false);
                        if (plan == null) continue;
                        token.ThrowIfCancellationRequested();
                        // Reconfiguration is also checked within the atomic apply transaction.
                        if (store.Automation(binding.TrackerId, binding.Account).Settings.Id != settings.Id) { refresh.Cancel(plan); continue; }
                        bool review = state.ReviewPending || prior?.Outcome == TrackerRefreshOutcome.Undone || plan.HasRollback || prior?.SourceObservedUtc > plan.ObservedUtc || plan.Rows.Any(r => r.Conflict);
                        int changed = plan.Rows.Count(r => r.Changed);
                        state.LastCheckUtc = now.Ticks; state.Failures = 0;
                        state.ReviewPending = review;
                        state.Message = review ? "Automatic refresh needs review. Open Refresh to inspect manual conflicts, an older save or an undone batch."
                            : changed == 0 ? "Automatic refresh: no progress changes." : "Automatic refresh updated " + changed + " items. Manual progress and notes were preserved.";
                        if (!review && changed == 0) state.NoticeKey = null;
                        bool recorded;
                        if (review) recorded = store.RecordAutomatic(binding, settings.Id, state, plan, TrackerRefreshOutcome.ReviewRequired);
                        else if (prior?.Fingerprint == plan.Fingerprint || changed == 0)
                            recorded = store.RecordAutomatic(binding, settings.Id, state, plan, TrackerRefreshOutcome.Unchanged, acknowledge: true);
                        else { refresh.Apply(plan, false, Array.Empty<string>(), token, settings.Id); recorded = store.RecordAutomatic(binding, settings.Id, state, plan); }
                        if (recorded && (review || changed > 0)) Publish(binding, definition, settings, state, review ? "review:" + plan.Fingerprint : "changed:" + plan.Fingerprint, review);
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
