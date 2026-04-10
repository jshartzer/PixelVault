using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class SourceFileStabilityProbeTests
{
    [Fact]
    public async Task WaitUntilStableAsync_UnchangedFile_BecomesStableWithinQuietWindow()
    {
        var path = Path.Combine(Path.GetTempPath(), "pv-stable-" + Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            await File.WriteAllBytesAsync(path, new byte[] { 1, 2, 3 });
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var ok = await SourceFileStabilityProbe.WaitUntilStableAsync(
                path,
                quietMilliseconds: 80,
                maxWait: TimeSpan.FromSeconds(5),
                cts.Token,
                pollMilliseconds: 40);
            Assert.True(ok);
        }
        finally
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public async Task WaitUntilStableAsync_MissingPath_ReturnsFalse()
    {
        var path = Path.Combine(Path.GetTempPath(), "pv-missing-" + Guid.NewGuid().ToString("N") + ".bin");
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var ok = await SourceFileStabilityProbe.WaitUntilStableAsync(
            path,
            quietMilliseconds: 50,
            maxWait: TimeSpan.FromSeconds(2),
            cts.Token);
        Assert.False(ok);
    }
}
