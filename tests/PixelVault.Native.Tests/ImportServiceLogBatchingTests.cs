using PixelVaultNative;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class ImportServiceLogBatchingTests
{
    [Fact]
    public void MoveFilesToLibraryDestination_BatchesPerFileLogLinesAndKeepsSummaryImmediate()
    {
        var root = Path.Combine(Path.GetTempPath(), "pv-import-log-batch-move-" + Guid.NewGuid().ToString("n"));
        var source = Path.Combine(root, "source");
        var destination = Path.Combine(root, "destination");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        var files = Enumerable.Range(1, 3)
            .Select(i => Path.Combine(source, "clip" + i + ".png"))
            .ToList();
        foreach (var file in files) File.WriteAllText(file, "x");

        var log = new CapturingLogService();
        var service = CreateService(log, destination);

        try
        {
            var result = service.MoveFilesToLibraryDestination(files, "Move summary");

            Assert.Equal(3, result.Moved);
            Assert.Equal(1, log.BatchAppendCalls);
            Assert.Equal(1, log.SingleAppendCalls);
            Assert.Equal(3, log.BatchedMessages.Count);
            Assert.All(log.BatchedMessages, message => Assert.StartsWith("Moved: ", message));
            Assert.Single(log.SingleMessages, message => message.StartsWith("Move summary:", StringComparison.Ordinal));
        }
        finally
        {
            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
            catch
            {
                /* best-effort */
            }
        }
    }

    [Fact]
    public void MoveFilesToLibraryDestination_FlushesLargePerFileLogBatchesInChunks()
    {
        var root = Path.Combine(Path.GetTempPath(), "pv-import-log-batch-large-move-" + Guid.NewGuid().ToString("n"));
        var source = Path.Combine(root, "source");
        var destination = Path.Combine(root, "destination");
        Directory.CreateDirectory(source);
        Directory.CreateDirectory(destination);
        var files = Enumerable.Range(1, 101)
            .Select(i => Path.Combine(source, "clip" + i.ToString("000") + ".png"))
            .ToList();
        foreach (var file in files) File.WriteAllText(file, "x");

        var log = new CapturingLogService();
        var service = CreateService(log, destination);

        try
        {
            var result = service.MoveFilesToLibraryDestination(files, "Move summary");

            Assert.Equal(101, result.Moved);
            Assert.Equal(2, log.BatchAppendCalls);
            Assert.Equal(1, log.SingleAppendCalls);
            Assert.Equal(101, log.BatchedMessages.Count);
            Assert.Single(log.BatchSizes, size => size == 100);
            Assert.Single(log.BatchSizes, size => size == 1);
            Assert.Single(log.SingleMessages, message => message.StartsWith("Move summary:", StringComparison.Ordinal));
        }
        finally
        {
            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
            catch
            {
                /* best-effort */
            }
        }
    }

    [Fact]
    public void WriteMetadataForReviewItems_BatchesPerFilePreparationLogsAndKeepsSummaryImmediate()
    {
        var root = Path.Combine(Path.GetTempPath(), "pv-import-log-batch-metadata-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(root);
        var files = Enumerable.Range(1, 2)
            .Select(i => Path.Combine(root, "clip" + i + ".png"))
            .ToList();
        foreach (var file in files) File.WriteAllText(file, "x");

        var log = new CapturingLogService();
        var service = CreateService(log, root);
        var items = files.Select(file => new ReviewItem
        {
            FilePath = file,
            FileName = Path.GetFileName(file),
            CaptureTime = new DateTime(2026, 5, 5, 12, 0, 0, DateTimeKind.Local),
            TagSteam = true
        }).ToList();

        try
        {
            var result = service.WriteMetadataForReviewItems(items);

            Assert.Equal(2, result.Updated);
            Assert.Equal(1, log.BatchAppendCalls);
            Assert.Equal(1, log.SingleAppendCalls);
            Assert.Equal(2, log.BatchedMessages.Count);
            Assert.All(log.BatchedMessages, message => Assert.StartsWith("Updating metadata: ", message));
            Assert.Single(log.SingleMessages, message => message.StartsWith("Metadata summary:", StringComparison.Ordinal));
        }
        finally
        {
            try
            {
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
            catch
            {
                /* best-effort */
            }
        }
    }

    static ImportService CreateService(CapturingLogService log, string destinationRoot)
    {
        return new ImportService(new ImportServiceDependencies
        {
            FileSystem = new FileSystemService(),
            LogService = log,
            MetadataService = new StubMetadataService(),
            GetFileCreationTime = _ => DateTime.MinValue,
            GetFileLastWriteTime = _ => DateTime.MinValue,
            CoverService = new StubCoverService(),
            GetDestinationRoot = () => destinationRoot,
            GetLibraryRoot = () => destinationRoot,
            GetConflictMode = () => "Rename",
            UniquePath = path => Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, Path.GetFileNameWithoutExtension(path) + " (1)" + Path.GetExtension(path)),
            MoveMetadataSidecarIfPresent = (_, _) => { },
            NormalizeGameIndexName = value => (value ?? string.Empty).Trim()
        });
    }

    sealed class CapturingLogService : ILogService
    {
        public readonly List<string> SingleMessages = new();
        public readonly List<string> BatchedMessages = new();
        public readonly List<int> BatchSizes = new();
        public int SingleAppendCalls { get; private set; }
        public int BatchAppendCalls { get; private set; }

        public string AppendMainLine(string? message)
        {
            SingleAppendCalls++;
            var line = message ?? string.Empty;
            SingleMessages.Add(line);
            return line;
        }

        public string[] AppendMainLines(IEnumerable<string?> messages)
        {
            BatchAppendCalls++;
            var lines = (messages ?? Array.Empty<string?>())
                .Select(message => message ?? string.Empty)
                .ToArray();
            BatchSizes.Add(lines.Length);
            BatchedMessages.AddRange(lines);
            return lines;
        }
    }
}
