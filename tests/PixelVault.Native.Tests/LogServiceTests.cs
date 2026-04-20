using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class LogServiceTests
{
    [Fact]
    public void NullLogService_AppendMainLine_Returns_Empty_Without_Throwing()
    {
        var log = NullLogService.Instance;
        Assert.Same(log, NullLogService.Instance);
        Assert.Equal(string.Empty, log.AppendMainLine("any"));
        Assert.Equal(string.Empty, log.AppendMainLine(null));
    }
}
