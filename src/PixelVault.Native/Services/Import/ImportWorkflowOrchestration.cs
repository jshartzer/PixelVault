#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;

namespace PixelVaultNative
{
    /// <summary>
    /// Shared import / progress orchestration helpers (PV-PLN-UI-001 Step 7) — no WPF, usable from services and hosts.
    /// </summary>
    internal static class ImportWorkflowOrchestration
    {
        public static int GetMetadataWorkerCount(int workItems)
        {
            if (workItems <= 1) return 1;
            return Math.Max(1, Math.Min(Math.Min(Environment.ProcessorCount, 4), workItems));
        }

        public static void ThrowIfCancellationRequested(CancellationToken cancellationToken, string? operationLabel)
        {
            if (cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException((operationLabel ?? "Workflow") + " cancelled.", cancellationToken);
        }

        public static ImportWorkflowStandardWorkTotals ComputeStandardImportWorkTotals(
            SourceInventory? renameInventory,
            IReadOnlyList<ReviewItem>? reviewItems,
            SourceInventory? inventory,
            HashSet<string>? manualPaths) =>
            new ImportWorkflowStandardWorkTotals(renameInventory, reviewItems, inventory, manualPaths);

        public static ImportWorkflowUnifiedProgressPlan ComputeUnifiedImportProgressPlan(IReadOnlyList<ManualMetadataItem>? batch) =>
            new ImportWorkflowUnifiedProgressPlan(batch);

        public static ImportManualIntakeProgressPlan ComputeManualIntakeProgressPlan(IReadOnlyList<ManualMetadataItem>? manualItems) =>
            new ImportManualIntakeProgressPlan(manualItems);
    }

    /// <summary>Progress counts and offsets for the standard import (review + Steam rename scope) workflow.</summary>
    internal readonly struct ImportWorkflowStandardWorkTotals
    {
        public ImportWorkflowStandardWorkTotals(
            SourceInventory? renameInventory,
            IReadOnlyList<ReviewItem>? reviewItems,
            SourceInventory? inventory,
            HashSet<string>? manualPaths)
        {
            RenameTotal = renameInventory?.RenameScopeFiles?.Count ?? 0;
            DeleteTotal = reviewItems == null ? 0 : reviewItems.Count(item => item != null && item.DeleteBeforeProcessing);
            MetadataTotal = reviewItems?.Count ?? 0;
            if (inventory?.TopLevelMediaFiles == null)
                MoveTotal = 0;
            else
                MoveTotal = inventory.TopLevelMediaFiles.Count(file =>
                    !string.IsNullOrWhiteSpace(file) && File.Exists(file) && (manualPaths == null || !manualPaths.Contains(file)));
            TotalWork = RenameTotal + DeleteTotal + MetadataTotal + MoveTotal + 1;
        }

        public int RenameTotal { get; }
        public int DeleteTotal { get; }
        public int MetadataTotal { get; }
        public int MoveTotal { get; }
        public int TotalWork { get; }
        public int RenameOffset => 0;
        public int DeleteOffset => RenameOffset + RenameTotal;
        public int MetadataOffset => DeleteOffset + DeleteTotal;
        public int MoveOffset => MetadataOffset + MetadataTotal;
        public int SortOffset => MoveOffset + MoveTotal;
    }

    /// <summary>Progress counts and offsets for import-and-comment / unified manual batch workflow.</summary>
    internal readonly struct ImportWorkflowUnifiedProgressPlan
    {
        public ImportWorkflowUnifiedProgressPlan(IReadOnlyList<ManualMetadataItem>? batch)
        {
            var n = batch?.Count ?? 0;
            SteamRenameTotal = n;
            ManualRenameTotal = n;
            DeleteTotal = batch == null ? 0 : batch.Count(item => item != null && item.DeleteBeforeProcessing);
            MetadataTotal = n;
            MoveTotal = batch == null ? 0 : batch.Count(item =>
                item != null && !string.IsNullOrWhiteSpace(item.FilePath) && File.Exists(item.FilePath));
            TotalWork = SteamRenameTotal + ManualRenameTotal + DeleteTotal + MetadataTotal + MoveTotal + 1;
        }

        public int SteamRenameTotal { get; }
        public int ManualRenameTotal { get; }
        public int DeleteTotal { get; }
        public int MetadataTotal { get; }
        public int MoveTotal { get; }
        public int TotalWork { get; }
        public int SteamOff => 0;
        public int ManualRenameOff => SteamOff + SteamRenameTotal;
        public int DeleteOff => ManualRenameOff + ManualRenameTotal;
        public int MetadataOff => DeleteOff + DeleteTotal;
        public int MoveOff => MetadataOff + MetadataTotal;
        public int SortOff => MoveOff + MoveTotal;
    }

    /// <summary>Progress counts and offsets for manual intake-only workflow.</summary>
    internal readonly struct ImportManualIntakeProgressPlan
    {
        public ImportManualIntakeProgressPlan(IReadOnlyList<ManualMetadataItem>? manualItems)
        {
            var n = manualItems?.Count ?? 0;
            RenameTotal = n;
            MetadataTotal = n;
            MoveTotal = manualItems == null ? 0 : manualItems.Count(item =>
                item != null && !string.IsNullOrWhiteSpace(item.FilePath) && File.Exists(item.FilePath));
            TotalWork = RenameTotal + MetadataTotal + MoveTotal + 1;
        }

        public int RenameTotal { get; }
        public int MetadataTotal { get; }
        public int MoveTotal { get; }
        public int TotalWork { get; }
        public int RenameOffset => 0;
        public int MetadataOffset => RenameOffset + RenameTotal;
        public int MoveOffset => MetadataOffset + MetadataTotal;
        public int SortOffset => MoveOffset + MoveTotal;
    }
}
