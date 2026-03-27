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
        string[] ReadEmbeddedKeywordTagsDirect(string file);
        string ReadEmbeddedCommentDirect(string file);
        DateTime? ReadEmbeddedCaptureDateDirect(string file);
        Dictionary<string, string[]> ReadEmbeddedKeywordTagsBatch(IEnumerable<string> files);
        Dictionary<string, EmbeddedMetadataSnapshot> ReadEmbeddedMetadataBatch(IEnumerable<string> files);
        void EnsureExifTool();
        void RunExifToolBatch(IReadOnlyList<ExifWriteRequest> requests);
        int RunExifWriteRequests(List<ExifWriteRequest> requests, int totalCount, int alreadyCompleted, Action<int, int, string> progress = null);
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
        public Func<string, string[], string, bool, string> RunExeCapture;
    }

    sealed class MetadataService : IMetadataService
    {
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

        string RunExeCapture(string file, string[] args, string cwd, bool logOutput)
        {
            return dependencies.RunExeCapture == null ? string.Empty : dependencies.RunExeCapture(file, args, cwd, logOutput);
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
            var ext = Path.GetExtension(file).ToLowerInvariant();
            var targetPath = IsVideo(file) ? MetadataSidecarPath(file) : file;
            var args = new List<string>();
            var png = dt.ToString("yyyy:MM:dd HH:mm:ss");
            var std = dt.ToString("yyyyMMdd HH:mm:ss");
            if (writeDateMetadata)
            {
                if (IsVideo(file))
                {
                    args.Add("-XMP:DateTimeOriginal=" + std);
                    args.Add("-XMP:CreateDate=" + std);
                    args.Add("-XMP:ModifyDate=" + std);
                    args.Add("-XMP:MetadataDate=" + std);
                }
                else if (ext == ".png")
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
                if (!preserveFileTimes && !IsVideo(file))
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
                    if (!IsVideo(file))
                    {
                        args.Add("-EXIF:ImageDescription=" + cleanedComment);
                        args.Add("-EXIF:UserComment=" + cleanedComment);
                        args.Add("-IPTC:Caption-Abstract=" + cleanedComment);
                        if (ext == ".png") args.Add("-PNG:Comment=" + cleanedComment);
                    }
                }
                else
                {
                    args.Add("-XMP-dc:Description-x-default=");
                    args.Add("-XMP-dc:Description=");
                    args.Add("-XMP-exif:UserComment=");
                    if (!IsVideo(file))
                    {
                        args.Add("-EXIF:ImageDescription=");
                        args.Add("-EXIF:UserComment=");
                        args.Add("-IPTC:Caption-Abstract=");
                        if (ext == ".png") args.Add("-PNG:Comment=");
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
                if (!IsVideo(file))
                {
                    args.Add("-IPTC:Keywords=" + serializedTags);
                    args.Add("-Keywords=" + serializedTags);
                }
            }
            args.Add("-overwrite_original");
            args.Add(targetPath);
            return args.ToArray();
        }

        public string[] ReadEmbeddedKeywordTagsDirect(string file)
        {
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) return new string[0];
            if (string.IsNullOrWhiteSpace(ExifToolPath) || !File.Exists(ExifToolPath)) return new string[0];
            var readTarget = MetadataReadPath(file);
            if (string.IsNullOrWhiteSpace(readTarget) || !File.Exists(readTarget)) return new string[0];
            var output = RunExeCapture(ExifToolPath, new[] { "-s3", "-XMP-digiKam:TagsList", "-XMP-lr:HierarchicalSubject", "-XMP-dc:Subject", "-XMP:Subject", "-XMP:TagsList", "-IPTC:Keywords", readTarget }, Path.GetDirectoryName(ExifToolPath), false);
            return output
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .SelectMany(ParseTagText)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        public string ReadEmbeddedCommentDirect(string file)
        {
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) return string.Empty;
            if (string.IsNullOrWhiteSpace(ExifToolPath) || !File.Exists(ExifToolPath)) return string.Empty;
            var readTarget = MetadataReadPath(file);
            if (string.IsNullOrWhiteSpace(readTarget) || !File.Exists(readTarget)) return string.Empty;
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
                false);
            return output
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(CleanComment)
                .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line))
                ?? string.Empty;
        }

        public DateTime? ReadEmbeddedCaptureDateDirect(string file)
        {
            if (string.IsNullOrWhiteSpace(file) || !File.Exists(file)) return null;
            if (string.IsNullOrWhiteSpace(ExifToolPath) || !File.Exists(ExifToolPath)) return null;
            var readTarget = MetadataReadPath(file);
            if (string.IsNullOrWhiteSpace(readTarget) || !File.Exists(readTarget)) return null;
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
                false);
            return output
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                .Select(ParseEmbeddedMetadataDateValue)
                .FirstOrDefault(parsed => parsed.HasValue);
        }

        public Dictionary<string, string[]> ReadEmbeddedKeywordTagsBatch(IEnumerable<string> files)
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
                var output = RunExeCapture(ExifToolPath, new[] { "-@", argFile }, Path.GetDirectoryName(ExifToolPath), false);
                var matchedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var outputLines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                for (int lineIndex = 0; lineIndex < outputLines.Length; lineIndex++)
                {
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
                    result[pair.Key] = ReadEmbeddedKeywordTagsDirect(pair.Key);
                }
            }
            finally
            {
                if (File.Exists(argFile)) File.Delete(argFile);
            }
            return result;
        }

        public Dictionary<string, EmbeddedMetadataSnapshot> ReadEmbeddedMetadataBatch(IEnumerable<string> files)
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
                    "-QuickTime:ModifyDate"
                };
                argLines.AddRange(orderedReadTargets.Select(pair => pair.Value));
                File.WriteAllLines(argFile, argLines.ToArray(), Encoding.UTF8);
                var output = RunExeCapture(ExifToolPath, new[] { "-@", argFile }, Path.GetDirectoryName(ExifToolPath), false);
                var matchedFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var outputLines = output.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
                for (int lineIndex = 0; lineIndex < outputLines.Length; lineIndex++)
                {
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

                    result[sourceFile] = snapshot;
                    matchedFiles.Add(sourceFile);
                }

                foreach (var pair in readTargets)
                {
                    if (matchedFiles.Contains(pair.Key)) continue;
                    result[pair.Key] = new EmbeddedMetadataSnapshot
                    {
                        Tags = ReadEmbeddedKeywordTagsDirect(pair.Key),
                        Comment = ReadEmbeddedCommentDirect(pair.Key),
                        CaptureTime = ReadEmbeddedCaptureDateDirect(pair.Key)
                    };
                }
            }
            finally
            {
                if (File.Exists(argFile)) File.Delete(argFile);
            }

            return result;
        }

        public void EnsureExifTool()
        {
            if (!File.Exists(ExifToolPath)) throw new InvalidOperationException("ExifTool not found: " + ExifToolPath);
            var support = Path.Combine(Path.GetDirectoryName(ExifToolPath), "exiftool_files");
            if (Path.GetFileName(ExifToolPath).Equals("exiftool.exe", StringComparison.OrdinalIgnoreCase) && !Directory.Exists(support)) throw new InvalidOperationException("ExifTool support folder missing: " + support);
            RunExe(ExifToolPath, new[] { "-ver" }, Path.GetDirectoryName(ExifToolPath), false);
        }

        public void RunExifToolBatch(IReadOnlyList<ExifWriteRequest> requests)
        {
            if (requests == null || requests.Count == 0) return;

            var argFile = Path.Combine(dependencies.CacheRoot, "exiftool-batch-write-" + Guid.NewGuid().ToString("N") + ".args");
            try
            {
                var argLines = new List<string> { "-stay_open", "True" };
                foreach (var request in requests.Where(entry => entry != null && entry.Arguments != null && entry.Arguments.Length > 0))
                {
                    argLines.AddRange(request.Arguments);
                    argLines.Add("-execute");
                }
                argLines.Add("-stay_open");
                argLines.Add("False");
                File.WriteAllLines(argFile, argLines.ToArray(), Encoding.UTF8);
                RunExe(ExifToolPath, new[] { "-@", argFile }, Path.GetDirectoryName(ExifToolPath), false);
            }
            finally
            {
                if (File.Exists(argFile)) File.Delete(argFile);
            }
        }

        public int RunExifWriteRequests(List<ExifWriteRequest> requests, int totalCount, int alreadyCompleted, Action<int, int, string> progress = null)
        {
            var workItems = requests ?? new List<ExifWriteRequest>();
            if (workItems.Count == 0) return 0;

            var completed = alreadyCompleted;
            var failures = new ConcurrentQueue<Exception>();
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
                RunExe(ExifToolPath, request.Arguments, Path.GetDirectoryName(ExifToolPath), false);
                finalizeRequest(request);
            };

            Parallel.ForEach(batches, new ParallelOptions { MaxDegreeOfParallelism = workerCount }, delegate(ExifWriteRequest[] batch)
            {
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
                            failures.Enqueue(new InvalidOperationException("Metadata update failed for " + request.FileName + ". " + itemEx.Message, itemEx));
                        }
                    }
                }
            });

            if (!failures.IsEmpty) throw new AggregateException(failures);
            return workItems.Count;
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
