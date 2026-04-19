using System;
using System.Globalization;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        string[] BuildExifArgs(string file, DateTime dt, string[] platformTags, bool preserveFileTimes, string comment, bool addPhotographyTag)
        {
            return metadataService.BuildExifArgs(file, dt, platformTags, preserveFileTimes, comment, addPhotographyTag);
        }

        string[] BuildExifArgs(string file, DateTime dt, string[] platformTags, IEnumerable<string> extraTags, bool preserveFileTimes, string comment, bool addPhotographyTag)
        {
            return metadataService.BuildExifArgs(file, dt, platformTags, extraTags, preserveFileTimes, comment, addPhotographyTag);
        }

        string[] BuildExifArgs(string file, DateTime dt, string[] platformTags, IEnumerable<string> extraTags, bool preserveFileTimes, string comment, bool addPhotographyTag, bool writeDateMetadata, bool writeCommentMetadata, bool writeTagMetadata)
        {
            return metadataService.BuildExifArgs(file, dt, platformTags, extraTags, preserveFileTimes, comment, addPhotographyTag, writeDateMetadata, writeCommentMetadata, writeTagMetadata);
        }

        void SyncIncludeGameCaptureKeywordsMirror()
        {
        }

        bool ShouldIncludeGameCaptureKeywords()
        {
            return _includeGameCaptureKeywordsMirror;
        }

        string[] BuildMetadataTagSet(IEnumerable<string> platformTags, IEnumerable<string> extraTags, bool addPhotographyTag)
        {
            var tags = new List<string>();
            if (ShouldIncludeGameCaptureKeywords())
            {
                tags.Add("Game Capture");
                if (platformTags != null) tags.AddRange(platformTags.Where(tag => !string.IsNullOrWhiteSpace(tag)));
            }
            if (extraTags != null) tags.AddRange(extraTags.Where(tag => !string.IsNullOrWhiteSpace(tag)));
            if (addPhotographyTag) tags.Add(GamePhotographyTag);
            return tags.Select(CleanTag).Where(tag => !string.IsNullOrWhiteSpace(tag)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        List<string> BuildManualMetadataExtraTags(ManualMetadataItem item)
        {
            var extraTags = new List<string>(ParseTagText(item == null ? string.Empty : item.TagText));
            if (item == null) return extraTags;
            if (item.TagSteam) extraTags.Add("Steam");
            if (item.TagPc) extraTags.Add("PC");
            if (item.TagEmulation) extraTags.Add("Emulation");
            if (item.TagPs5) { extraTags.Add("PS5"); extraTags.Add("PlayStation"); }
            if (item.TagXbox) extraTags.Add("Xbox");
            if (item.TagOther && !string.IsNullOrWhiteSpace(item.CustomPlatformTag)) extraTags.Add(CustomPlatformPrefix + CleanTag(item.CustomPlatformTag));
            return extraTags;
        }

        string[] ReadEmbeddedKeywordTagsDirect(string file, CancellationToken cancellationToken = default(CancellationToken))
        {
            return metadataService.ReadEmbeddedKeywordTagsDirect(file, cancellationToken);
        }

        string ReadEmbeddedCommentDirect(string file, CancellationToken cancellationToken = default(CancellationToken))
        {
            return metadataService.ReadEmbeddedCommentDirect(file, cancellationToken);
        }

        static DateTime? ParseEmbeddedMetadataDateValue(string value)
        {
            var cleaned = CleanComment(value);
            if (string.IsNullOrWhiteSpace(cleaned) || cleaned == "-") return null;

            DateTimeOffset dto;
            if (DateTimeOffset.TryParse(cleaned, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out dto))
                return dto.LocalDateTime;

            DateTime dt;
            var exactFormats = new[]
            {
                "yyyy:MM:dd HH:mm:ss",
                "yyyy:MM:dd HH:mm:ssK",
                "yyyy:MM:dd HH:mm:sszzz",
                "yyyyMMdd HH:mm:ss",
                "yyyy-MM-dd HH:mm:ss",
                "yyyy-MM-ddTHH:mm:ss",
                "yyyy-MM-ddTHH:mm:ssK",
                "yyyy-MM-ddTHH:mm:sszzz"
            };
            if (DateTime.TryParseExact(cleaned, exactFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out dt))
                return DateTime.SpecifyKind(dt, DateTimeKind.Local);
            if (DateTime.TryParse(cleaned, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out dt))
                return DateTime.SpecifyKind(dt, DateTimeKind.Local);
            return null;
        }

        DateTime? ReadEmbeddedCaptureDateDirect(string file, CancellationToken cancellationToken = default(CancellationToken))
        {
            return metadataService.ReadEmbeddedCaptureDateDirect(file, cancellationToken);
        }

        internal static string NormalizeConsoleLabel(string label)
        {
            if (string.IsNullOrWhiteSpace(label)) return "Other";
            if (string.Equals(label, "Steam", StringComparison.OrdinalIgnoreCase)) return "Steam";
            if (string.Equals(label, "PC", StringComparison.OrdinalIgnoreCase)) return "PC";
            if (string.Equals(label, "Emulation", StringComparison.OrdinalIgnoreCase)) return "Emulation";
            if (string.Equals(label, "Xbox PC", StringComparison.OrdinalIgnoreCase)
                || string.Equals(label, "Xbox/Windows", StringComparison.OrdinalIgnoreCase)
                || string.Equals(label, "Xbox Windows", StringComparison.OrdinalIgnoreCase)
                || string.Equals(label, "Xbox on Windows", StringComparison.OrdinalIgnoreCase))
            {
                return "PC";
            }
            if (string.Equals(label, "PlayStation", StringComparison.OrdinalIgnoreCase) || string.Equals(label, "PS5", StringComparison.OrdinalIgnoreCase)) return "PS5";
            if (string.Equals(label, "Xbox", StringComparison.OrdinalIgnoreCase)) return "Xbox";
            if (string.Equals(label, "Multiple Tags", StringComparison.OrdinalIgnoreCase)) return "Multiple Tags";
            return CleanTag(label);
        }

        internal static string[] ExtractConsolePlatformFamilies(IEnumerable<string> tags)
        {
            var labels = new List<string>();
            var tagList = (tags ?? Enumerable.Empty<string>()).Where(tag => !string.IsNullOrWhiteSpace(tag)).Select(CleanTag).ToList();
            var hasPcTag = tagList.Any(tag => string.Equals(NormalizeConsoleLabel(tag), "PC", StringComparison.OrdinalIgnoreCase))
                || tagList
                    .Where(tag => tag.StartsWith(CustomPlatformPrefix, StringComparison.OrdinalIgnoreCase))
                    .Select(tag => CleanTag(tag.Substring(CustomPlatformPrefix.Length)))
                    .Any(tag => string.Equals(NormalizeConsoleLabel(tag), "PC", StringComparison.OrdinalIgnoreCase));
            if (tagList.Any(tag => string.Equals(tag, "Steam", StringComparison.OrdinalIgnoreCase))) labels.Add("Steam");
            else if (hasPcTag) labels.Add("PC");
            if (tagList.Any(tag => string.Equals(tag, "Emulation", StringComparison.OrdinalIgnoreCase))) labels.Add("Emulation");
            if (tagList.Any(tag => string.Equals(tag, "PS5", StringComparison.OrdinalIgnoreCase) || string.Equals(tag, "PlayStation", StringComparison.OrdinalIgnoreCase))) labels.Add("PS5");
            if (tagList.Any(tag => string.Equals(tag, "Xbox", StringComparison.OrdinalIgnoreCase))) labels.Add("Xbox");
            foreach (var custom in tagList.Where(tag => tag.StartsWith(CustomPlatformPrefix, StringComparison.OrdinalIgnoreCase)).Select(tag => CleanTag(tag.Substring(CustomPlatformPrefix.Length))))
            {
                if (string.IsNullOrWhiteSpace(custom)) continue;
                var normalizedCustom = NormalizeConsoleLabel(custom);
                if (string.Equals(normalizedCustom, "Other", StringComparison.OrdinalIgnoreCase) || string.Equals(normalizedCustom, "Multiple Tags", StringComparison.OrdinalIgnoreCase)) continue;
                labels.Add(normalizedCustom);
            }
            return labels.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        internal static string DetermineConsoleLabelFromTags(IEnumerable<string> tags)
        {
            var labels = ExtractConsolePlatformFamilies(tags);
            if (labels.Length > 1) return "Multiple Tags";
            if (labels.Length == 1) return labels[0];
            return "Other";
        }

        internal static bool ConsoleLabelBlocksFilenameFallback(string normalizedLabel)
        {
            if (string.IsNullOrWhiteSpace(normalizedLabel)) return false;
            if (string.Equals(normalizedLabel, "Other", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        /// <summary>Removes tags that <see cref="ExtractConsolePlatformFamilies"/> would treat as a console label (embedded Game Bar / GeForce noise must not override filename-based emulation).</summary>
        internal static string[] StripTagsThatParticipateInConsolePlatformDetection(IEnumerable<string> tags)
        {
            var kept = new List<string>();
            foreach (var raw in tags ?? Enumerable.Empty<string>())
            {
                if (string.IsNullOrWhiteSpace(raw)) continue;
                var cleaned = CleanTag(raw);
                if (string.IsNullOrWhiteSpace(cleaned)) continue;
                var fams = ExtractConsolePlatformFamilies(new[] { cleaned });
                if (fams != null && fams.Length > 0) continue;
                kept.Add(cleaned);
            }
            return kept.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        internal static string[] BuildFilenamePlatformHintTags(FilenameParseResult parsed)
        {
            var extras = (parsed == null ? Enumerable.Empty<string>() : (parsed.PlatformTags ?? new string[0]))
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(CleanTag)
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .ToList();
            var normalizedPlatform = NormalizeConsoleLabel(parsed == null ? string.Empty : parsed.PlatformLabel);
            if (string.Equals(normalizedPlatform, "Steam", StringComparison.OrdinalIgnoreCase)) extras.Add("Steam");
            else if (string.Equals(normalizedPlatform, "PC", StringComparison.OrdinalIgnoreCase)) extras.Add("PC");
            else if (string.Equals(normalizedPlatform, "Emulation", StringComparison.OrdinalIgnoreCase)) extras.Add("Emulation");
            else if (string.Equals(normalizedPlatform, "PS5", StringComparison.OrdinalIgnoreCase) || string.Equals(normalizedPlatform, "PlayStation", StringComparison.OrdinalIgnoreCase))
            {
                extras.Add("PS5");
                extras.Add("PlayStation");
            }
            else if (string.Equals(normalizedPlatform, "Xbox", StringComparison.OrdinalIgnoreCase)) extras.Add("Xbox");
            else if (!string.IsNullOrWhiteSpace(normalizedPlatform)
                && !string.Equals(normalizedPlatform, "Other", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(normalizedPlatform, "Multiple Tags", StringComparison.OrdinalIgnoreCase))
            {
                extras.Add(CustomPlatformPrefix + normalizedPlatform);
            }
            if (!FilenameParserService.ParsedResultHasSubstantiveSteamAppId(parsed))
                extras.RemoveAll(t => string.Equals(CleanTag(t), "Steam", StringComparison.OrdinalIgnoreCase));
            return extras
                .Select(CleanTag)
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        internal static string[] MergePlatformTagsWithFilenamePlatformHint(IEnumerable<string> tags, FilenameParseResult parsed)
        {
            var tagArray = (tags ?? Enumerable.Empty<string>())
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Select(CleanTag)
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (parsed != null && FilenameParserService.ParseResultUsesNonSteamShortcutIdentity(parsed))
            {
                var stripped = StripTagsThatParticipateInConsolePlatformDetection(tagArray);
                var shortcutHintTags = BuildFilenamePlatformHintTags(parsed);
                return stripped
                    .Concat(shortcutHintTags)
                    .Select(CleanTag)
                    .Where(t => !string.IsNullOrWhiteSpace(t))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
            }
            var label = NormalizeConsoleLabel(DetermineConsoleLabelFromTags(tagArray));
            if (ConsoleLabelBlocksFilenameFallback(label)) return tagArray;
            var extras = BuildFilenamePlatformHintTags(parsed);
            if (extras.Length == 0) return tagArray;
            return tagArray
                .Concat(extras)
                .Select(CleanTag)
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        internal static void ApplyFilenameParseResultToManualPlatformFlags(
            FilenameParseResult parsed,
            out bool tagSteam,
            out bool tagPc,
            out bool tagEmulation,
            out bool tagPs5,
            out bool tagXbox,
            out bool tagOther,
            out string customPlatformTag)
        {
            tagSteam = false;
            tagPc = false;
            tagEmulation = false;
            tagPs5 = false;
            tagXbox = false;
            tagOther = false;
            customPlatformTag = string.Empty;

            var resolvedPlatform = NormalizeConsoleLabel(parsed == null ? string.Empty : parsed.PlatformLabel);
            if (string.Equals(resolvedPlatform, "Other", StringComparison.OrdinalIgnoreCase))
            {
                resolvedPlatform = NormalizeConsoleLabel(DetermineConsoleLabelFromTags(BuildFilenamePlatformHintTags(parsed)));
            }
            if (string.IsNullOrWhiteSpace(resolvedPlatform)
                || string.Equals(resolvedPlatform, "Other", StringComparison.OrdinalIgnoreCase)
                || string.Equals(resolvedPlatform, "Multiple Tags", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            if (string.Equals(resolvedPlatform, "Steam", StringComparison.OrdinalIgnoreCase))
            {
                tagSteam = FilenameParserService.ParsedResultHasSubstantiveSteamAppId(parsed);
                return;
            }
            if (string.Equals(resolvedPlatform, "PC", StringComparison.OrdinalIgnoreCase))
            {
                tagPc = true;
                return;
            }
            if (string.Equals(resolvedPlatform, "Emulation", StringComparison.OrdinalIgnoreCase))
            {
                tagEmulation = true;
                return;
            }
            if (string.Equals(resolvedPlatform, "PS5", StringComparison.OrdinalIgnoreCase) || string.Equals(resolvedPlatform, "PlayStation", StringComparison.OrdinalIgnoreCase))
            {
                tagPs5 = true;
                return;
            }
            if (string.Equals(resolvedPlatform, "Xbox", StringComparison.OrdinalIgnoreCase))
            {
                tagXbox = true;
                return;
            }
            tagOther = true;
            customPlatformTag = resolvedPlatform;
        }

        string[] GetEmbeddedKeywordTags(string file)
        {
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) return new string[0];
            var stamp = MetadataCacheStamp(file);
            string[] cachedTags;
            if (TryGetCachedFileTags(file, stamp, out cachedTags)) return cachedTags;
            var tags = ReadEmbeddedKeywordTagsDirect(file);
            SetCachedFileTags(file, tags, stamp);
            return tags;
        }

        string[] GetConsolePlatformTagsForFile(string file)
        {
            return ExtractConsolePlatformFamilies(GetEmbeddedKeywordTags(file));
        }

        string DetermineFolderPlatform(List<string> files, Dictionary<string, LibraryMetadataIndexEntry> index, string root = null)
        {
            var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                LibraryMetadataIndexEntry entry;
                string indexedLabel;
                if (index != null && index.TryGetValue(file, out entry))
                {
                    indexedLabel = string.IsNullOrWhiteSpace(entry.ConsoleLabel)
                        ? NormalizeConsoleLabel(DetermineConsoleLabelFromTags(ParseTagText(entry.TagText)))
                        : NormalizeConsoleLabel(entry.ConsoleLabel);
                }
                else
                {
                    indexedLabel = NormalizeConsoleLabel(DetermineConsoleLabelFromTags(GetEmbeddedKeywordTags(file)));
                }
                labels.Add(indexedLabel);
            }
            if (labels.Count > 1) return "Multiple Tags";
            if (labels.Count == 1) return labels.First();
            return "Other";
        }

        string DetermineLibraryFolderGroup(LibraryFolderInfo folder)
        {
            return NormalizeConsoleLabel(folder == null ? string.Empty : folder.PlatformLabel);
        }

        string DetermineManualMetadataPlatformLabel(ManualMetadataItem item)
        {
            if (item == null) return "Other";
            if (item.TagSteam) return "Steam";
            if (item.TagPc) return "PC";
            if (item.TagEmulation) return "Emulation";
            if (item.TagPs5) return "PS5";
            if (item.TagXbox) return "Xbox";
            if (item.TagOther && !string.IsNullOrWhiteSpace(item.CustomPlatformTag)) return NormalizeConsoleLabel(item.CustomPlatformTag);
            return "Other";
        }

        string DetermineOriginalManualMetadataPlatformLabel(ManualMetadataItem item)
        {
            if (item == null) return "Other";
            if (item.OriginalTagSteam) return "Steam";
            if (item.OriginalTagPc) return "PC";
            if (item.OriginalTagEmulation) return "Emulation";
            if (item.OriginalTagPs5) return "PS5";
            if (item.OriginalTagXbox) return "Xbox";
            if (item.OriginalTagOther && !string.IsNullOrWhiteSpace(item.OriginalCustomPlatformTag)) return NormalizeConsoleLabel(item.OriginalCustomPlatformTag);
            return "Other";
        }

        bool ManualMetadataChangesGroupingIdentity(ManualMetadataItem item)
        {
            if (item == null) return false;
            var currentName = NormalizeGameIndexName(
                string.IsNullOrWhiteSpace(item.GameName)
                    ? GetGameNameFromFileName(Path.GetFileName(item.FilePath))
                    : item.GameName);
            var originalName = NormalizeGameIndexName(
                string.IsNullOrWhiteSpace(item.OriginalGameName)
                    ? GetGameNameFromFileName(Path.GetFileName(item.FilePath))
                    : item.OriginalGameName);
            if (!string.Equals(currentName, originalName, StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.Equals(
                NormalizeConsoleLabel(DetermineManualMetadataPlatformLabel(item)),
                NormalizeConsoleLabel(DetermineOriginalManualMetadataPlatformLabel(item)),
                StringComparison.OrdinalIgnoreCase))
                return true;
            if (!string.Equals(
                NormalizeGameId(item.GameId ?? string.Empty),
                NormalizeGameId(item.OriginalGameId ?? string.Empty),
                StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }

        /// <summary>
        /// When the edited title + platform match a row in the loaded game index, set <see cref="ManualMetadataItem.GameId"/> to that row
        /// so library apply does not keep a stale id (identity-first resolution expects consistent id + title).
        /// </summary>
        void SyncManualMetadataGameIdsFromGameTitleIndexRows(ManualMetadataDialogHost h, IEnumerable<ManualMetadataItem> items)
        {
            if (h == null) return;
            var rows = h.GameTitleIndexRows;
            if (rows == null || rows.Count == 0) return;
            foreach (var item in items ?? Enumerable.Empty<ManualMetadataItem>())
            {
                if (item == null) continue;
                var platform = DetermineManualMetadataPlatformLabel(item);
                var titleForIdentity = string.IsNullOrWhiteSpace(item.GameName)
                    ? GetGameNameFromFileName(Path.GetFileName(item.FilePath))
                    : item.GameName;
                var wantedIdentity = BuildGameIndexIdentity(titleForIdentity, platform);
                GameIndexEditorRow matched = null;
                foreach (var row in rows)
                {
                    if (row == null) continue;
                    if (string.Equals(BuildGameIndexIdentity(row.Name, row.PlatformLabel), wantedIdentity, StringComparison.OrdinalIgnoreCase))
                    {
                        matched = row;
                        break;
                    }
                }
                if (matched != null)
                {
                    item.GameId = NormalizeGameId(matched.GameId);
                    continue;
                }
                // Intake / import items start with OriginalGameId empty. GetGameNameFromFileName() can echo another game's title when
                // the same non-Steam shortcut ID is shared across ROMs, which previously synced a stale GameId that never cleared
                // after the user typed the real title.
                if (string.IsNullOrWhiteSpace(NormalizeGameId(item.OriginalGameId ?? string.Empty)))
                    item.GameId = string.Empty;
            }
        }

        static bool ManualMetadataTouchesTags(ManualMetadataItem item)
        {
            if (item == null) return false;
            return !SameManualText(item.TagText, item.OriginalTagText)
                || item.AddPhotographyTag != item.OriginalAddPhotographyTag
                || item.TagSteam != item.OriginalTagSteam
                || item.TagPc != item.OriginalTagPc
                || item.TagEmulation != item.OriginalTagEmulation
                || item.TagPs5 != item.OriginalTagPs5
                || item.TagXbox != item.OriginalTagXbox
                || item.TagOther != item.OriginalTagOther
                || !SameManualText(CleanTag(item.CustomPlatformTag), CleanTag(item.OriginalCustomPlatformTag));
        }

        static bool ManualMetadataTouchesComment(ManualMetadataItem item)
        {
            if (item == null) return false;
            return !SameManualText(item.Comment, item.OriginalComment);
        }

        static bool ManualMetadataTouchesCaptureTime(ManualMetadataItem item)
        {
            if (item == null) return false;
            return item.UseCustomCaptureTime != item.OriginalUseCustomCaptureTime
                || (item.UseCustomCaptureTime && item.CaptureTime != item.OriginalCaptureTime);
        }

        bool CanUpdateMetadata(string file)
        {
            var parsed = ParseFilename(file);
            if (parsed.RoutesToManualWhenMissingSteamAppId && string.IsNullOrWhiteSpace(parsed.SteamAppId))
            {
                return false;
            }
            return IsVideo(file) || parsed.PlatformTags.Contains("Xbox") || parsed.CaptureTime.HasValue;
        }

        string[] DetectPlatformTags(string file)
        {
            return ParseFilename(file).PlatformTags;
        }
    }
}
