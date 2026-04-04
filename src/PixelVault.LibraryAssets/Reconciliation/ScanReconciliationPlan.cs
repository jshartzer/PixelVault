using PixelVault.LibraryAssets.Health;
using PixelVault.LibraryAssets.Scanning;

namespace PixelVault.LibraryAssets.Reconciliation;

/// <summary>What the host may apply after a scan, depending on root health.</summary>
public sealed class ScanReconciliationPlan
{
    /// <summary>Insert new canonical rows for <see cref="ScanDiffKind.Added"/>.</summary>
    public bool ApplyAdditions { get; init; }

    /// <summary>Update fingerprints/paths for <see cref="ScanDiffKind.Updated"/>.</summary>
    public bool ApplyUpdates { get; init; }

    /// <summary>Transition active assets to <see cref="Models.AssetLifecycle.Missing"/> for <see cref="ScanDiffKind.Missing"/>.</summary>
    public bool ApplySoftMissing { get; init; }

    /// <summary>Hard-delete or archive for <see cref="ScanDiffKind.ConfirmedDeleted"/> (never from scan alone).</summary>
    public bool ApplyConfirmedDeletes { get; init; }

    public bool AbortedDueToUnhealthyRoot { get; init; }

    public string? AbortSummary { get; init; }

    /// <summary>Log line when reconciliation is restricted; e.g. library offline/inaccessible.</summary>
    public string? DiagnosticLogLine { get; init; }

    public static ScanReconciliationPlan Create(LibraryRootHealthResult health, bool destructiveMissingAndDeletesRequested)
    {
        if (health.IsHealthy)
        {
            return new ScanReconciliationPlan
            {
                ApplyAdditions = true,
                ApplyUpdates = true,
                ApplySoftMissing = true,
                ApplyConfirmedDeletes = destructiveMissingAndDeletesRequested,
                DiagnosticLogLine = null
            };
        }

        var code = health.FailureCode ?? LibraryRootHealthFailureCode.Offline;
        var log = "Library offline/inaccessible (" + code + "). Applying scan additions/updates only; soft-missing and destructive reconciliation deferred.";
        return new ScanReconciliationPlan
        {
            ApplyAdditions = true,
            ApplyUpdates = true,
            ApplySoftMissing = false,
            ApplyConfirmedDeletes = false,
            AbortedDueToUnhealthyRoot = true,
            AbortSummary = health.Messages.Count > 0 ? health.Messages[0] : "Library root health check failed.",
            DiagnosticLogLine = log
        };
    }
}
