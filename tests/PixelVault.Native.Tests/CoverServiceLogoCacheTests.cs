using System;
using System.IO;
using System.Linq;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class CoverServiceLogoCacheTests
{
    static CoverService CreateService(string coversRoot)
    {
        return new CoverService(new CoverServiceDependencies
        {
            AppVersion = "test",
            CoversRoot = coversRoot,
            RequestTimeoutMilliseconds = 1000,
            GetSteamGridDbApiToken = () => string.Empty,
            NormalizeTitle = value => (value ?? string.Empty).Trim(),
            NormalizeConsoleLabel = value => (value ?? string.Empty).Trim(),
            SafeCacheName = value =>
            {
                var chars = (value ?? string.Empty)
                    .Trim()
                    .ToLowerInvariant()
                    .Where(char.IsLetterOrDigit)
                    .ToArray();
                return chars.Length == 0 ? "item" : new string(chars);
            },
            StripTags = value => value ?? string.Empty,
            Sanitize = value => value ?? string.Empty,
            Log = _ => { },
            LogPerformanceSample = (_, _, _, _) => { },
            ClearImageCache = () => { }
        });
    }

    [Fact]
    public void CachedLogoPath_AndPurgeCachedLogoDownloads_Work()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "pv-logo-cache-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var cachedLogoPath = Path.Combine(tempDir, "logo-hades.png");
            File.WriteAllText(cachedLogoPath, "logo");
            var service = CreateService(tempDir);

            Assert.Equal(cachedLogoPath, service.CachedLogoPath("Hades"));

            service.PurgeCachedLogoDownloads("Hades");

            Assert.Null(service.CachedLogoPath("Hades"));
            Assert.False(File.Exists(cachedLogoPath));
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true);
        }
    }
}
