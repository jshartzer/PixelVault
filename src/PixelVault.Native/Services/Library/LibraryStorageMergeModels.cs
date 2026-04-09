using System.Collections.Generic;

namespace PixelVaultNative
{
    /// <summary>
    /// One file move proposed when re-homing shared <see cref="GameIndexEditorRow.StorageGroupId"/> captures (PV-PLN-LIBST-001 Step 5).
    /// </summary>
    internal sealed class LibraryStorageMergeFileMovePreview
    {
        public string SourcePath { get; set; }
        public string TargetPath { get; set; }
        /// <summary>Destination path already exists and is not the same file (apply will allocate a unique name).</summary>
        public bool WouldRenameForConflict { get; set; }
    }

    /// <summary>One storage group that has work to consolidate disk folders.</summary>
    internal sealed class LibraryStorageMergeGroupPreview
    {
        public string StorageGroupId { get; set; }
        public string TargetDirectory { get; set; }
        public List<GameIndexEditorRow> MemberRows { get; set; } = new List<GameIndexEditorRow>();
        public List<LibraryStorageMergeFileMovePreview> FileMoves { get; set; } = new List<LibraryStorageMergeFileMovePreview>();
        /// <summary>Distinct directory paths that lose all library media files after <see cref="FileMoves"/> (best-effort preview).</summary>
        public List<string> DirectoriesThatMayBeRemovedIfEmpty { get; set; } = new List<string>();
    }

    /// <summary>Dry-run output for the merge / re-home workflow.</summary>
    internal sealed class LibraryStorageMergeDryRunResult
    {
        public List<LibraryStorageMergeGroupPreview> Groups { get; set; } = new List<LibraryStorageMergeGroupPreview>();
        public List<string> Warnings { get; set; } = new List<string>();
        public int TotalFileMoves;
        public int TotalConflictRenames;
    }
}
