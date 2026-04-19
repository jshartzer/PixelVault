using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace PixelVaultNative
{
    public sealed partial class MainWindow
    {
        sealed class XboxPcCleanupPlan
        {
            public LibraryMetadataIndexEntry Entry;
            public string RewrittenTagText;
            public string RewrittenConsoleLabel;
            public bool RequiresExifWrite;
        }

        void RunXboxPcPlatformCleanupIfNeeded()
        {
            var root = libraryRoot ?? string.Empty;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return;

            var markerPath = BuildXboxPcCleanupMarkerPath(root);
            if (File.Exists(markerPath)) return;

            try
            {
                var canMarkComplete = ApplyXboxPcPlatformCleanup(root);
                if (!canMarkComplete) return;
                var markerDirectory = Path.GetDirectoryName(markerPath) ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(markerDirectory)) Directory.CreateDirectory(markerDirectory);
                File.WriteAllText(markerPath, "completed " + DateTime.UtcNow.ToString("O"));
            }
            catch (Exception ex)
            {
                Log("Xbox PC cleanup failed: " + ex.Message);
            }
        }

        bool ApplyXboxPcPlatformCleanup(string root)
        {
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return false;

            // Loading saved rows already normalizes and persists platform labels via MergeGameIndexRows.
            GetSavedGameIndexRowsForRoot(root);

            var rulesChanged = NormalizeSavedFilenameConventionsXboxPcToPc(root);
            var index = LoadLibraryMetadataIndex(root, true) ?? new Dictionary<string, LibraryMetadataIndexEntry>(StringComparer.OrdinalIgnoreCase);
            var plans = new List<XboxPcCleanupPlan>();
            var exifCandidates = new List<XboxPcCleanupPlan>();

            foreach (var entry in index.Values.Where(entry => entry != null && !string.IsNullOrWhiteSpace(entry.FilePath)))
            {
                var normalizedConsole = NormalizeConsoleLabel(entry.ConsoleLabel);
                var rewrittenTagText = RewriteXboxPcCleanupTagText(entry.TagText);
                var tagTextNeedsRewrite = XboxPcCleanupTagTextNeedsRewrite(entry.TagText);
                var consoleNeedsRewrite = IsXboxPcAliasValue(entry.ConsoleLabel);
                if (!tagTextNeedsRewrite && !consoleNeedsRewrite) continue;

                var rewrittenConsole = DetermineConsoleLabelFromTags(ParseTagText(rewrittenTagText));
                if (string.IsNullOrWhiteSpace(rewrittenConsole) || string.Equals(rewrittenConsole, "Other", StringComparison.OrdinalIgnoreCase))
                    rewrittenConsole = normalizedConsole;
                if (string.IsNullOrWhiteSpace(rewrittenConsole)) rewrittenConsole = "Other";

                var plan = new XboxPcCleanupPlan
                {
                    Entry = entry,
                    RewrittenTagText = rewrittenTagText,
                    RewrittenConsoleLabel = rewrittenConsole,
                    RequiresExifWrite = tagTextNeedsRewrite && File.Exists(entry.FilePath)
                };
                plans.Add(plan);
                if (plan.RequiresExifWrite) exifCandidates.Add(plan);
            }

            var canMarkComplete = true;
            HashSet<string> failedExifWrites = null;
            if (exifCandidates.Count > 0)
            {
                try
                {
                    metadataService.EnsureExifTool();
                    var requests = exifCandidates.Select(BuildXboxPcCleanupExifWriteRequest)
                        .Where(request => request != null && request.Arguments != null && request.Arguments.Length > 0)
                        .ToList();
                    if (requests.Count > 0)
                    {
                        var batch = metadataService.RunExifWriteRequests(requests, requests.Count, 0, null);
                        failedExifWrites = new HashSet<string>(
                            (batch.Failures ?? new List<ExifWriteFailure>())
                                .Where(failure => failure != null && !string.IsNullOrWhiteSpace(failure.FilePath))
                                .Select(failure => failure.FilePath),
                            StringComparer.OrdinalIgnoreCase);
                        if (failedExifWrites.Count > 0) canMarkComplete = false;
                    }
                }
                catch (Exception ex)
                {
                    canMarkComplete = false;
                    failedExifWrites = new HashSet<string>(exifCandidates.Select(plan => plan.Entry.FilePath), StringComparer.OrdinalIgnoreCase);
                    Log("Xbox PC cleanup metadata rewrite skipped: " + ex.Message);
                }
            }

            var indexChanged = false;
            var affectedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var plan in plans)
            {
                if (plan == null || plan.Entry == null) continue;
                if (plan.RequiresExifWrite && failedExifWrites != null && failedExifWrites.Contains(plan.Entry.FilePath)) continue;

                var entry = plan.Entry;
                if (!string.Equals(entry.TagText ?? string.Empty, plan.RewrittenTagText ?? string.Empty, StringComparison.Ordinal)
                    || !string.Equals(entry.ConsoleLabel ?? string.Empty, plan.RewrittenConsoleLabel ?? string.Empty, StringComparison.OrdinalIgnoreCase))
                {
                    entry.TagText = plan.RewrittenTagText ?? string.Empty;
                    entry.ConsoleLabel = plan.RewrittenConsoleLabel ?? string.Empty;
                    index[entry.FilePath] = entry;
                    indexChanged = true;
                    affectedFiles.Add(entry.FilePath);
                }
            }

            if (indexChanged)
            {
                SaveLibraryMetadataIndex(root, index);
                libraryScanner.RefreshFolderCacheAfterGameIndexChange(root);
                RemoveCachedFileTagEntries(affectedFiles);
                RemoveCachedFolderListings(affectedFiles.Select(file => Path.GetDirectoryName(file) ?? string.Empty));
                var refresh = activeLibraryFolderRefresh;
                if (refresh != null && string.Equals(root, libraryRoot, StringComparison.OrdinalIgnoreCase))
                    refresh(false);
            }

            if (rulesChanged || indexChanged || exifCandidates.Count > 0)
            {
                Log("Xbox PC cleanup: rules=" + (rulesChanged ? "updated" : "unchanged")
                    + ", indexRows=" + plans.Count
                    + ", exifWrites=" + exifCandidates.Count
                    + ", completed=" + canMarkComplete);
            }

            return canMarkComplete;
        }

        bool NormalizeSavedFilenameConventionsXboxPcToPc(string root)
        {
            var rules = indexPersistenceService.LoadFilenameConventions(root) ?? new List<FilenameConventionRule>();
            var changed = false;
            foreach (var rule in rules)
            {
                if (RewriteXboxPcFilenameConventionRuleInPlace(rule)) changed = true;
            }
            if (changed) indexPersistenceService.SaveFilenameConventions(root, rules);
            return changed;
        }

        ExifWriteRequest BuildXboxPcCleanupExifWriteRequest(XboxPcCleanupPlan plan)
        {
            if (plan == null || plan.Entry == null || string.IsNullOrWhiteSpace(plan.Entry.FilePath) || !File.Exists(plan.Entry.FilePath)) return null;
            var filePath = plan.Entry.FilePath;
            var originalCreate = DateTime.MinValue;
            var originalWrite = DateTime.MinValue;
            try
            {
                originalCreate = File.GetCreationTime(filePath);
                originalWrite = File.GetLastWriteTime(filePath);
            }
            catch
            {
            }
            return new ExifWriteRequest
            {
                FilePath = filePath,
                FileName = Path.GetFileName(filePath),
                Arguments = BuildExifArgs(
                    filePath,
                    GetLibraryDate(filePath),
                    new string[0],
                    ParseTagText(plan.RewrittenTagText),
                    false,
                    string.Empty,
                    false,
                    false,
                    false,
                    true),
                RestoreFileTimes = true,
                OriginalCreateTime = originalCreate,
                OriginalWriteTime = originalWrite,
                SuccessDetail = "Xbox PC -> PC tags | " + Path.GetFileName(filePath)
            };
        }

        string BuildXboxPcCleanupMarkerPath(string root)
        {
            var normalizedRoot = root ?? string.Empty;
            try
            {
                normalizedRoot = Path.GetFullPath(normalizedRoot.Trim());
            }
            catch
            {
            }

            using (var sha = SHA256.Create())
            {
                var hash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(normalizedRoot.ToLowerInvariant())))
                    .Replace("-", string.Empty)
                    .ToLowerInvariant();
                return Path.Combine(cacheRoot ?? string.Empty, "migrations", "xbox-pc-to-pc-" + hash + ".done");
            }
        }

        internal static bool IsXboxPcAliasValue(string value)
        {
            var cleaned = CleanTag(value);
            return string.Equals(cleaned, "Xbox PC", StringComparison.OrdinalIgnoreCase)
                || string.Equals(cleaned, "Xbox/Windows", StringComparison.OrdinalIgnoreCase)
                || string.Equals(cleaned, "Xbox Windows", StringComparison.OrdinalIgnoreCase)
                || string.Equals(cleaned, "Xbox on Windows", StringComparison.OrdinalIgnoreCase);
        }

        internal static bool IsXboxPcAliasTag(string tag)
        {
            var cleaned = CleanTag(tag);
            if (string.IsNullOrWhiteSpace(cleaned)) return false;
            if (cleaned.StartsWith(CustomPlatformPrefix, StringComparison.OrdinalIgnoreCase))
                return IsXboxPcAliasValue(cleaned.Substring(CustomPlatformPrefix.Length));
            return IsXboxPcAliasValue(cleaned);
        }

        internal static string[] RewriteXboxPcCleanupTags(IEnumerable<string> tags)
        {
            var rewritten = new List<string>();
            foreach (var raw in tags ?? Enumerable.Empty<string>())
            {
                var cleaned = CleanTag(raw);
                if (string.IsNullOrWhiteSpace(cleaned)) continue;
                rewritten.Add(IsXboxPcAliasTag(cleaned) ? "PC" : cleaned);
            }
            return rewritten
                .Where(tag => !string.IsNullOrWhiteSpace(tag))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        internal static string RewriteXboxPcCleanupTagText(string tagText)
        {
            return string.Join(", ", RewriteXboxPcCleanupTags(ParseTagText(tagText)));
        }

        internal static bool XboxPcCleanupTagTextNeedsRewrite(string tagText)
        {
            var original = ParseTagText(tagText);
            var rewritten = RewriteXboxPcCleanupTags(original);
            return !original.SequenceEqual(rewritten, StringComparer.OrdinalIgnoreCase);
        }

        internal static bool RewriteXboxPcFilenameConventionRuleInPlace(FilenameConventionRule rule)
        {
            if (rule == null) return false;
            var changed = false;

            if (IsXboxPcAliasValue(rule.PlatformLabel))
            {
                rule.PlatformLabel = "PC";
                changed = true;
            }
            else
            {
                var normalizedPlatform = NormalizeConsoleLabel(rule.PlatformLabel);
                if (!string.Equals(rule.PlatformLabel ?? string.Empty, normalizedPlatform ?? string.Empty, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(normalizedPlatform, "PC", StringComparison.OrdinalIgnoreCase))
                {
                    rule.PlatformLabel = normalizedPlatform;
                    changed = true;
                }
            }

            var originalTags = ParseTagText(rule.PlatformTagsText);
            var rewrittenTags = RewriteXboxPcCleanupTags(originalTags);
            if (!originalTags.SequenceEqual(rewrittenTags, StringComparer.OrdinalIgnoreCase))
            {
                rule.PlatformTagsText = string.Join("; ", rewrittenTags);
                changed = true;
            }

            if (!string.IsNullOrWhiteSpace(rule.Name))
            {
                var rewrittenName = rule.Name
                    .Replace("Xbox PC", "PC", StringComparison.OrdinalIgnoreCase)
                    .Replace("Xbox/Windows", "PC", StringComparison.OrdinalIgnoreCase)
                    .Replace("Xbox Windows", "PC", StringComparison.OrdinalIgnoreCase)
                    .Replace("Xbox on Windows", "PC", StringComparison.OrdinalIgnoreCase);
                if (!string.Equals(rewrittenName, rule.Name, StringComparison.Ordinal))
                {
                    rule.Name = rewrittenName;
                    changed = true;
                }
            }

            return changed;
        }
    }
}
