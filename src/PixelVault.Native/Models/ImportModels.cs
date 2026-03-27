using System;
using System.Collections.Generic;

namespace PixelVaultNative
{
    sealed class ReviewItem
    {
        public string FilePath;
        public string FileName;
        public string PlatformLabel;
        public string[] PlatformTags;
        public DateTime CaptureTime;
        public bool PreserveFileTimes;
        public string Comment;
        public bool AddPhotographyTag;
        public bool TagSteam;
        public bool TagPs5;
        public bool TagXbox;
        public bool DeleteBeforeProcessing;
    }

    sealed class ManualMetadataItem
    {
        public string GameId;
        public string FilePath;
        public string FileName;
        public string OriginalFileName;
        public DateTime CaptureTime;
        public bool UseCustomCaptureTime;
        public string GameName;
        public string Comment;
        public string TagText;
        public bool AddPhotographyTag;
        public bool ForceTagMetadataWrite;
        public bool TagSteam;
        public bool TagPc;
        public bool TagPs5;
        public bool TagXbox;
        public bool TagOther;
        public string CustomPlatformTag;
        public string OriginalGameId;
        public DateTime OriginalCaptureTime;
        public bool OriginalUseCustomCaptureTime;
        public string OriginalGameName;
        public string OriginalComment;
        public string OriginalTagText;
        public bool OriginalAddPhotographyTag;
        public bool OriginalTagSteam;
        public bool OriginalTagPc;
        public bool OriginalTagPs5;
        public bool OriginalTagXbox;
        public bool OriginalTagOther;
        public string OriginalCustomPlatformTag;
    }

    sealed class UndoImportEntry
    {
        public string SourceDirectory;
        public string ImportedFileName;
        public string CurrentPath;
    }

    sealed class RenameStepResult
    {
        public int Renamed;
        public int Skipped;
    }

    sealed class DeleteStepResult
    {
        public int Deleted;
        public int Skipped;
    }

    sealed class MetadataStepResult
    {
        public int Updated;
        public int Skipped;
    }

    sealed class MoveStepResult
    {
        public int Moved;
        public int Skipped;
        public int RenamedOnConflict;
        public List<UndoImportEntry> Entries = new List<UndoImportEntry>();
    }

    sealed class SortStepResult
    {
        public int Sorted;
        public int FoldersCreated;
        public int RenamedOnConflict;
    }

    sealed class SourceInventory
    {
        public List<string> TopLevelMediaFiles = new List<string>();
        public List<string> RenameScopeFiles = new List<string>();
    }

    sealed class IntakePreviewSummary
    {
        public List<string> SourceRoots = new List<string>();
        public int RenameScopeCount;
        public int RenameCandidateCount;
        public int TopLevelMediaCount;
        public int MetadataCandidateCount;
        public int MoveCandidateCount;
        public int ManualItemCount;
        public int ConflictCount;
        public List<ReviewItem> ReviewItems = new List<ReviewItem>();
        public List<ManualMetadataItem> ManualItems = new List<ManualMetadataItem>();
    }

    sealed class ExifWriteRequest
    {
        public string FilePath;
        public string FileName;
        public string[] Arguments;
        public bool RestoreFileTimes;
        public DateTime OriginalCreateTime;
        public DateTime OriginalWriteTime;
        public string SuccessDetail;
    }

    sealed class EmbeddedMetadataSnapshot
    {
        public string[] Tags = new string[0];
        public string Comment = string.Empty;
        public DateTime? CaptureTime;
    }
}
