using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Threading;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        string TrimFilenameConventionSeparatorText(string value)
        {
            return (value ?? string.Empty).Trim(' ', '_', '-', '.', '(', ')', '[', ']');
        }

        bool TryFindFilenameTimestampToken(string value, out string tokenPattern, out string tokenFormat, out int tokenIndex, out int tokenLength)
        {
            tokenPattern = string.Empty;
            tokenFormat = string.Empty;
            tokenIndex = -1;
            tokenLength = 0;

            var candidates = new[]
            {
                new { Pattern = @"\d{14}", Format = "yyyyMMddHHmmss" },
                new { Pattern = @"\d{4}[-_]\d{2}[-_]\d{2}[ T_-]\d{2}[-_:]\d{2}[-_:]\d{2}\s?[AP]M", Format = "yyyy-MM-dd hh-mm-ss tt" },
                new { Pattern = @"\d{4}[-_]\d{2}[-_]\d{2}[ T_-]\d{2}[-_:]\d{2}[-_:]\d{2}", Format = "yyyy-MM-dd HH-mm-ss" },
                new { Pattern = @"\d{4}[-_]\d{2}[-_]\d{2}", Format = "yyyy-MM-dd" },
                new { Pattern = @"\d{8}", Format = "yyyyMMdd" }
            };

            foreach (var candidate in candidates)
            {
                var match = Regex.Match(value ?? string.Empty, candidate.Pattern, RegexOptions.IgnoreCase);
                if (!match.Success) continue;
                tokenPattern = candidate.Pattern;
                tokenFormat = candidate.Format;
                tokenIndex = match.Index;
                tokenLength = match.Length;
                return true;
            }
            return false;
        }

        string BuildFilenameConventionSuffixPattern(string suffix)
        {
            var cleaned = TrimFilenameConventionSeparatorText(suffix);
            if (string.IsNullOrWhiteSpace(cleaned)) return string.Empty;
            if (Regex.IsMatch(cleaned, @"^\d+$")) return @"(?:[_-]\d+)?";
            return @"(?:[_\-\s]+" + Regex.Escape(cleaned).Replace(@"\ ", @"\s+") + ")?";
        }

        string BuildFilenameConventionSuffixTokenText(string suffix)
        {
            var cleaned = TrimFilenameConventionSeparatorText(suffix);
            if (string.IsNullOrWhiteSpace(cleaned)) return string.Empty;
            if (Regex.IsMatch(cleaned, @"^\d+$")) return "[opt-counter]";
            return "_" + cleaned;
        }

        string FilenameTimestampTokenTextForFormat(string format)
        {
            switch ((format ?? string.Empty).Trim())
            {
                case "yyyyMMddHHmmss":
                    return "[yyyy][MM][dd][HH][mm][ss]";
                case "yyyy-MM-dd hh-mm-ss tt":
                    return "[yyyy]-[MM]-[dd] [hh]-[mm]-[ss] [tt]";
                case "yyyy-MM-dd HH-mm-ss":
                    return "[yyyy]-[MM]-[dd] [HH]-[mm]-[ss]";
                case "yyyy-MM-dd":
                    return "[yyyy]-[MM]-[dd]";
                case "yyyyMMdd":
                    return "[yyyy][MM][dd]";
                case "unix-ms":
                    return "[unixms]";
                default:
                    return string.Empty;
            }
        }

        string DefaultPlatformTagsTextForLabel(string platformLabel)
        {
            var normalized = NormalizeConsoleLabel(platformLabel);
            if (string.Equals(normalized, "Steam", StringComparison.OrdinalIgnoreCase)) return "Steam";
            if (string.Equals(normalized, "Xbox", StringComparison.OrdinalIgnoreCase)) return "Xbox";
            if (string.Equals(normalized, "Xbox PC", StringComparison.OrdinalIgnoreCase)) return "Platform:Xbox PC";
            if (string.Equals(normalized, "PS5", StringComparison.OrdinalIgnoreCase)) return "PS5;PlayStation";
            if (string.Equals(normalized, "PlayStation", StringComparison.OrdinalIgnoreCase)) return "PlayStation";
            if (string.Equals(normalized, "PC", StringComparison.OrdinalIgnoreCase)) return "PC";
            return string.IsNullOrWhiteSpace(normalized) || string.Equals(normalized, "Other", StringComparison.OrdinalIgnoreCase)
                ? string.Empty
                : normalized;
        }

        FilenameConventionRule BuildCustomFilenameConventionFromSample(FilenameConventionSample sample)
        {
            var fileName = Path.GetFileName(sample == null ? string.Empty : sample.FileName);
            var baseName = Path.GetFileNameWithoutExtension(fileName ?? string.Empty);
            var parsed = ParseFilename(fileName);
            var platformLabel = NormalizeConsoleLabel(string.IsNullOrWhiteSpace(parsed.PlatformLabel) ? (sample == null ? string.Empty : sample.SuggestedPlatformLabel) : parsed.PlatformLabel);
            if (string.IsNullOrWhiteSpace(platformLabel)) platformLabel = "Other";

            var rule = new FilenameConventionRule
            {
                ConventionId = "custom_" + Guid.NewGuid().ToString("N").Substring(0, 10),
                Name = "Custom: " + (string.IsNullOrWhiteSpace(fileName) ? "New Rule" : Path.GetFileNameWithoutExtension(fileName)),
                Enabled = true,
                Priority = 1200,
                PlatformLabel = platformLabel,
                PlatformTagsText = DefaultPlatformTagsTextForLabel(platformLabel),
                ConfidenceLabel = "CustomRule",
                IsBuiltIn = false
            };

            if (string.IsNullOrWhiteSpace(fileName))
            {
                rule.PatternText = "[title].[ext:media]";
                rule.Pattern = rule.PatternText;
                return rule;
            }

            if (Regex.IsMatch(fileName, @"^\d{3,}_\d{14}(?:[_-]\d+)?\.(png|jpe?g|mp4|mkv|avi|mov|wmv|webm)$", RegexOptions.IgnoreCase))
            {
                rule.Name = "Custom: Steam Screenshot";
                rule.PlatformLabel = "Steam";
                rule.PlatformTagsText = "Steam";
                rule.PatternText = "[appid]_[yyyy][MM][dd][HH][mm][ss][opt-counter].[ext:media]";
                rule.Pattern = rule.PatternText;
                rule.SteamAppIdGroup = "appid";
                rule.TimestampGroup = "stamp";
                rule.TimestampFormat = "yyyyMMddHHmmss";
                return rule;
            }

            if (Regex.IsMatch(fileName, @"^\d{14}(?:[_-]\d+)?\.(png|jpe?g|mp4|mkv|avi|mov|wmv|webm)$", RegexOptions.IgnoreCase))
            {
                rule.Name = "Custom: Steam Manual Export";
                rule.PlatformLabel = "Steam";
                rule.PlatformTagsText = "Steam";
                rule.PatternText = "[yyyy][MM][dd][HH][mm][ss][opt-counter].[ext:media]";
                rule.Pattern = rule.PatternText;
                rule.TimestampGroup = "stamp";
                rule.TimestampFormat = "yyyyMMddHHmmss";
                rule.RoutesToManualWhenMissingSteamAppId = true;
                return rule;
            }

            if (Regex.IsMatch(fileName, @"^clip_[\d,]{13,17}\.(mp4|mkv|avi|mov|wmv|webm)$", RegexOptions.IgnoreCase))
            {
                rule.Name = "Custom: Steam Clip";
                rule.PlatformLabel = "Steam";
                rule.PlatformTagsText = "Steam";
                rule.PatternText = "clip_[unixms].[ext:video]";
                rule.Pattern = rule.PatternText;
                rule.TimestampGroup = "stamp";
                rule.TimestampFormat = "unix-ms";
                return rule;
            }

            if (Regex.IsMatch(fileName, @"^.+?\s*-\s*\d{4}-\d{2}-\d{2}\s+\d{2}-\d{2}-\d{2}\s+[AP]M\.(png|jpe?g|mp4|mkv|avi|mov|wmv|webm)$", RegexOptions.IgnoreCase))
            {
                rule.Name = "Custom: Xbox Capture";
                rule.PlatformLabel = "Xbox";
                rule.PlatformTagsText = "Xbox";
                rule.PatternText = "[title] - [yyyy]-[MM]-[dd] [hh]-[mm]-[ss] [tt].[ext:media]";
                rule.Pattern = rule.PatternText;
                rule.TitleGroup = "title";
                rule.TimestampGroup = "stamp";
                rule.TimestampFormat = "yyyy-MM-dd hh-mm-ss tt";
                rule.PreserveFileTimes = true;
                return rule;
            }

            if (Regex.IsMatch(fileName, @"^.+?\s+\d{1,2}_\d{1,2}_\d{4}\s+\d{1,2}_\d{2}_\d{2}\s+[AP]M\.(png|jpe?g|mp4|mkv|avi|mov|wmv|webm)$", RegexOptions.IgnoreCase))
            {
                rule.Name = "Custom: Xbox PC Capture";
                rule.PlatformLabel = "Xbox PC";
                rule.PlatformTagsText = "Platform:Xbox PC";
                rule.PatternText = @"^(?<title>.+?)\s+(?<stamp>\d{1,2}_\d{1,2}_\d{4}\s+\d{1,2}_\d{2}_\d{2}\s+[AP]M)\.(png|jpe?g|mp4|mkv|avi|mov|wmv|webm)$";
                rule.Pattern = rule.PatternText;
                rule.TitleGroup = "title";
                rule.TimestampGroup = "stamp";
                rule.TimestampFormat = "M_d_yyyy h_mm_ss tt";
                rule.PreserveFileTimes = true;
                return rule;
            }

            if (Regex.IsMatch(fileName, @"^.+?_\d{14}\.(png|jpe?g|mp4|mkv|avi|mov|wmv|webm)$", RegexOptions.IgnoreCase))
            {
                rule.Name = "Custom: Title And Timestamp";
                rule.PatternText = "[title]_[yyyy][MM][dd][HH][mm][ss].[ext:media]";
                rule.Pattern = rule.PatternText;
                rule.TitleGroup = "title";
                rule.TimestampGroup = "stamp";
                rule.TimestampFormat = "yyyyMMddHHmmss";
                return rule;
            }

            string timestampPattern;
            string timestampFormat;
            int timestampIndex;
            int timestampLength;
            if (TryFindFilenameTimestampToken(baseName, out timestampPattern, out timestampFormat, out timestampIndex, out timestampLength))
            {
                var prefix = timestampIndex > 0 ? baseName.Substring(0, timestampIndex) : string.Empty;
                var suffix = timestampIndex + timestampLength < baseName.Length ? baseName.Substring(timestampIndex + timestampLength) : string.Empty;
                var prefixClean = TrimFilenameConventionSeparatorText(prefix);
                var suffixPattern = BuildFilenameConventionSuffixPattern(suffix);
                var extPattern = @"\.(png|jpe?g|mp4|mkv|avi|mov|wmv|webm)$";

                var appIdAndTitlePrefix = Regex.Match(prefixClean, @"^(?<appid>\d{3,})[_\-\s]+(?<title>.+)$", RegexOptions.IgnoreCase);
                if (appIdAndTitlePrefix.Success)
                {
                    rule.Name = "Custom: AppID, Title, And Timestamp";
                    var tokenTime = FilenameTimestampTokenTextForFormat(timestampFormat);
                    rule.Pattern = @"^(?<appid>\d{3,})[_\-\s]+(?<title>.+?)[_\-\s]+(?<stamp>" + timestampPattern + @")" + suffixPattern + extPattern;
                    rule.PatternText = string.IsNullOrWhiteSpace(tokenTime)
                        ? rule.Pattern
                        : "[appid]_[title]_" + tokenTime + BuildFilenameConventionSuffixTokenText(suffix) + ".[ext:media]";
                    rule.SteamAppIdGroup = "appid";
                    rule.TitleGroup = "title";
                    rule.TimestampGroup = "stamp";
                    rule.TimestampFormat = timestampFormat;
                    return rule;
                }

                if (Regex.IsMatch(prefixClean, @"^\d{3,}$", RegexOptions.IgnoreCase) && string.Equals(platformLabel, "Steam", StringComparison.OrdinalIgnoreCase))
                {
                    rule.Name = "Custom: AppID And Timestamp";
                    rule.PlatformLabel = "Steam";
                    rule.PlatformTagsText = "Steam";
                    var tokenTime = FilenameTimestampTokenTextForFormat(timestampFormat);
                    rule.Pattern = @"^(?<appid>\d{3,})[_\-\s]+(?<stamp>" + timestampPattern + @")" + suffixPattern + extPattern;
                    rule.PatternText = string.IsNullOrWhiteSpace(tokenTime)
                        ? rule.Pattern
                        : "[appid]_" + tokenTime + BuildFilenameConventionSuffixTokenText(suffix) + ".[ext:media]";
                    rule.SteamAppIdGroup = "appid";
                    rule.TimestampGroup = "stamp";
                    rule.TimestampFormat = timestampFormat;
                    return rule;
                }

                if (!string.IsNullOrWhiteSpace(prefixClean))
                {
                    rule.Name = "Custom: Title And Timestamp";
                    var tokenTime = FilenameTimestampTokenTextForFormat(timestampFormat);
                    rule.Pattern = @"^(?<title>.+?)[_\-\s]+(?<stamp>" + timestampPattern + @")" + suffixPattern + extPattern;
                    rule.PatternText = string.IsNullOrWhiteSpace(tokenTime)
                        ? rule.Pattern
                        : "[title]_" + tokenTime + BuildFilenameConventionSuffixTokenText(suffix) + ".[ext:media]";
                    rule.TitleGroup = "title";
                    rule.TimestampGroup = "stamp";
                    rule.TimestampFormat = timestampFormat;
                    return rule;
                }
            }

            var escapedPattern = Regex.Escape(fileName);
            var timestampMatch = Regex.Match(fileName, @"\d{14}");
            if (timestampMatch.Success)
            {
                escapedPattern = escapedPattern.Replace(Regex.Escape(timestampMatch.Value), @"(?<stamp>\d{14})");
                rule.TimestampGroup = "stamp";
                rule.TimestampFormat = "yyyyMMddHHmmss";
            }
            else
            {
                timestampMatch = Regex.Match(fileName, @"\d{8}");
                if (timestampMatch.Success)
                {
                    escapedPattern = escapedPattern.Replace(Regex.Escape(timestampMatch.Value), @"(?<stamp>\d{8})");
                    rule.TimestampGroup = "stamp";
                    rule.TimestampFormat = "yyyyMMdd";
                }
            }
            rule.Pattern = "^" + escapedPattern + "$";
            rule.PatternText = FilenameParserService.GetPatternEditorText(rule.Pattern);
            return rule;
        }

        void OpenFilenameConventionEditor()
        {
            if (string.IsNullOrWhiteSpace(libraryRoot) || !Directory.Exists(libraryRoot))
            {
                TryLibraryToast("Library folder not found. Check Settings before opening filename rules.");
                return;
            }
            if (filenameConventionEditorWindow != null)
            {
                if (filenameConventionEditorWindow.IsVisible)
                {
                    filenameConventionEditorWindow.Activate();
                    return;
                }
                filenameConventionEditorWindow = null;
            }

            try
            {
                FilenameConventionEditorWindow.Show(
                    this,
                    AppVersion,
                    libraryRoot,
                    w => filenameConventionEditorWindow = w,
                    w => { if (ReferenceEquals(filenameConventionEditorWindow, w)) filenameConventionEditorWindow = null; },
                    new FilenameConventionEditorServices
                    {
                        NotifyUser = (msg, icon) => TryLibraryToast(msg, icon),
                        RulesService = filenameRulesService,
                        ParserService = filenameParserService,
                        SetStatus = delegate(string text) { if (status != null) status.Text = text; },
                        Log = Log,
                        RefreshPreviewIfNeeded = delegate { RefreshPreview(); },
                        CreateButton = Btn,
                        NormalizeConsoleLabel = NormalizeConsoleLabel,
                        CleanTag = CleanTag
                    });
            }
            catch (Exception ex)
            {
                status.Text = "Filename rules unavailable";
                Log("Failed to open filename rules. " + ex.Message);
                TryLibraryToast("Could not open the filename rules." + Environment.NewLine + Environment.NewLine + ex.Message, MessageBoxImage.Error);
            }
        }
    }
}
