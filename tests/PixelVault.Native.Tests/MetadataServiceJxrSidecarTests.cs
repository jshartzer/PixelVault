using System;
using System.IO;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class MetadataServiceJxrSidecarTests
{
    [Fact]
    public void BuildStarRatingExifArgs_Targets_Companion_Xmp_When_MetadataSidecar_Is_Jxr()
    {
        var jxr = Path.Combine(Path.GetTempPath(), "pv-meta-jxr-" + Guid.NewGuid().ToString("N") + ".jxr");
        File.WriteAllBytes(jxr, new byte[] { 1 });
        var hostExe = Environment.ProcessPath;
        Assert.False(string.IsNullOrWhiteSpace(hostExe));
        Assert.True(File.Exists(hostExe));
        try
        {
            var svc = new MetadataService(new MetadataServiceDependencies
            {
                GetExifToolPath = () => hostExe,
                MetadataSidecarPath = file =>
                    string.Equals(Path.GetExtension(file), ".jxr", StringComparison.OrdinalIgnoreCase) ? file + ".xmp" : null,
                MetadataReadPath = file => file
            });
            var args = svc.BuildStarRatingExifArgs(jxr, starred: true);
            Assert.NotNull(args);
            Assert.NotEmpty(args);
            Assert.Contains("-overwrite_original", args);
            Assert.Equal(jxr + ".xmp", args[^1]);
        }
        finally
        {
            try
            {
                if (File.Exists(jxr)) File.Delete(jxr);
            }
            catch
            {
                // ignore
            }
        }
    }

    [Fact]
    public void BuildStarRatingExifArgs_Targets_Image_When_No_Sidecar()
    {
        var png = Path.Combine(Path.GetTempPath(), "pv-meta-png-" + Guid.NewGuid().ToString("N") + ".png");
        File.WriteAllBytes(png, new byte[] { 1 });
        var hostExe = Environment.ProcessPath;
        Assert.False(string.IsNullOrWhiteSpace(hostExe));
        try
        {
            var svc = new MetadataService(new MetadataServiceDependencies
            {
                GetExifToolPath = () => hostExe,
                MetadataSidecarPath = _ => null,
                MetadataReadPath = file => file
            });
            var args = svc.BuildStarRatingExifArgs(png, starred: false);
            Assert.NotNull(args);
            Assert.NotEmpty(args);
            Assert.Contains("-overwrite_original", args);
            Assert.Equal(png, args[^1]);
        }
        finally
        {
            try
            {
                if (File.Exists(png)) File.Delete(png);
            }
            catch
            {
                // ignore
            }
        }
    }
}
