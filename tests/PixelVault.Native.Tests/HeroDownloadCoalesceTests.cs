using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class HeroDownloadCoalesceTests
{
    [Fact]
    public async Task RunAsync_ParallelWaiters_ShareOneInnerCall()
    {
        var gate = new object();
        var inflight = new Dictionary<string, Task<string>>(StringComparer.Ordinal);
        var starts = 0;

        Func<CancellationToken, Task<string>> Inner = async _ =>
        {
            Interlocked.Increment(ref starts);
            await Task.Delay(80);
            return "ok";
        };

        var t1 = HeroDownloadCoalesce.RunAsync(gate, inflight, "k", Inner, CancellationToken.None);
        await Task.Delay(5);
        var t2 = HeroDownloadCoalesce.RunAsync(gate, inflight, "k", Inner, CancellationToken.None);
        var r1 = await t1;
        var r2 = await t2;

        Assert.Equal("ok", r1);
        Assert.Equal("ok", r2);
        Assert.Equal(1, starts);
    }

    [Fact]
    public async Task RunAsync_WaitCanceled_DoesNotForceInnerToThrow()
    {
        var gate = new object();
        var inflight = new Dictionary<string, Task<string>>(StringComparer.Ordinal);
        var innerDone = new TaskCompletionSource<bool>();

        Func<CancellationToken, Task<string>> Inner = async _ =>
        {
            await Task.Delay(200);
            innerDone.TrySetResult(true);
            return "done";
        };

        using var cts = new CancellationTokenSource(25);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            HeroDownloadCoalesce.RunAsync(gate, inflight, "x", Inner, cts.Token));

        Assert.True(await innerDone.Task.WaitAsync(TimeSpan.FromSeconds(5)));
    }
}
