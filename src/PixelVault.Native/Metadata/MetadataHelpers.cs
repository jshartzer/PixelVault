using System;
using System.Globalization;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

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

        bool ShouldIncludeGameCaptureKeywords()
        {
            bool includeGameCaptureKeywords = true;
            if (keywordsBox != null)
            {
                if (keywordsBox.Dispatcher.CheckAccess()) includeGameCaptureKeywords = keywordsBox.IsChecked == true;
                else includeGameCaptureKeywords = (bool)keywordsBox.Dispatcher.Invoke(new Func<bool>(delegate { return keywordsBox.IsChecked == true; }));
            }
            return includeGameCaptureKeywords;
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
            if (item.TagPs5) { extraTags.Add("PS5"); extraTags.Add("PlayStation"); }
            if (item.TagXbox) extraTags.Add("Xbox");
            if (item.TagOther && !string.IsNullOrWhiteSpace(item.CustomPlatformTag)) extraTags.Add(CustomPlatformPrefix + CleanTag(item.CustomPlatformTag));
            return extraTags;
        }

        string[] ReadEmbeddedKeywordTagsDirect(string file)
        {
            return metadataService.ReadEmbeddedKeywordTagsDirect(file);
        }

        string ReadEmbeddedCommentDirect(string file)
        {
            return metadataService.ReadEmbeddedCommentDirect(file);
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

        DateTime? ReadEmbeddedCaptureDateDirect(string file)
        {
            return metadataService.ReadEmbeddedCaptureDateDirect(file);
        }

        string NormalizeConsoleLabel(string label)
        {
            if (string.IsNullOrWhiteSpace(label)) return "Other";
            if (string.Equals(label, "Steam", StringComparison.OrdinalIgnoreCase)) return "Steam";
            if (string.Equals(label, "PC", StringComparison.OrdinalIgnoreCase)) return "PC";
            if (string.Equals(label, "PlayStation", StringComparison.OrdinalIgnoreCase) || string.Equals(label, "PS5", StringComparison.OrdinalIgnoreCase)) return "PS5";
            if (string.Equals(label, "Xbox", StringComparison.OrdinalIgnoreCase)) return "Xbox";
            if (string.Equals(label, "Multiple Tags", StringComparison.OrdinalIgnoreCase)) return "Multiple Tags";
            return CleanTag(label);
        }

        string[] ExtractConsolePlatformFamilies(IEnumerable<string> tags)
        {
            var labels = new List<string>();
            var tagList = (tags ?? Enumerable.Empty<string>()).Where(tag => !string.IsNullOrWhiteSpace(tag)).Select(CleanTag).ToList();
            if (tagList.Any(tag => string.Equals(tag, "Steam", StringComparison.OrdinalIgnoreCase))) labels.Add("Steam");
            else if (tagList.Any(tag => string.Equals(tag, "PC", StringComparison.OrdinalIgnoreCase))) labels.Add("PC");
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

        string DetermineConsoleLabelFromTags(IEnumerable<string> tags)
        {
            var labels = ExtractConsolePlatformFamilies(tags);
            if (labels.Length > 1) return "Multiple Tags";
            if (labels.Length == 1) return labels[0];
            return "Other";
        }

        string[] GetEmbeddedKeywordTags(string file)
        {
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) return new string[0];
            var stamp = MetadataCacheStamp(file);
            string[] cachedTags;
            long cachedStamp;
            if (fileTagCache.TryGetValue(file, out cachedTags) && fileTagCacheStamp.TryGetValue(file, out cachedStamp) && cachedStamp == stamp)
            {
                return cachedTags;
            }
            var tags = ReadEmbeddedKeywordTagsDirect(file);
            fileTagCache[file] = tags;
            fileTagCacheStamp[file] = stamp;
            return tags;
        }

        string[] GetConsolePlatformTagsForFile(string file)
        {
            return ExtractConsolePlatformFamilies(GetEmbeddedKeywordTags(file));
        }

        string DetermineFolderPlatform(List<string> files, Dictionary<string, LibraryMetadataIndexEntry> index)
        {
            var labels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in files)
            {
                LibraryMetadataIndexEntry entry;
                string indexedLabel;
                if (index != null && index.TryGetValue(file, out entry))
                {
                    indexedLabel = NormalizeConsoleLabel(DetermineConsoleLabelFromTags(ParseTagText(entry.TagText)));
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
                    ? GetGameNameFromFileName(Path.GetFileNameWithoutExtension(item.FilePath))
                    : item.GameName);
            var originalName = NormalizeGameIndexName(
                string.IsNullOrWhiteSpace(item.OriginalGameName)
                    ? GetGameNameFromFileName(Path.GetFileNameWithoutExtension(item.FilePath))
                    : item.OriginalGameName);
            if (!string.Equals(currentName, originalName, StringComparison.OrdinalIgnoreCase)) return true;
            return !string.Equals(
                NormalizeConsoleLabel(DetermineManualMetadataPlatformLabel(item)),
                NormalizeConsoleLabel(DetermineOriginalManualMetadataPlatformLabel(item)),
                StringComparison.OrdinalIgnoreCase);
        }

        static bool ManualMetadataTouchesTags(ManualMetadataItem item)
        {
            if (item == null) return false;
            return !SameManualText(item.TagText, item.OriginalTagText)
                || item.AddPhotographyTag != item.OriginalAddPhotographyTag
                || item.TagSteam != item.OriginalTagSteam
                || item.TagPc != item.OriginalTagPc
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

        static bool CanUpdateMetadata(string file) { return IsVideo(file) || DetectPlatformTags(file).Contains("Xbox") || ParseCaptureDate(file).HasValue; }

        static string[] DetectPlatformTags(string file)
        {
            var tags = new List<string>();
            if (Regex.IsMatch(file, @"^.+_\d{14}_\d+\.(png|jpe?g|mp4|mkv|avi|mov|wmv|webm)$", RegexOptions.IgnoreCase))
            {
                tags.Add("Steam");
            }
            else if (Regex.IsMatch(file, @"^.+_\d{4}-\d{2}-\d{2}_\d+\.(png|jpe?g|mp4|mkv|avi|mov|wmv|webm)$", RegexOptions.IgnoreCase))
            {
                tags.Add("Steam");
            }
            else if (Regex.IsMatch(file, @"^clip_[\d,]{13,17}\.(mp4|mkv|avi|mov|wmv|webm)$", RegexOptions.IgnoreCase))
            {
                tags.Add("Steam");
            }
            else if (Regex.IsMatch(file, @"^.+_\d{14}\.(png|jpe?g|mp4|mkv|avi|mov|wmv|webm)$", RegexOptions.IgnoreCase))
            {
                tags.Add("PS5");
                tags.Add("PlayStation");
            }
            if (Regex.IsMatch(file, @".+[-â€“â€”]\d{4}_\d{2}_\d{2}[-_]\d{2}[-_]\d{2}[-_]\d{2}\.(png|jpe?g|mp4|mkv|avi|mov|wmv|webm)$", RegexOptions.IgnoreCase)) tags.Add("Xbox");
            if (file.IndexOf("PS5", StringComparison.OrdinalIgnoreCase) >= 0) { tags.Add("PS5"); tags.Add("PlayStation"); }
            else if (file.IndexOf("PlayStation", StringComparison.OrdinalIgnoreCase) >= 0) tags.Add("PlayStation");
            return tags.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }
    }
}
