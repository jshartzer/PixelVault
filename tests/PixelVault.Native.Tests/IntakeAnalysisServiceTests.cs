using System;
using System.IO;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class IntakeAnalysisServiceTests
{
    [Fact]
    public void AnalyzeFiles_XboxTagAndCaptureTime_CanUpdateMetadata()
    {
        var temp = Path.Combine(Path.GetTempPath(), "pv_intake_test_" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            File.WriteAllBytes(temp, new byte[] { 0 });
            var svc = new IntakeAnalysisService(
                _ => new FilenameParseResult
                {
                    MatchedConvention = true,
                    PlatformTags = new[] { "Xbox" },
                    CaptureTime = new DateTime(2024, 1, 1, 12, 0, 0)
                },
                _ => false,
                _ => DateTime.MinValue);

            var map = svc.AnalyzeFiles(new[] { temp });

            var a = Assert.Single(map.Values);
            Assert.True(a.CanUpdateMetadata);
            Assert.Equal(Path.GetFileName(temp), a.FileName);
        }
        finally
        {
            try
            {
                File.Delete(temp);
            }
            catch
            {
                // ignore
            }
        }
    }

    [Fact]
    public void AnalyzeFiles_MissingSteamAppIdWhenRequired_CannotUpdateMetadata()
    {
        var temp = Path.Combine(Path.GetTempPath(), "pv_intake_test_" + Guid.NewGuid().ToString("N") + ".png");
        try
        {
            File.WriteAllBytes(temp, new byte[] { 0 });
            var svc = new IntakeAnalysisService(
                _ => new FilenameParseResult
                {
                    MatchedConvention = true,
                    PlatformTags = new[] { "Steam" },
                    CaptureTime = new DateTime(2024, 1, 1),
                    RoutesToManualWhenMissingSteamAppId = true,
                    SteamAppId = string.Empty
                },
                _ => false,
                _ => DateTime.MinValue);

            var map = svc.AnalyzeFiles(new[] { temp });

            var a = Assert.Single(map.Values);
            Assert.False(a.CanUpdateMetadata);
        }
        finally
        {
            try
            {
                File.Delete(temp);
            }
            catch
            {
                // ignore
            }
        }
    }

    [Fact]
    public void FilenameAutoIntakeModes_Normalize_UnknownDefaultsToManualOnly()
    {
        Assert.Equal(FilenameAutoIntakeModes.ManualOnly, FilenameAutoIntakeModes.Normalize(null));
        Assert.Equal(FilenameAutoIntakeModes.ManualOnly, FilenameAutoIntakeModes.Normalize(""));
        Assert.Equal(FilenameAutoIntakeModes.ManualOnly, FilenameAutoIntakeModes.Normalize("bogus"));
        Assert.Equal(FilenameAutoIntakeModes.TrustedExactMatch, FilenameAutoIntakeModes.Normalize("TrustedExactMatch"));
        Assert.Equal(FilenameAutoIntakeModes.TrustedExactMatch, FilenameAutoIntakeModes.Normalize("trustedexactmatch"));
    }
}
