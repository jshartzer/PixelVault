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

    [Fact]
    public void Build_TitlelessSwitchCaptureSuffixWithFolderTitle_IsReviewItemNotManual()
    {
        var gameFolder = Path.Combine(_root, "Mario Kart World");
        Directory.CreateDirectory(gameFolder);
        var switchFile = Path.Combine(gameFolder, "2018032120305000_c.jpg");
        File.WriteAllBytes(switchFile, new byte[] { 0 });
        var parser = new FilenameParserService(new FilenameParserServiceDependencies
        {
            LoadCustomConventions = _ => new List<FilenameConventionRule>(),
            LoadSavedGameIndexRows = _ => new List<GameIndexEditorRow>(),
            NormalizeGameIndexName = value => (value ?? string.Empty).Trim(),
            ParseTagText = value => (value ?? string.Empty).Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries),
            IsVideo = _ => false,
            NormalizeConsoleLabel = MainWindow.NormalizeConsoleLabel
        });
        var analyzer = new IntakeAnalysisService(
            file => parser.Parse(file, string.Empty),
            _ => false,
            _ => DateTime.MinValue,
            null,
            file => string.Equals(file, switchFile, StringComparison.OrdinalIgnoreCase) ? "Mario Kart World" : string.Empty);

        var result = IntakePreparationBuilder.Build(
            new[] { switchFile },
            (files, cancellationToken) => analyzer.AnalyzeFiles(files, cancellationToken),
            includeImportEditRows: false);

        var item = Assert.Single(result.ReviewItems);
        Assert.Equal(switchFile, item.FilePath);
        Assert.Equal("Switch", item.PlatformLabel);
        Assert.True(item.TagSwitch);
        Assert.Empty(result.ManualItems);
        Assert.DoesNotContain(switchFile, result.ManualPaths);
    }

    [Fact]
    public void Build_SteamClip_GoesToManualWhileIdentifiedSteamVideoStaysAutomatic()
    {
        var clipFile = CreateFile("clip_1700000000000.mp4");
        var identifiedFile = CreateFile("620_20261012123456_1.mp4");
        var parser = new FilenameParserService(new FilenameParserServiceDependencies
        {
            LoadCustomConventions = _ => new List<FilenameConventionRule>(),
            LoadSavedGameIndexRows = _ => new List<GameIndexEditorRow>
            {
                new() { Name = "Clip", PlatformLabel = "Steam", SteamAppId = "123" }
            },
            ParseTagText = value => (value ?? string.Empty).Split(';', StringSplitOptions.RemoveEmptyEntries),
            NormalizeConsoleLabel = MainWindow.NormalizeConsoleLabel
        });
        var analyzer = new IntakeAnalysisService(
            file => parser.Parse(file, "library-root"),
            _ => true,
            _ => DateTime.MinValue,
            getImportFolderTitleHint: _ => "Portal 2");

        var result = IntakePreparationBuilder.Build(
            new[] { clipFile, identifiedFile },
            analyzer.AnalyzeFiles,
            includeImportEditRows: true);

        var manualItem = Assert.Single(result.ManualItems);
        Assert.Equal(clipFile, manualItem.FilePath);
        Assert.Empty(manualItem.GameName);
        Assert.Empty(manualItem.SteamAppId);
        Assert.Equal("Steam", result.Analysis[clipFile].Parsed.PlatformLabel);
        Assert.False(manualItem.TagSteam); // Manual platform assignment waits for a Steam AppID.
        Assert.Equal(DateTimeOffset.FromUnixTimeMilliseconds(1700000000000).ToLocalTime().DateTime, manualItem.CaptureTime);
        Assert.Contains(clipFile, result.ManualPaths);
        Assert.DoesNotContain(clipFile, result.RecognizedPaths);
        Assert.Equal(identifiedFile, Assert.Single(result.ReviewItems).FilePath);
        Assert.False(result.ImportEditItems.Single(item => item.FilePath == clipFile).IntakeRuleMatched);
        Assert.True(result.ImportEditItems.Single(item => item.FilePath == identifiedFile).IntakeRuleMatched);
        var rules = parser.GetConventionRules("library-root");
        var clipAnalysis = result.Analysis[clipFile];
        var identifiedAnalysis = result.Analysis[identifiedFile];
        Assert.True(clipAnalysis.PreserveFileTimes);
        Assert.False(AutoIntakePolicy.IsEligibleForBackgroundAutoImport(clipAnalysis, AutoIntakePolicy.TryResolveMatchedRule(rules, clipAnalysis.Parsed)));
        Assert.True(AutoIntakePolicy.IsEligibleForBackgroundAutoImport(identifiedAnalysis, AutoIntakePolicy.TryResolveMatchedRule(rules, identifiedAnalysis.Parsed)));
    }

    string CreateFile(string name)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllBytes(path, new byte[] { 0 });
        return path;
    }
}
