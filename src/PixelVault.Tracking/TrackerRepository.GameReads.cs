using Microsoft.Data.Sqlite;

namespace PixelVaultNative;

public sealed record TrackerGameRead(AchievementTrackerDefinition Definition, IReadOnlyList<AchievementTrackerState> States);

public sealed record TrackerDashboardRead(AchievementTrackerDefinition Definition,
    IReadOnlyList<AchievementTrackerState> States, TrackerSourceBinding Source,
    TrackerAutomationSettings Automation, TrackerAutomationState AutomationState);

public partial class TrackerRepository
{
    /// <summary>Fresh, consistent dashboard data: resolve the catalogue once, then read each selected
    /// tracker in the same transaction. No process cache can hide another writer's changes.</summary>
    public IReadOnlyList<TrackerDashboardRead> ReadDashboard(IReadOnlyDictionary<string, string> accounts,
        string provider = null, string game = null)
    {
        using var connection = Open();
        using var transaction = connection.BeginTransaction(deferred: true);
        var result = new List<TrackerDashboardRead>();
        foreach (var definition in ReadDefinitions(connection, transaction))
        {
            if (provider != null && definition.Provider != provider || game != null && definition.GameId != game) continue;
            var account = AchievementTrackingCatalog.AccountKey(definition.Provider,
                accounts.TryGetValue(definition.Provider, out var value) ? value : string.Empty);
            var states = ReadStates(connection, transaction, definition, account);
            var source = ReadSource(connection, transaction, definition.Id, account).Binding;
            var automation = string.IsNullOrEmpty(account)
                ? (Settings: new TrackerAutomationSettings(), State: new TrackerAutomationState())
                : ReadAutomation(connection, transaction, definition.Id, account);
            if (source?.Id != automation.Settings.BindingId)
                automation = (new TrackerAutomationSettings(), new TrackerAutomationState());
            result.Add(new(definition, states, source, automation.Settings, automation.State));
        }
        transaction.Commit();
        return result;
    }

    public IReadOnlyList<TrackerGameRead> ReadForAccounts(IReadOnlyDictionary<string,string> accounts)
    {
        using var c=Open();using var tx=c.BeginTransaction(deferred:true);
        var rows=ReadDefinitions(c,tx).Where(d=>accounts.ContainsKey(d.Provider))
            .Select(d=>new TrackerGameRead(d,ReadStates(c,tx,d,AchievementTrackingCatalog.AccountKey(d.Provider,accounts[d.Provider])))).ToList();
        tx.Commit();return rows;
    }
    /// <summary>One read-only snapshot of requested same-game trackers. No schema work, observations or history writes.</summary>
    public IReadOnlyList<TrackerGameRead> ReadGameTrackers(string provider, string game, string account, IReadOnlyCollection<string> trackerIds)
    {
        var key = AchievementTrackingCatalog.AccountKey(provider, account);
        if (key.Length == 0 || trackerIds.Count == 0) return Array.Empty<TrackerGameRead>();
        var requested = trackerIds.ToHashSet(StringComparer.Ordinal);
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        { DataSource = path, Mode = SqliteOpenMode.ReadOnly, Pooling = false, DefaultTimeout = 5 }.ToString());
        connection.Open();
        using var transaction = connection.BeginTransaction(deferred: true);
        var result = ReadDefinitions(connection, transaction)
            .Where(d => d.Provider == provider && d.GameId == game && requested.Contains(d.Id))
            .Select(d => new TrackerGameRead(d, ReadStates(connection, transaction, d, key))).ToList();
        transaction.Commit();
        return result;
    }
}
