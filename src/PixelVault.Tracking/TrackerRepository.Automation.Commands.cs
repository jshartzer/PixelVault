using Microsoft.Data.Sqlite;
namespace PixelVaultNative
{
    public partial class TrackerRepository
    {
        public IReadOnlyList<TrackerSourceBinding> Sources()
        {
            using var c = Open(); using var cmd = c.CreateCommand(); cmd.CommandText = "SELECT binding FROM tracker_source";
            using var r = cmd.ExecuteReader(); var result = new List<TrackerSourceBinding>();
            while (r.Read()) result.Add(FromJson<TrackerSourceBinding>(r.GetString(0))); return result;
        }
        public (TrackerAutomationSettings Settings, TrackerAutomationState State) Automation(string tracker, string account)
        {
            account = RequireAccount(Definition(tracker), account); using var c = Open();
            var result = ReadAutomation(c, null, tracker, account); var binding = ReadSource(c, null, tracker, account).Binding;
            return binding?.Id == result.Settings.BindingId ? result : (new(), new());
        }
        public IReadOnlyList<string> AutomationHistory(string tracker, string account)
        {
            account = RequireAccount(Definition(tracker), account); using var c = Open(); using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT created_utc,message FROM tracker_automation_result WHERE tracker_id=$t AND account=$a ORDER BY id DESC LIMIT 10";
            cmd.Parameters.AddWithValue("$t", tracker); cmd.Parameters.AddWithValue("$a", account); using var r = cmd.ExecuteReader(); var rows = new List<string>();
            while (r.Read()) rows.Add(new DateTime(r.GetInt64(0), DateTimeKind.Utc).ToLocalTime().ToString("g") + " · " + r.GetString(1)); return rows;
        }
        public void SaveAutomation(string tracker, string account, string bindingId, TrackerAutomationMode mode, int intervalMinutes, bool paused)
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
            NotifyAutomationChanged();
        }
        public bool RecordAutomatic(TrackerSourceBinding binding, string settingsId, TrackerAutomationState state, TrackerRefreshPlan plan = null,
            TrackerRefreshOutcome? outcome = null, bool acknowledge = false)
        {
            using var c = Open(); using var tx = c.BeginTransaction();
            var source = ReadSource(c, tx, binding.TrackerId, binding.Account);
            if (source.Binding?.Id != binding.Id) return false;
            var previous = ReadAutomation(c, tx, binding.TrackerId, binding.Account); var settings = previous.Settings;
            if (settings.Id != settingsId) return false;
            if (plan != null && source.Status?.AttemptId != plan.AttemptId) return false;
            if (acknowledge && plan != null && plan.Baseline != Revision(c, tx, binding.TrackerId, binding.Account)) return false;
            if (outcome.HasValue)
            {
                source.Status.Outcome = outcome.Value; source.Status.Message = state.Message;
                if (acknowledge) { source.Status.LastSuccessUtc = state.LastCheckUtc; source.Status.Fingerprint = plan.Fingerprint; source.Status.SourceObservedUtc = plan.ObservedUtc; RecordCentury(c, tx, plan, source.Binding); }
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
}
