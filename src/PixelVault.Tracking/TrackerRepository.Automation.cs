using Microsoft.Data.Sqlite;
using System.Text.Json;
namespace PixelVaultNative
{
    public partial class TrackerRepository
    {
        public event Action AutomationChanged;
        protected void NotifyAutomationChanged() => AutomationChanged?.Invoke();
        protected virtual IReadOnlySet<string> GameFileAdapterIds => new HashSet<string>(StringComparer.Ordinal) { "death-stranding-dc.fabrication" };
        protected static (TrackerAutomationSettings Settings, TrackerAutomationState State) ReadAutomation(SqliteConnection c, SqliteTransaction tx, string tracker, string account)
        {
            using var cmd = c.CreateCommand(); cmd.Transaction = tx;
            cmd.CommandText = "SELECT settings,state FROM tracker_automation WHERE tracker_id=$t AND account=$a";
            cmd.Parameters.AddWithValue("$t", tracker); cmd.Parameters.AddWithValue("$a", account); using var r = cmd.ExecuteReader();
            return r.Read() ? (FromJson<TrackerAutomationSettings>(r.GetString(0)), FromJson<TrackerAutomationState>(r.GetString(1))) : (new(), new());
        }
        protected static void RequireAutomation(SqliteConnection c, SqliteTransaction tx, string tracker, string account, string bindingId, string id)
        {
            var settings = ReadAutomation(c, tx, tracker, account).Settings;
            if (settings.Id != id || settings.BindingId != bindingId || settings.Paused || settings.Mode == TrackerAutomationMode.Manual)
                throw new OperationCanceledException("Automatic refresh settings changed. Saved progress was preserved.");
        }
        protected static void ClearAutomationReview(SqliteConnection c, SqliteTransaction tx, string tracker, string account)
        {
            var previous = ReadAutomation(c, tx, tracker, account);
            if (!previous.State.ReviewPending) return;
            previous.State.ReviewPending = false; previous.State.NoticeKey = null;
            previous.State.Message = "Manual refresh reviewed. Automatic refresh can resume.";
            using var cmd = c.CreateCommand(); cmd.Transaction = tx;
            cmd.CommandText = "UPDATE tracker_automation SET state=$s WHERE tracker_id=$t AND account=$a";
            cmd.Parameters.AddWithValue("$s", Json(previous.State)); cmd.Parameters.AddWithValue("$t", tracker); cmd.Parameters.AddWithValue("$a", account); cmd.ExecuteNonQuery();
        }
    }
}
