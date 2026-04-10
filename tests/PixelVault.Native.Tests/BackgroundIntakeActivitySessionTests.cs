using System.Linq;
using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class BackgroundIntakeActivitySessionTests
{
    [Fact]
    public void AddBatch_KeepsAtMostTenBatches()
    {
        var session = new BackgroundIntakeActivitySession();
        for (var i = 0; i < 12; i++)
        {
            var b = new BackgroundIntakeActivityBatch { CompletedUtc = DateTime.UtcNow.AddMinutes(-i) };
            b.Rows.Add(new BackgroundIntakeActivityRow { FileLabel = "f" + i + ".png" });
            session.AddBatch(b);
        }
        Assert.Equal(10, session.GetBatchesSnapshot().Count);
    }

    [Fact]
    public void AddBatch_TrimRowsTotal_CapsAtTwoHundred()
    {
        var session = new BackgroundIntakeActivitySession();
        for (var b = 0; b < 5; b++)
        {
            var batch = new BackgroundIntakeActivityBatch { CompletedUtc = DateTime.UtcNow.AddMinutes(-b) };
            for (var r = 0; r < 50; r++)
                batch.Rows.Add(new BackgroundIntakeActivityRow { FileLabel = "b" + b + "r" + r + ".png" });
            session.AddBatch(batch);
        }
        var snap = session.GetBatchesSnapshot();
        var total = snap.Sum(x => x.Rows.Count);
        Assert.True(total <= 200, "expected row cap 200, got " + total);
    }
}
