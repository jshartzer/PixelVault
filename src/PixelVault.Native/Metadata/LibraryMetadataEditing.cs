using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        List<ManualMetadataItem> BuildLibraryMetadataItems(LibraryFolderInfo folder)
        {
            var items = new List<ManualMetadataItem>();
            if (folder == null || string.IsNullOrWhiteSpace(folder.FolderPath) || !Directory.Exists(folder.FolderPath)) return items;
            foreach (var file in GetFilesForLibraryFolderEntry(folder, false).OrderByDescending(GetLibraryDate).ThenBy(Path.GetFileName))
            {
                var fileName = Path.GetFileName(file);
                var indexEntry = TryGetLibraryMetadataIndexEntry(libraryRoot, file, null);
                var tags = GetEmbeddedKeywordTags(file);
                var consoleTags = GetConsolePlatformTagsForFile(file);
                var customPlatform = tags.FirstOrDefault(tag => tag.StartsWith(CustomPlatformPrefix, StringComparison.OrdinalIgnoreCase));
                var customPlatformName = string.IsNullOrWhiteSpace(customPlatform) ? string.Empty : CleanTag(customPlatform.Substring(CustomPlatformPrefix.Length));
                var normalizedCustomPlatform = NormalizeConsoleLabel(customPlatformName);
                var useCustomPlatform = !string.IsNullOrWhiteSpace(customPlatformName)
                    && !string.Equals(normalizedCustomPlatform, "Steam", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(normalizedCustomPlatform, "PC", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(normalizedCustomPlatform, "PS5", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(normalizedCustomPlatform, "Xbox", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(normalizedCustomPlatform, "Other", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(normalizedCustomPlatform, "Multiple Tags", StringComparison.OrdinalIgnoreCase);
                var captureTime = GetLibraryDate(file);
                var currentComment = ReadEmbeddedCommentDirect(file);
                var filteredTagText = string.Join(", ", tags.Where(tag =>
                    !string.Equals(tag, "Game Capture", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(tag, GamePhotographyTag, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(tag, "Photography", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(tag, "Steam", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(tag, "PC", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(tag, "PS5", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(tag, "PlayStation", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(tag, "Xbox", StringComparison.OrdinalIgnoreCase) &&
                    !tag.StartsWith(CustomPlatformPrefix, StringComparison.OrdinalIgnoreCase)));
                var addPhotographyTag = tags.Any(tag => string.Equals(tag, GamePhotographyTag, StringComparison.OrdinalIgnoreCase) || string.Equals(tag, "Photography", StringComparison.OrdinalIgnoreCase));
                var tagSteam = consoleTags.Contains("Steam");
                var tagPc = !consoleTags.Contains("Steam") && consoleTags.Contains("PC");
                var tagPs5 = consoleTags.Contains("PS5");
                var tagXbox = consoleTags.Contains("Xbox");
                var customPlatformValue = useCustomPlatform ? customPlatformName : string.Empty;
                items.Add(new ManualMetadataItem
                {
                    GameId = indexEntry == null ? (folder == null ? string.Empty : folder.GameId) : indexEntry.GameId,
                    FilePath = file,
                    FileName = fileName,
                    OriginalFileName = fileName,
                    CaptureTime = captureTime,
                    UseCustomCaptureTime = false,
                    GameName = folder.Name ?? string.Empty,
                    Comment = currentComment,
                    TagText = filteredTagText,
                    AddPhotographyTag = addPhotographyTag,
                    TagSteam = tagSteam,
                    TagPc = tagPc,
                    TagPs5 = tagPs5,
                    TagXbox = tagXbox,
                    TagOther = useCustomPlatform,
                    CustomPlatformTag = customPlatformValue,
                    OriginalGameId = indexEntry == null ? (folder == null ? string.Empty : folder.GameId) : indexEntry.GameId,
                    OriginalCaptureTime = captureTime,
                    OriginalUseCustomCaptureTime = false,
                    OriginalGameName = folder.Name ?? string.Empty,
                    OriginalComment = currentComment,
                    OriginalTagText = filteredTagText,
                    OriginalAddPhotographyTag = addPhotographyTag,
                    OriginalTagSteam = tagSteam,
                    OriginalTagPc = tagPc,
                    OriginalTagPs5 = tagPs5,
                    OriginalTagXbox = tagXbox,
                    OriginalTagOther = useCustomPlatform,
                    OriginalCustomPlatformTag = customPlatformValue
                });
            }
            return items;
        }

        int OrganizeLibraryItems(List<ManualMetadataItem> items, Action<int, int, string> progress = null)
        {
            int moved = 0, created = 0, renamedConflict = 0, skipped = 0;
            var total = items == null ? 0 : items.Count;
            var affectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var touchedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (progress != null) progress(0, total, "Starting organize step for " + total + " image(s).");
            for (int i = 0; i < total; i++)
            {
                var item = items[i];
                var remaining = total - (i + 1);
                if (!File.Exists(item.FilePath))
                {
                    skipped++;
                    if (progress != null) progress(i + 1, total, "Skipped organize " + (i + 1) + " of " + total + " | " + remaining + " remaining | file missing");
                    continue;
                }
                var gameName = string.IsNullOrWhiteSpace(item.GameName)
                    ? GetGameNameFromFileName(Path.GetFileNameWithoutExtension(item.FilePath))
                    : item.GameName;
                var targetDirectory = Path.Combine(libraryRoot, GetSafeGameFolderName(gameName));
                if (!Directory.Exists(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                    created++;
                }
                var currentDirectory = Path.GetDirectoryName(item.FilePath) ?? string.Empty;
                if (string.Equals(currentDirectory.TrimEnd(Path.DirectorySeparatorChar), targetDirectory.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                {
                    skipped++;
                    if (progress != null) progress(i + 1, total, "Already organized " + (i + 1) + " of " + total + " | " + remaining + " remaining | " + item.FileName);
                    continue;
                }
                var target = Path.Combine(targetDirectory, Path.GetFileName(item.FilePath));
                if (File.Exists(target))
                {
                    target = Unique(target);
                    renamedConflict++;
                }
                var oldName = item.FileName;
                var originalPath = item.FilePath;
                File.Move(item.FilePath, target);
                MoveMetadataSidecarIfPresent(originalPath, target);
                item.FilePath = target;
                item.FileName = Path.GetFileName(target);
                affectedFiles.Add(originalPath);
                affectedFiles.Add(target);
                if (!string.IsNullOrWhiteSpace(currentDirectory)) touchedDirectories.Add(currentDirectory);
                if (!string.IsNullOrWhiteSpace(targetDirectory)) touchedDirectories.Add(targetDirectory);
                moved++;
                Log("Library organize: " + oldName + " -> " + target);
                if (progress != null) progress(i + 1, total, "Organized " + (i + 1) + " of " + total + " | " + remaining + " remaining | " + item.FileName);
            }
            if (progress != null) progress(total, total, "Organize step complete: moved " + moved + ", folders created " + created + ", renamed-on-conflict " + renamedConflict + ", already-in-place " + skipped + ".");
            RemoveCachedImageEntries(affectedFiles);
            RemoveCachedFolderListings(touchedDirectories);
            RemoveCachedFileTagEntries(affectedFiles);
            Log("Library organize summary: moved " + moved + ", folders created " + created + ", renamed-on-conflict " + renamedConflict + ", already-in-place " + skipped + ".");
            return moved;
        }

        void PreserveLibraryMetadataEditGameIndex(string root, LibraryFolderInfo originalFolder, GameIndexEditorRow originalSavedRow, List<ManualMetadataItem> items)
        {
            if (string.IsNullOrWhiteSpace(root) || items == null || items.Count == 0) return;
            var preservedAppId = !string.IsNullOrWhiteSpace(originalSavedRow == null ? string.Empty : originalSavedRow.SteamAppId)
                ? originalSavedRow.SteamAppId
                : (originalFolder == null ? string.Empty : (originalFolder.SteamAppId ?? string.Empty));
            var preservedSteamGridDbId = !string.IsNullOrWhiteSpace(originalSavedRow == null ? string.Empty : originalSavedRow.SteamGridDbId)
                ? originalSavedRow.SteamGridDbId
                : (originalFolder == null ? string.Empty : (originalFolder.SteamGridDbId ?? string.Empty));
            if (string.IsNullOrWhiteSpace(preservedAppId) && string.IsNullOrWhiteSpace(preservedSteamGridDbId)) return;
            var rows = LoadSavedGameIndexRows(root);
            var sourceGameId = NormalizeGameId(originalSavedRow == null ? (originalFolder == null ? string.Empty : originalFolder.GameId) : originalSavedRow.GameId);
            var sourceName = NormalizeGameIndexName(originalSavedRow == null ? (originalFolder == null ? string.Empty : originalFolder.Name) : originalSavedRow.Name);
            var sourcePlatform = NormalizeConsoleLabel(originalSavedRow == null ? (originalFolder == null ? string.Empty : originalFolder.PlatformLabel) : originalSavedRow.PlatformLabel);
            var existing = !string.IsNullOrWhiteSpace(sourceGameId)
                ? FindSavedGameIndexRowById(rows, sourceGameId)
                : null;
            if (existing == null && !string.IsNullOrWhiteSpace(sourceName))
            {
                existing = FindSavedGameIndexRowByIdentity(rows, sourceName, sourcePlatform);
            }
            if (existing == null && (originalSavedRow != null || originalFolder != null))
            {
                existing = new GameIndexEditorRow
                {
                    GameId = !string.IsNullOrWhiteSpace(sourceGameId) ? sourceGameId : CreateGameId(rows.Select(row => row.GameId)),
                    Name = sourceName,
                    PlatformLabel = sourcePlatform,
                    SteamAppId = string.Empty,
                    SteamGridDbId = string.Empty,
                    FileCount = 0,
                    FolderPath = originalSavedRow == null ? (originalFolder == null ? string.Empty : originalFolder.FolderPath ?? string.Empty) : originalSavedRow.FolderPath ?? string.Empty,
                    PreviewImagePath = string.Empty,
                    FilePaths = new string[0]
                };
                rows.Add(existing);
            }
            if (existing == null) return;
            if (string.IsNullOrWhiteSpace(existing.GameId)) existing.GameId = !string.IsNullOrWhiteSpace(sourceGameId) ? sourceGameId : CreateGameId(rows.Select(row => row.GameId));
            if (string.IsNullOrWhiteSpace(existing.Name)) existing.Name = sourceName;
            if (string.IsNullOrWhiteSpace(existing.PlatformLabel)) existing.PlatformLabel = sourcePlatform;
            if (string.IsNullOrWhiteSpace(existing.SteamAppId)) existing.SteamAppId = preservedAppId;
            if (string.IsNullOrWhiteSpace(existing.SteamGridDbId)) existing.SteamGridDbId = preservedSteamGridDbId;
            SaveSavedGameIndexRows(root, rows);
        }
    }
}
