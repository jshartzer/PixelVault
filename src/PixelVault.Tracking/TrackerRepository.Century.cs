using Microsoft.Data.Sqlite;
using PixelVault.Tracking;
using System.Text.RegularExpressions;

namespace PixelVaultNative;

public sealed record CenturyProgress(int Current, int Target, long ObservedUtc, long VerifiedUtc, long UnlockUtc,
    string TrackerId, string BindingId, string Fingerprint, string Source, string Evidence, int Unknown = 0)
{
    public bool Unlocked => UnlockUtc > 0;
    public int EarnedPoints => Unlocked ? CenturyAchievementCatalog.PointsPerAchievement : 0;
    public string Status => Unlocked ? "Earned" : $"{Current} / {Target}";
}

public partial class TrackerRepository
{
    public CenturyProgress Century(string provider, string game, string id, string account)
    {
        if (CenturyAchievementCatalog.Find(provider, game, id) == null || string.IsNullOrWhiteSpace(account)) return null;
        using var c = Open();
        return ReadCentury(c, null, provider, game, id, AchievementTrackingCatalog.AccountKey(provider, account));
    }

    static CenturyProgress ReadCentury(SqliteConnection c, SqliteTransaction tx, string provider, string game, string id, string account)
    {
        using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "SELECT value FROM century_progress WHERE provider=$p AND game=$g AND id=$id AND account=$a";
        cmd.Parameters.AddWithValue("$p", provider); cmd.Parameters.AddWithValue("$g", game);
        cmd.Parameters.AddWithValue("$id", id); cmd.Parameters.AddWithValue("$a", account);
        return cmd.ExecuteScalar() is string json ? FromJson<CenturyProgress>(json) : null;
    }

    // Called only inside a successfully applied/acknowledged, current bound-source transaction.
    // Counts the incoming save observation, never effective or manually edited checklist state.
    protected void RecordCentury(SqliteConnection c, SqliteTransaction tx, TrackerRefreshPlan plan, TrackerSourceBinding binding)
    {
        if (binding == null || binding.Id != plan.BindingId || binding.TrackerId != plan.TrackerId || binding.Account != plan.Account
            || binding.AdapterId != "death-stranding-dc.fabrication" || binding.AdapterVersion != "1"
            || plan.AdapterId != binding.AdapterId || plan.AdapterVersion != binding.AdapterVersion
            || plan.Fingerprint?.Length != 64 || !plan.Fingerprint.All(Uri.IsHexDigit)) return;
        var snapshot = FromJson<AchievementTrackerSnapshot>(plan.Json);
        var definition = ReadDefinitions(c, tx).Single(d => d.Id == plan.TrackerId);
        if (definition.Provider != "steam" || definition.GameId != "1850570" || definition.Milestones?.Count != 0) return;
        var active = definition.Items.Where(i => !i.Archived).ToArray();
        var values = snapshot.Items.ToDictionary(i => i.ItemId, StringComparer.Ordinal);
        string id;
        if (definition.Id == CenturyMailCatalogue.TrackerId)
        {
            if (definition.Items.Count != CenturyMailCatalogue.Total || definition.CategoryTargets?.Count != 0
                || active.Length != CenturyMailCatalogue.Total || active.Any(i => i.Target != 1)
                || !CenturyMailCatalogue.ItemIds.SetEquals(active.Select(i => i.Id))
                || active.Any(i => !values.TryGetValue(i.Id, out var value)
                    || (value.Current is not (0 or 1) && !(i.Id == CenturyMailCatalogue.UnmappedId && value.Current == null)))) return;
            id = CenturyAchievementCatalog.DeathStrandingMailsId;
        }
        else if (definition.Id == CenturyInterviewCatalogue.TrackerId)
        {
            if (!CenturyInterviewCatalogue.Matches(definition)
                || active.Any(i => !values.TryGetValue(i.Id, out var value) || value.Current is not (0 or 1))) return;
            id = CenturyAchievementCatalog.DeathStrandingInterviewsId;
        }
        else if (definition.Id == CenturyCargoGoal.TrackerId)
        {
            if (!CenturyCargoGoal.Matches(definition) || !values.TryGetValue(CenturyCargoGoal.ItemId, out var value)
                || value.Current is not int kilograms || kilograms < 0 || kilograms > CenturyCargoGoal.MaximumKilograms) return;
            id = CenturyAchievementCatalog.DeathStrandingCargoId;
        }
        else if (definition.Id == CenturyLegendsCatalogue.TrackerId)
        {
            if (!CenturyLegendsCatalogue.Matches(definition)
                || active.Any(i => !values.TryGetValue(i.Id, out var value) || value.Current is not (0 or 1))) return;
            id = CenturyAchievementCatalog.DeathStrandingLegendsId;
        }
        else
        {
            var expected = Enumerable.Range(1, 18).Select(i => "UC Route 23-" + i)
                .Concat(Enumerable.Range(1, 33).Select(i => "UC Route 41-" + i)).ToHashSet(StringComparer.Ordinal);
            var routes = active.Select(i => Regex.Match(i.Name ?? "", @"^(UC Route (?:23|41)-[1-9][0-9]*)(?:\s*\([^\r\n]*\))?\s*$").Groups[1].Value).ToArray();
            if (active.Length != 51 || active.Any(i => i.Target != 1) || !expected.SetEquals(routes) || routes.Distinct().Count() != 51) return;
            if (active.Any(i => !values.TryGetValue(i.Id, out var value) || value.Current is not (0 or 1))) return;
            id = CenturyAchievementCatalog.DeathStrandingRoadsId;
        }
        var verified = TrackerTotals.FromVerifiedSnapshot(definition, snapshot);
        int current = verified.Completed, target = verified.Total, unknown = verified.Unknown;
        if (target <= 0) return;
        var prior = ReadCentury(c, tx, definition.Provider, definition.GameId, id, plan.Account);
        // Old saves and changed content with the same observation date cannot rewrite verified progress.
        if (prior != null && (prior.ObservedUtc > snapshot.ObservedUtcTicks
            || prior.ObservedUtc == snapshot.ObservedUtcTicks && prior.Fingerprint != plan.Fingerprint)) return;
        if (prior?.Fingerprint == plan.Fingerprint && prior.Current == current && prior.Target == target && prior.Unknown == unknown) return;
        var now = DateTime.UtcNow.Ticks;
        var progress = new CenturyProgress(current, target, snapshot.ObservedUtcTicks, now,
            prior?.UnlockUtc > 0 ? prior.UnlockUtc : unknown == 0 && current == target ? now : 0,
            plan.TrackerId, binding.Id, plan.Fingerprint, snapshot.Source, snapshot.Evidence, unknown);
        using var cmd = c.CreateCommand(); cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO century_progress(provider,game,id,account,value) VALUES($p,$g,$id,$a,$v) ON CONFLICT(provider,game,id,account) DO UPDATE SET value=$v";
        cmd.Parameters.AddWithValue("$p", definition.Provider); cmd.Parameters.AddWithValue("$g", definition.GameId);
        cmd.Parameters.AddWithValue("$id", id); cmd.Parameters.AddWithValue("$a", plan.Account); cmd.Parameters.AddWithValue("$v", Json(progress));
        cmd.ExecuteNonQuery();
    }
}
