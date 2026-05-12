using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace PixelVaultNative
{
    interface IMetadataService
    {
        string[] BuildExifArgs(string file, DateTime dt, string[] platformTags, bool preserveFileTimes, string comment, bool addPhotographyTag);
        string[] BuildExifArgs(string file, DateTime dt, string[] platformTags, IEnumerable<string> extraTags, bool preserveFileTimes, string comment, bool addPhotographyTag);
        string[] BuildExifArgs(string file, DateTime dt, string[] platformTags, IEnumerable<string> extraTags, bool preserveFileTimes, string comment, bool addPhotographyTag, bool writeDateMetadata, bool writeCommentMetadata, bool writeTagMetadata);
        string[] ReadEmbeddedKeywordTagsDirect(string file, CancellationToken cancellationToken = default(CancellationToken));
        string ReadEmbeddedCommentDirect(string file, CancellationToken cancellationToken = default(CancellationToken));
        DateTime? ReadEmbeddedCaptureDateDirect(string file, CancellationToken cancellationToken = default(CancellationToken));
        Dictionary<string, string[]> ReadEmbeddedKeywordTagsBatch(IEnumerable<string> files, CancellationToken cancellationToken = default(CancellationToken));
        Dictionary<string, EmbeddedMetadataSnapshot> ReadEmbeddedMetadataBatch(IEnumerable<string> files, CancellationToken cancellationToken = default(CancellationToken));
        Task<Dictionary<string, string[]>> ReadEmbeddedKeywordTagsBatchAsync(IEnumerable<string> files, CancellationToken cancellationToken = default(CancellationToken));
        Task<Dictionary<string, EmbeddedMetadataSnapshot>> ReadEmbeddedMetadataBatchAsync(IEnumerable<string> files, CancellationToken cancellationToken = default(CancellationToken));
        int? ReadEmbeddedRatingDirect(string file, CancellationToken cancellationToken = default(CancellationToken));
        string[] BuildStarRatingExifArgs(string file, bool starred);
        void EnsureExifTool();
        void RunExifToolBatch(IReadOnlyList<ExifWriteRequest> requests);
        ExifWriteBatchResult RunExifWriteRequests(List<ExifWriteRequest> requests, int totalCount, int alreadyCompleted, Action<int, int, string> progress = null, CancellationToken cancellationToken = default(CancellationToken));
    }

    sealed class MetadataServiceDependencies
    {
        public Func<string> GetExifToolPath;
        public string CacheRoot;
        public Func<string, bool> IsVideo;
        public Func<string, string> MetadataSidecarPath;
        public Func<string, string> MetadataReadPath;
        public Func<IEnumerable<string>, IEnumerable<string>, bool, string[]> BuildMetadataTagSet;
        public Func<string, string> CleanComment;
        public Func<string, string> CleanTag;
        public Func<string, DateTime?> ParseEmbeddedMetadataDateValue;
        public Func<int, int> GetMetadataWorkerCount;
        public Action<string> Log;
        public Action<string, string[], string, bool> RunExe;
        public Func<string, string[], string, bool, CancellationToken, string> RunExeCapture;
    }

    sealed class MetadataService : IMetadataService
    {
        internal const string ExifToolTempSuffix = "_exiftool_tmp";

        readonly MetadataServiceDependencies dependencies;

        public MetadataService(MetadataServiceDependencies dependencies)
        {
            this.dependencies = dependencies ?? throw new ArgumentNullException(nameof(dependencies));
        }

        string ExifToolPath
        {
            get { return dependencies.GetExifToolPath == null ? string.Empty : dependencies.GetExifToolPath() ?? string.Empty; }
        }

        string CleanComment(string value)
        {
            return dependencies.CleanComment == null ? (value ?? string.Empty) : dependencies.CleanComment(value ?? string.Empty);
        }

        string CleanTag(string value)
        {
            return dependencies.CleanTag == null ? (value ?? string.Empty) : dependencies.CleanTag(value ?? string.Empty);
        }

        bool IsVideo(string file)
        {
            return dependencies.IsVideo != null && dependencies.IsVideo(file);
        }

        string MetadataSidecarPath(string file)
        {
            return dependencies.MetadataSidecarPath == null ? null : dependencies.MetadataSidecarPath(file);
        }

        string MetadataReadPath(string file)
        {
            return dependencies.MetadataReadPath == null ? file : dependencies.MetadataReadPath(file);
        }

        void Log(string message)
        {
            if (dependencies.Log != null) dependencies.Log(message);
        }

        void RunExe(string file, string[] args, string cwd, bool logOutput)
        {
            if (dependencies.RunExe != null) dependencies.RunExe(file, args, cwd, logOutput);
        }

        string RunExeCapture(string file, string[] args, string cwd, bool logOutput, CancellationToken cancellationToken = default(CancellationToken))
        {
            return dependencies.RunExeCapture == null ? string.Empty : dependencies.RunExeCapture(file, args, cwd, logOutput, cancellationToken);
        }

        string NormalizeExifToolPathKey(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return string.Empty;
            var normalized = path.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar).Trim();
            try
            {
                return Path.GetFullPath(normalized);
            }
            catch
            {
                return normalized;
            }
        }

        internal static bool IsExifToolTempPath(string path)
        {
            return !string.IsNullOrWhiteSpace(path)
                && path.EndsWith(ExifToolTempSuffix, StringComparison.OrdinalIgnoreCase);
        }

        internal static string BuildExifToolTempPath(string targetPath)
        {
            return string.IsNullOrWhiteSpace(targetPath) ? string.Empty : targetPath + ExifToolTempSuffix;
        }

        internal static string ResolveExifWriteTargetPathForCleanup(ExifWriteRequest request)
        {
            if (request != null && request.Arguments != null)
            {
                for (var i = request.Arguments.Length - 1; i >= 0; i--)
                {
                    var candidate = request.Arguments[i];
                    if (string.IsNullOrWhiteSpace(candidate)) continue;
                    if (candidate.StartsWith("-", StringComparison.Ordinal)) continue;
                    return candidate;
                }
            }
            return request == null ? string.Empty : request.FilePath ?? string.Empty;
        }

        internal static bool TryDeleteExifToolTempFile(string tempPath, Action<string> log = null)
        {
            if (!IsExifToolTempPath(tempPath)) return false;
            try
            {
                if (!File.Exists(tempPath)) return false;
                File.Delete(tempPath);
                log?.Invoke("Removed ExifTool temp file: " + tempPath);
                return true;
            }
            catch (Exception ex)
            {
                log?.Invoke("Could not remove ExifTool temp file: " + tempPath + ". " + ex.Message);
                return false;
            }
        }

        internal static bool TryDeleteExifToolTempForTarget(string targetPath, Action<string> log = null)
        {
            return TryDeleteExifToolTempFile(BuildExifToolTempPath(targetPath), log);
        }

        internal static bool TryDeleteExifToolTempForRequest(ExifWriteRequest request, Action<string> log = null)
        {
            if (request == null) return false;
            var deleted = false;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var target in new[] { ResolveExifWriteTargetPathForCleanup(request), request.FilePath ?? string.Empty })
            {
                if (string.IsNullOrWhiteSpace(target) || !seen.Add(target)) continue;
                deleted |= TryDeleteExifToolTempForTarget(target, log);
            }
            return deleted;
        }

        /// <summary>ExifTool <c>-overwrite_original</c> writes via a sibling <c>_exiftool_tmp</c> file; if a prior run crashed, that temp blocks the next write (&quot;temporary file already exists&quot;).</summary>
        static void DeleteStaleExifToolTempsBeforeWrite(IEnumerable<ExifWriteRequest> requests, Action<string> log)
        {
            if (requests == null) return;
            foreach (var request in requests)
            {
                if (request?.Arguments == null || request.Arguments.Length == 0) continue;
                TryDeleteExifToolTempForRequest(request, log);
            }
        }

        public string[] BuildExifArgs(string file, DateTime dt, string[] platformTags, bool preserveFileTimes, string comment, bool addPhotographyTag)
        {
            return BuildExifArgs(file, dt, platformTags, null, preserveFileTimes, comment, addPhotographyTag, true, true, true);
        }

        public string[] BuildExifArgs(string file, DateTime dt, string[] platformTags, IEnumerable<string> extraTags, bool preserveFileTimes, string comment, bool addPhotographyTag)
        {
            return BuildExifArgs(file, dt, platformTags, extraTags, preserveFileTimes, comment, addPhotographyTag, true, true, true);
        }

        public string[] BuildExifArgs(string file, DateTime dt, string[] platformTags, IEnumerable<string> extraTags, bool preserveFileTimes, string comment, bool addPhotographyTag, bool writeDateMetadata, bool writeCommentMetadata, bool writeTagMetadata)
        {
            var sidecar = MetadataSidecarPath(file);
            var writesMetadataToSidecar = !string.IsNullOrWhiteSpace(sidecar);
            var targetPath = writesMetadataToSidecar ? sidecar : file;
            var contentIsPng = !writesMetadataToSidecar && FileContentHasPngSignature(file);
            var args = new List<string>();
            var png = dt.ToString("yyyy:MM:dd HH:mm:ss");
            var std = dt.ToString("yyyyMMdd HH:mm:ss");
            if (writeDateMetadata)
            {
                if (writesMetadataToSidecar)
                {
                    args.Add("-XMP:DateTimeOriginal=" + std);
                    args.Add("-XMP:CreateDate=" + std);
                    args.Add("-XMP:ModifyDate=" + std);
                    args.Add("-XMP:MetadataDate=" + std);
                }
                else if (contentIsPng)
                {
                    args.Add("-PNG:CreationTime=" + png);
                    args.Add("-PNG:ModifyDate=" + png);
                    args.Add("-XMP:DateTimeOriginal=" + std);
                    args.Add("-XMP:CreateDate=" + std);
                    args.Add("-XMP:ModifyDate=" + std);
                    args.Add("-XMP:MetadataDate=" + std);
                }
                else
                {
                    args.Add("-EXIF:DateTimeOriginal=" + std);
                    args.Add("-EXIF:CreateDate=" + std);
                    args.Add("-EXIF:ModifyDate=" + std);
                    args.Add("-XMP:DateTimeOriginal=" + std);
                    args.Add("-XMP:CreateDate=" + std);
                    args.Add("-XMP:ModifyDate=" + std);
                    args.Add("-XMP:MetadataDate=" + std);
                }
                if (!preserveFileTimes && !writesMetadataToSidecar)
                {
                    args.Add("-File:FileCreateDate=" + std);
                    args.Add("-File:FileModifyDate=" + std);
                }
            }
            var cleanedComment = CleanComment(comment);
            if (writeCommentMetadata)
            {
                if (!string.IsNullOrWhiteSpace(cleanedComment))
                {
                    args.Add("-XMP-dc:Description-x-default=" + cleanedComment);
                    args.Add("-XMP-dc:Description=" + cleanedComment);
                    args.Add("-XMP-exif:UserComment=" + cleanedComment);
                    if (!writesMetadataToSidecar)
                    {
                        args.Add("-EXIF:ImageDescription=" + cleanedComment);
                        args.Add("-EXIF:UserComment=" + cleanedComment);
                        args.Add("-IPTC:Caption-Abstract=" + cleanedComment);
                        if (contentIsPng) args.Add("-PNG:Comment=" + cleanedComment);
                    }
                }
                else
                {
                    args.Add("-XMP-dc:Description-x-default=");
                    args.Add("-XMP-dc:Description=");
                    args.Add("-XMP-exif:UserComment=");
                    if (!writesMetadataToSidecar)
                    {
                        args.Add("-EXIF:ImageDescription=");
                        args.Add("-EXIF:UserComment=");
                        args.Add("-IPTC:Caption-Abstract=");
                        if (contentIsPng) args.Add("-PNG:Comment=");
                    }
                }
            }
            if (writeTagMetadata)
            {
                var tags = dependencies.BuildMetadataTagSet == null
                    ? new string[0]
                    : dependencies.BuildMetadataTagSet(platformTags, extraTags, addPhotographyTag);
                var serializedTags = string.Join("||", tags);
                args.Add("-sep");
                args.Add("||");
                args.Add("-XMP:Subject=" + serializedTags);
                args.Add("-XMP-dc:Subject=" + serializedTags);
                args.Add("-XMP:TagsList=" + serializedTags);
                args.Add("-XMP-digiKam:TagsList=" + serializedTags);
                args.Add("-XMP-lr:HierarchicalSubject=" + serializedTags);
                if (!writesMetadataToSidecar)
                {
                    args.Add("-IPTC:Keywords=" + serializedTags);
                    args.Add("-Keywords=" + serializedTags);
                }
            }
            args.Add("-overwrite_original");
            args.Add(targetPath);
            return args.ToArray();
        }

        static bool FileContentHasPngSignature(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
            try
            {
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var sig = new byte[8];
                    if (fs.Read(sig, 0, 8) != 8) return false;
                    return sig[0] == 0x89 && sig[1] == 0x50 && sig[2] == 0x4E && sig[3] == 0x47
                        && sig[4] == 0x0D && sig[5] == 0x0A && sig[6] == 0x1A && sig[7] == 0x0A;
                }
            }
            catch
            {
                return false;
            }
        }

        public string[] ReadEmbeddedKeywordTagsDirect(string file, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) return new string[0];
            if (string.IsNullOrWhiteSpace(ExifToolPath) || !File.Exists(ExifToolPath)) return new string[0];
            var readTarget = MetadataReadPath(file);
            if (string.IsNullOrWhiteSpace(readTarget) || !File.Exists(readTarget)) return new string[0];
            cancellationToken.ThrowIfCancellationRequested();
            var output = RunExeCapture(ExifToolPath, new[] { "-s3", "-XMP-digiKam:TagsList", "-XMP-lr:HierarchicalSubject", "-XMP-dc:Subject", "-XMP:Subject", "-XMP:TagsList", "-IPTC:Keywords", readTarget }, Path.GetDirectoryName(ExifToolPath), false, cancellationToken);
            return output
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .SelectMany(ParseTagText)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public string ReadEmbeddedCommentDirect(string file, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) return string.Empty;
            if (string.IsNullOrWhiteSpace(ExifToolPath) || !File.Exists(ExifToolPath)) return string.Empty;
            var readTarget = MetadataReadPath(file);
            if (string.IsNullOrWhiteSpace(readTarget) || !File.Exists(readTarget)) return string.Empty;
            cancellationToken.ThrowIfCancellationRequested();
            var output = RunExeCapture(
                ExifToolPath,
                new[]
                {
                    "-s3",
                    "-XMP-dc:Description-x-default",
                    "-XMP-dc:Description",
                    "-XMP-exif:UserComment",
                    "-EXIF:ImageDescription",
                    "-EXIF:UserComment",
                    "-IPTC:Caption-Abstract",
                    "-PNG:Comment",
                    readTarget
                },
                Path.GetDirectoryName(ExifToolPath),
                false,
                cancellationToken);
            return output
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(CleanComment)
                .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))
                ?? string.Empty;
        }

        public DateTime? ReadEmbeddedCaptureDateDirect(string file, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) return null;
            if (string.IsNullOrWhiteSpace(ExifToolPath) || !File.Exists(ExifToolPath)) return null;
            var readTarget = MetadataReadPath(file);
            if (string.IsNullOrWhiteSpace(readTarget) || !File.Exists(readTarget)) return null;
            cancellationToken.ThrowIfCancellationRequested();
            var output = RunExeCapture(
                ExifToolPath,
                new[]
                {
                    "-s3",
                    "-XMP:DateTimeOriginal",
                    "-XMP:CreateDate",
                    "-XMP:ModifyDate",
                    "-EXIF:DateTimeOriginal",
                    "-EXIF:CreateDate",
                    "-EXIF:ModifyDate",
                    "-QuickTime:CreateDate",
                    "-QuickTime:ModifyDate",
                    readTarget
                },
                Path.GetDirectoryName(ExifToolPath),
                false,
                cancellationToken);
            return output
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(ParseEmbeddedMetadataDateValue)
                .FirstOrDefault(parsed => parsed.HasValue);
        }

        static int? ParseEmbeddedRatingField(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            raw = raw.Trim();
            if (raw == "-" || raw.Equals("unknown", StringComparison.OrdinalIgnoreCase)) return null;
            if (int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n))
            {
                if (n < 0 || n > 5) return null;
                return n;
            }
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            {
                var r = (int)Math.Round(d, MidpointRounding.AwayFromZero);
                if (r < 0 || r > 5) return null;
                return r;
            }
            return null;
        }

        public int? ReadEmbeddedRatingDirect(string file, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) return null;
            if (string.IsNullOrWhiteSpace(ExifToolPath) || !File.Exists(ExifToolPath)) return null;
            var readTarget = MetadataReadPath(file);
            if (string.IsNullOrWhiteSpace(readTarget) || !File.Exists(readTarget)) return null;
            cancellationToken.ThrowIfCancellationRequested();
            var output = RunExeCapture(ExifToolPath, new[] { "-s3", "-XMP:Rating", readTarget }, Path.GetDirectoryName(ExifToolPath), false, cancellationToken);
            var line = (output ?? string.Empty).Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            return ParseEmbeddedRatingField(line);
        }

        Tuple<string, string> ReadEmbeddedCameraMakeModelDirect(string file, CancellationToken cancellationToken = default(CancellationToken))
        {
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) return Tuple.Create(string.Empty, string.Empty);
            if (string.IsNullOrWhiteSpace(ExifToolPath) || !File.Exists(ExifToolPath)) return Tuple.Create(string.Empty, string.Empty);
            var readTarget = MetadataReadPath(file);
            if (string.IsNullOrWhiteSpace(readTarget) || !File.Exists(readTarget)) return Tuple.Create(string.Empty, string.Empty);
            cancellationToken.ThrowIfCancellationRequested();
            var output = RunExeCapture(ExifToolPath, new[] { "-T", "-Make", "-Model", readTarget }, Path.GetDirectoryName(ExifToolPath), false, cancellationToken);
            var line = (output ?? string.Empty).Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(line)) return Tuple.Create(string.Empty, string.Empty);
            var parts = line.Split('\t');
            var make = parts.Length > 0 && parts[0] != "-" ? CleanTag(parts[0]) : string.Empty;
            var model = parts.Length > 1 && parts[1] != "-" ? CleanTag(parts[1]) : string.Empty;
            return Tuple.Create(make, model);
        }

        public string[] BuildStarRatingExifArgs(string file, bool starred)
        {
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) return Array.Empty<string>();
            if (string.IsNullOrWhiteSpace(ExifToolPath) || !File.Exists(ExifToolPath)) return Array.Empty<string>();
            var rating = starred ? "5" : "0";
            var sidecar = MetadataSidecarPath(file);
            var writesToSidecar = !string.IsNullOrWhiteSpace(sidecar);
            var targetPath = writesToSidecar ? sidecar : file;
            if (string.IsNullOrWhiteSpace(targetPath)) return Array.Empty<string>();
            // Sidecar may not exist yet (video / JPEG XR HDR); ExifTool creates it on first write.
            if (!writesToSidecar && !File.Exists(targetPath)) return Array.Empty<string>();
            // Rename-based overwrite matches <see cref="BuildExifArgs"/> (manual metadata). In-place JPEG updates fail sporadically on Windows
            // when scanners/indexers or brief handles contend — Switch exports like "*_c.jpg" behave as normal JPEGs but hit this often.
            return new[] { "-XMP:Rating=" + rating, "-overwrite_original", targetPath };
        }

        public Dictionary<string, string[]> ReadEmbeddedKeywordTagsBatch(IEnumerable<string> files, CancellationToken cancellationToken = default(CancellationToken))
        {
            var result = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            var sourceFiles = (files ?? Enumerable.Empty<string>())
                .Where(file => !string.IsNullOrWhiteSpace(file) && File.Exists(file))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var file in sourceFiles) result[file] = new string[0];
            if (sourceFiles.Count == 0) return result;
            if (string.IsNullOrWhiteSpace(ExifToolPath) || !File.Exists(ExifToolPath)) return result;

            var readTargets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var targetToSource = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in sourceFiles)
            {
                var readTarget = MetadataReadPath(file);
                if (string.IsNullOrWhiteSpace(readTarget) || !File.Exists(readTarget)) continue;
                readTargets[file] = readTarget;
                targetToSource[NormalizeExifToolPathKey(readTarget)] = file;
            }
            if (readTargets.Count == 0) return result;
            var orderedReadTargets = readTargets.OrderBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase).ToList();

            var argFile = Path.Combine(dependencies.CacheRoot, "exiftool-batch-read-" + Guid.NewGuid().ToString("N") + ".args");
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var argLines = new List<string>
                {
                    "-T",
                    "-sep",
                    "||",
                    "-Directory",
                    "-FileName",
                    "-XMP-digiKam:TagsList",
                    "-XMP-lr:HierarchicalSubject",
                    "-XMP-dc:Subject",
                    "-XMP:Subject",
                    "-XMP:TagsList",
                    "-IPTC:Keywords"
                };
                argLines.AddRange(orderedReadTargets.Select(pair => pair.Value));
                File.WriteAllLines(argFile, argLines.ToArray(), Encoding.UTF8);
                var output = RunExeCapture(ExifToolPath, new[] { "-@", argFile }, Path.GetDirectoryName(ExifToolPath), false, cancellationToken);
                var matchedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var outputLines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                for (int lineIndex = 0; lineIndex < outputLines.Length; lineIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var line = outputLines[lineIndex];
                    var parts = line.Split('\t');
                    if (parts.Length < 2) continue;
                    var directoryPart = parts[0] == "-" ? string.Empty : parts[0];
                    var fileNamePart = parts[1] == "-" ? string.Empty : parts[1];
                    var exifPath = NormalizeExifToolPathKey(Path.Combine(directoryPart, fileNamePart));
                    string sourceFile;
                    if (!targetToSource.TryGetValue(exifPath, out sourceFile))
                    {
                        if (lineIndex >= orderedReadTargets.Count) continue;
                        sourceFile = orderedReadTargets[lineIndex].Key;
                    }
                    var tags = new List<string>();
                    for (int i = 2; i < parts.Length; i++)
                    {
                        foreach (var value in parts[i].Split(new[] { "||" }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            var tag = CleanTag(value);
                            if (!string.IsNullOrWhiteSpace(tag) && tag != "-") tags.Add(tag);
                        }
                    }
                    result[sourceFile] = tags.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    matchedFiles.Add(sourceFile);
                }
                foreach (var pair in readTargets)
                {
                    if (matchedFiles.Contains(pair.Key)) continue;
                    result[pair.Key] = ReadEmbeddedKeywordTagsDirect(pair.Key, cancellationToken);
                }
            }
            finally
            {
                if (File.Exists(argFile)) File.Delete(argFile);
            }
            return result;
        }

        public Dictionary<string, EmbeddedMetadataSnapshot> ReadEmbeddedMetadataBatch(IEnumerable<string> files, CancellationToken cancellationToken = default(CancellationToken))
        {
            var result = new Dictionary<string, EmbeddedMetadataSnapshot>(StringComparer.OrdinalIgnoreCase);
            var sourceFiles = (files ?? Enumerable.Empty<string>())
                .Where(file => !string.IsNullOrWhiteSpace(file) && File.Exists(file))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            foreach (var file in sourceFiles) result[file] = new EmbeddedMetadataSnapshot();
            if (sourceFiles.Count == 0) return result;
            if (string.IsNullOrWhiteSpace(ExifToolPath) || !File.Exists(ExifToolPath)) return result;

            var readTargets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var targetToSource = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var file in sourceFiles)
            {
                var readTarget = MetadataReadPath(file);
                if (string.IsNullOrWhiteSpace(readTarget) || !File.Exists(readTarget)) continue;
                readTargets[file] = readTarget;
                targetToSource[NormalizeExifToolPathKey(readTarget)] = file;
            }
            if (readTargets.Count == 0) return result;
            var orderedReadTargets = readTargets.OrderBy(pair => pair.Value, StringComparer.OrdinalIgnoreCase).ToList();

            var argFile = Path.Combine(dependencies.CacheRoot, "exiftool-batch-metadata-" + Guid.NewGuid().ToString("N") + ".args");
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var argLines = new List<string>
                {
                    "-T",
                    "-sep",
                    "||",
                    "-Directory",
                    "-FileName",
                    "-XMP-digiKam:TagsList",
                    "-XMP-lr:HierarchicalSubject",
                    "-XMP-dc:Subject",
                    "-XMP:Subject",
                    "-XMP:TagsList",
                    "-IPTC:Keywords",
                    "-XMP-dc:Description-x-default",
                    "-XMP-dc:Description",
                    "-XMP-exif:UserComment",
                    "-EXIF:ImageDescription",
                    "-EXIF:UserComment",
                    "-IPTC:Caption-Abstract",
                    "-PNG:Comment",
                    "-XMP:DateTimeOriginal",
                    "-XMP:CreateDate",
                    "-XMP:ModifyDate",
                    "-EXIF:DateTimeOriginal",
                    "-EXIF:CreateDate",
                    "-EXIF:ModifyDate",
                    "-QuickTime:CreateDate",
                    "-QuickTime:ModifyDate",
                    "-XMP:Rating",
                    "-Make",
                    "-Model"
                };
                argLines.AddRange(orderedReadTargets.Select(pair => pair.Value));
                File.WriteAllLines(argFile, argLines.ToArray(), Encoding.UTF8);
                var output = RunExeCapture(ExifToolPath, new[] { "-@", argFile }, Path.GetDirectoryName(ExifToolPath), false, cancellationToken);
                var matchedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var outputLines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                for (int lineIndex = 0; lineIndex < outputLines.Length; lineIndex++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var line = outputLines[lineIndex];
                    var parts = line.Split('\t');
                    if (parts.Length < 2) continue;
                    var directoryPart = parts[0] == "-" ? string.Empty : parts[0];
                    var fileNamePart = parts[1] == "-" ? string.Empty : parts[1];
                    var exifPath = NormalizeExifToolPathKey(Path.Combine(directoryPart, fileNamePart));
                    string sourceFile;
                    if (!targetToSource.TryGetValue(exifPath, out sourceFile))
                    {
                        if (lineIndex >= orderedReadTargets.Count) continue;
                        sourceFile = orderedReadTargets[lineIndex].Key;
                    }

                    var snapshot = new EmbeddedMetadataSnapshot();
                    var tags = new List<string>();
                    for (int i = 2; i <= 7 && i < parts.Length; i++)
                    {
                        foreach (var value in parts[i].Split(new[] { "||" }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            var tag = CleanTag(value);
                            if (!string.IsNullOrWhiteSpace(tag) && tag != "-") tags.Add(tag);
                        }
                    }
                    snapshot.Tags = tags.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

                    for (int i = 8; i <= 14 && i < parts.Length; i++)
                    {
                        var comment = CleanComment(parts[i]);
                        if (string.IsNullOrWhiteSpace(comment) || comment == "-") continue;
                        snapshot.Comment = comment;
                        break;
                    }

                    for (int i = 15; i <= 21 && i < parts.Length; i++)
                    {
                        var parsed = ParseEmbeddedMetadataDateValue(parts[i]);
                        if (!parsed.HasValue) continue;
                        snapshot.CaptureTime = parsed.Value;
                        break;
                    }

                    if (parts.Length > 22) snapshot.Rating = ParseEmbeddedRatingField(parts[22]);
                    if (parts.Length > 23 && parts[23] != "-") snapshot.CameraMake = CleanTag(parts[23]);
                    if (parts.Length > 24 && parts[24] != "-") snapshot.CameraModel = CleanTag(parts[24]);

                    result[sourceFile] = snapshot;
                    matchedFiles.Add(sourceFile);
                }

                foreach (var pair in readTargets)
                {
                    if (matchedFiles.Contains(pair.Key)) continue;
                    result[pair.Key] = new EmbeddedMetadataSnapshot
                    {
                        Tags = ReadEmbeddedKeywordTagsDirect(pair.Key, cancellationToken),
                        Comment = ReadEmbeddedCommentDirect(pair.Key, cancellationToken),
                        CaptureTime = ReadEmbeddedCaptureDateDirect(pair.Key, cancellationToken),
                        Rating = ReadEmbeddedRatingDirect(pair.Key, cancellationToken)
                    };
                    var camera = ReadEmbeddedCameraMakeModelDirect(pair.Key, cancellationToken);
                    result[pair.Key].CameraMake = camera.Item1;
                    result[pair.Key].CameraModel = camera.Item2;
                }
            }
            finally
            {
                if (File.Exists(argFile)) File.Delete(argFile);
            }

            return result;
        }

        /// <summary>Batch keyword read on the **thread pool** (ExifTool is CPU/process-bound). Await with <c>ConfigureAwait(false)</c> when calling from UI code until you marshal results back to the dispatcher.</summary>
        public Task<Dictionary<string, string[]>> ReadEmbeddedKeywordTagsBatchAsync(IEnumerable<string> files, CancellationToken cancellationToken = default(CancellationToken))
        {
            var list = (files ?? Enumerable.Empty<string>())
                .Where(file => !string.IsNullOrWhiteSpace(file) && File.Exists(file))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (list.Count == 0) return Task.FromResult(new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase));
            return Task.Run(() => ReadEmbeddedKeywordTagsBatch(list, cancellationToken), cancellationToken);
        }

        /// <summary>Batch embedded-metadata read on the **thread pool** (ExifTool is CPU/process-bound). Await with <c>ConfigureAwait(false)</c> when calling from UI code until you marshal results back to the dispatcher.</summary>
        public Task<Dictionary<string, EmbeddedMetadataSnapshot>> ReadEmbeddedMetadataBatchAsync(IEnumerable<string> files, CancellationToken cancellationToken = default(CancellationToken))
        {
            var list = (files ?? Enumerable.Empty<string>())
                .Where(file => !string.IsNullOrWhiteSpace(file) && File.Exists(file))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (list.Count == 0) return Task.FromResult(new Dictionary<string, EmbeddedMetadataSnapshot>(StringComparer.OrdinalIgnoreCase));
            return Task.Run(() => ReadEmbeddedMetadataBatch(list, cancellationToken), cancellationToken);
        }

        public void EnsureExifTool()
        {
            if (!File.Exists(ExifToolPath)) throw new InvalidOperationException("ExifTool not found: " + ExifToolPath);
            var support = Path.Combine(Path.GetDirectoryName(ExifToolPath), "exiftool_files");
            if (Path.GetFileName(ExifToolPath).Equals("exiftool.exe", StringComparison.OrdinalIgnoreCase) && !Directory.Exists(support)) throw new InvalidOperationException("ExifTool support folder missing: " + support);
            RunExe(ExifToolPath, new[] { "-ver" }, Path.GetDirectoryName(ExifToolPath), false);
        }

        /// <summary>
        /// Runs each write as its own ExifTool process. Avoids <c>-stay_open</c> + <c>-overwrite_original</c>,
        /// which often triggers spurious &quot;Temporary file already exists&quot; on Windows when scanners touch <c>_exiftool_tmp</c>.
        /// </summary>
        void RunExifToolSingleRequestWithRetries(string cwd, ExifWriteRequest request)
        {
            InvalidOperationException last = null;
            for (var attempt = 1; attempt <= 4; attempt++)
            {
                try
                {
                    FileSystemService.TryClearReadOnlyForFile(request.FilePath);
                    var exifWriteTarget = ResolveExifWriteTargetPathForCleanup(request);
                    if (!string.IsNullOrWhiteSpace(exifWriteTarget)
                        && !string.Equals(exifWriteTarget, request.FilePath, StringComparison.OrdinalIgnoreCase))
                        FileSystemService.TryClearReadOnlyForFile(exifWriteTarget);
                    DeleteStaleExifToolTempsBeforeWrite(new[] { request }, Log);
                    RunExe(ExifToolPath, request.Arguments, cwd, false);
                    return;
                }
                catch (InvalidOperationException ex)
                {
                    last = ex;
                    TryDeleteExifToolTempForRequest(request, Log);
                    if (attempt >= 4) throw;
                    Log("ExifTool write attempt " + attempt + "/4 failed; retrying. " + ex.Message);
                    Thread.Sleep(80 * attempt);
                }
            }
            throw last;
        }

        public void RunExifToolBatch(IReadOnlyList<ExifWriteRequest> requests)
        {
            if (requests == null || requests.Count == 0) return;

            var cwd = Path.GetDirectoryName(ExifToolPath);
            DeleteStaleExifToolTempsBeforeWrite(requests, Log);
            foreach (var request in requests.Where(entry => entry != null && entry.Arguments != null && entry.Arguments.Length > 0))
            {
                try
                {
                    RunExifToolSingleRequestWithRetries(cwd, request);
                }
                finally
                {
                    TryDeleteExifToolTempForRequest(request, Log);
                }
            }
        }

        public ExifWriteBatchResult RunExifWriteRequests(List<ExifWriteRequest> requests, int totalCount, int alreadyCompleted, Action<int, int, string> progress = null, CancellationToken cancellationToken = default(CancellationToken))
        {
            var workItems = requests ?? new List<ExifWriteRequest>();
            if (workItems.Count == 0) return new ExifWriteBatchResult();
            cancellationToken.ThrowIfCancellationRequested();

            var completed = alreadyCompleted;
            var failures = new ConcurrentQueue<ExifWriteFailure>();
            var workerCount = dependencies.GetMetadataWorkerCount == null ? 1 : dependencies.GetMetadataWorkerCount(workItems.Count);
            var batchSize = Math.Max(1, Math.Min(24, (int)Math.Ceiling((double)workItems.Count / workerCount)));
            var batches = workItems.Chunk(batchSize).ToList();
            Log("Running metadata writes with " + workerCount + " worker(s) across " + batches.Count + " ExifTool batch(es) for " + workItems.Count + " file(s).");

            Action<ExifWriteRequest> finalizeRequest = delegate(ExifWriteRequest request)
            {
                if (request.RestoreFileTimes)
                {
                    if (request.OriginalCreateTime != DateTime.MinValue) File.SetCreationTime(request.FilePath, request.OriginalCreateTime);
                    if (request.OriginalWriteTime != DateTime.MinValue) File.SetLastWriteTime(request.FilePath, request.OriginalWriteTime);
                }
                if (progress != null)
                {
                    var current = Interlocked.Increment(ref completed);
                    var remaining = Math.Max(totalCount - current, 0);
                    progress(current, totalCount, "Updated metadata " + current + " of " + totalCount + " | " + remaining + " remaining | " + request.SuccessDetail);
                }
            };

            Action<ExifWriteRequest> runSingleRequest = delegate(ExifWriteRequest request)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    RunExifToolSingleRequestWithRetries(Path.GetDirectoryName(ExifToolPath), request);
                }
                finally
                {
                    TryDeleteExifToolTempForRequest(request, Log);
                }
                finalizeRequest(request);
            };

            Parallel.ForEach(batches, new ParallelOptions { MaxDegreeOfParallelism = workerCount, CancellationToken = cancellationToken }, delegate(ExifWriteRequest[] batch)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    RunExifToolBatch(batch);
                    foreach (var request in batch)
                    {
                        finalizeRequest(request);
                    }
                }
                catch (Exception ex)
                {
                    Log("Metadata batch fallback: " + ex.Message);
                    foreach (var request in batch)
                    {
                        try
                        {
                            runSingleRequest(request);
                        }
                        catch (Exception itemEx)
                        {
                            failures.Enqueue(new ExifWriteFailure
                            {
                                FilePath = request.FilePath,
                                FileName = request.FileName,
                                ErrorMessage = itemEx.Message
                            });
                        }
                    }
                }
            });

            var failureList = failures.ToList();
            return new ExifWriteBatchResult
            {
                SuccessCount = workItems.Count - failureList.Count,
                Failures = failureList
            };
        }

        IEnumerable<string> ParseTagText(string raw)
        {
            return (raw ?? string.Empty)
                .Split(new[] { "||", ";", ",", "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(CleanTag)
                .Where(tag => !string.IsNullOrWhiteSpace(tag));
        }

        DateTime? ParseEmbeddedMetadataDateValue(string value)
        {
            return dependencies.ParseEmbeddedMetadataDateValue == null ? null : dependencies.ParseEmbeddedMetadataDateValue(value);
        }
    }
}
