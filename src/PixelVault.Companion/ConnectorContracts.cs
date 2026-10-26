#nullable enable annotations
using PixelVaultNative;

namespace PixelVault.Companion;

public sealed record ConnectorSource(TrackerSourceBinding Binding,AchievementTrackerDefinition Definition,string Revision);
public sealed record ConnectorPairing(string Url,string WorkspaceId,string Token,IReadOnlyList<ConnectorSource> Sources,
    Dictionary<string,string>? Accounts=null,string? DesktopDataRoot=null,Dictionary<string,string>? AchievementAccounts=null);
public sealed record ConnectorTask(string TrackerId,string BindingId,string Revision);
public sealed record ConnectorWork(string Id,string Lease,IReadOnlyList<ConnectorTask> Tasks);
public sealed record ConnectorObservation(string Lease,string Fingerprint,string? SnapshotJson,string? Error);
public sealed record RequestGameRefresh(string GroupId,string RequestId);
public sealed record ReviewGameRefresh(string Version,bool AllowOlder);
public sealed record RefreshChange(string Name,int? Previous,int? Incoming,int? Manual,bool Older);
public sealed record RefreshTaskView(string Title,string Status,string Message,int Changed,int Conflicts,int Older,IReadOnlyList<RefreshChange> Changes);
public sealed record GameRefreshView(string Id,string Status,string Message,bool CanReview,bool HasOlder,string Version,IReadOnlyList<RefreshTaskView> Tasks);
public sealed record ConnectorSyncSettings(bool AutomaticEnabled,bool LibraryEnabled,string WorkspaceId);
public sealed record AutomaticSaveChange(string Signature,IReadOnlyList<ConnectorTask> Tasks);
public sealed record AutomaticReceipt(string Id,string Status,string Signature);
public sealed record SetAutomaticUpdates(bool Enabled);
public sealed record ConnectorHealth(string? SourceError,string? LibraryError,string? PublicationError=null,string? AchievementError=null);
public sealed record PublishedTracker(AchievementTrackerDefinition Definition,TrackerSourceBinding? Binding=null);
public sealed record TrackerPublication(string WorkspaceId,IReadOnlyList<PublishedTracker> Trackers);
public sealed record TrackerPublicationResult(int Added,IReadOnlyList<ConnectorSource> Sources);
public sealed record TrackerTitle(string Id,string Provider,string GameId,string Title);
public sealed record TrackerTitles(string WorkspaceId,IReadOnlyList<TrackerTitle> Titles);
public sealed record TrackerTitleResult(int Updated,int Conflicts);

// A display-name edit never grants access to a different save or a different catalogue.
public static class TrackerDefinitionComparison
{
    public static bool SameStructure(AchievementTrackerDefinition a,AchievementTrackerDefinition b)
    {
        var left=System.Text.Json.JsonSerializer.SerializeToNode(a,AchievementTrackingCatalog.Json)!;
        var right=System.Text.Json.JsonSerializer.SerializeToNode(b,AchievementTrackingCatalog.Json)!;
        left["Title"]="";right["Title"]="";
        return System.Text.Json.Nodes.JsonNode.DeepEquals(left,right);
    }
}
