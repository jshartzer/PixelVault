using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class IntakePreparationBuilderTests : IDisposable
{
    readonly string _root;

    public IntakePreparationBuilderTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "pv_intake_prep_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }
        catch
        {
        }
    }

    [Fact]
    public void Build_ReusesOneAnalysisPass_ForReviewManualAndImportEditRows()
    {
        var reviewFile = CreateFile("Xbox Game-2026_05_03-12_00_00.png");
        var manualFile = CreateFile("Mystery Capture.png");
        var analysis = new Dictionary<string, IntakePreviewFileAnalysis>(StringComparer.OrdinalIgnoreCase)
        {
            [reviewFile] = new IntakePreviewFileAnalysis
            {
                FilePath = reviewFile,
                FileName = Path.GetFileName(reviewFile),
                CanUpdateMetadata = true,
                CaptureTime = new DateTime(2026, 5, 3, 12, 0, 0),
                Parsed = new FilenameParseResult
                {
                    PlatformLabel = "Xbox",
                    PlatformTags = new[] { "Xbox" },
                    CaptureTime = new DateTime(2026, 5, 3, 12, 0, 0),
                    MatchedConvention = true
                }
            },
            [manualFile] = new IntakePreviewFileAnalysis
            {
                FilePath = manualFile,
                FileName = Path.GetFileName(manualFile),
                CanUpdateMetadata = false,
                CaptureTime = new DateTime(2026, 5, 3, 12, 5, 0),
                Parsed = new FilenameParseResult
                {
                    PlatformLabel = "PC",
                    PlatformTags = new[] { "PC" },
                    GameTitleHint = "Mystery Capture",
                    MatchedConvention = false
                }
            }
        };
        var analyzeCalls = 0;
        var analyzedFiles = new List<string>();

        var result = IntakePreparationBuilder.Build(
            new[] { reviewFile, manualFile, reviewFile },
            delegate(IEnumerable<string> files, CancellationToken _)
            {
                analyzeCalls++;
                analyzedFiles = files.ToList();
                return analysis;
            },
            includeImportEditRows: true);

        Assert.Equal(1, analyzeCalls);
        Assert.Equal(new[] { reviewFile, manualFile }, analyzedFiles);
        Assert.Same(analysis, result.Analysis);
        Assert.Single(result.ReviewItems);
        Assert.Equal(reviewFile, result.ReviewItems[0].FilePath);
        Assert.Contains(reviewFile, result.RecognizedPaths);
        Assert.Single(result.ManualItems);
        Assert.Equal(manualFile, result.ManualItems[0].FilePath);
        Assert.Contains(manualFile, result.ManualPaths);
        Assert.Equal(2, result.ImportEditItems.Count);
        Assert.True(result.ImportEditItems.Single(item => item.FilePath == reviewFile).IntakeRuleMatched);
        Assert.False(result.ImportEditItems.Single(item => item.FilePath == manualFile).IntakeRuleMatched);
    }

    string CreateFile(string name)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllBytes(path, new byte[] { 0 });
        return path;
    }
}
