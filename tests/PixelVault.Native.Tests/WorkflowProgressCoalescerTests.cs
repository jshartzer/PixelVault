using PixelVaultNative;
using Xunit;

namespace PixelVaultNative.Tests;

public sealed class WorkflowProgressCoalescerTests
{
    [Fact]
    public void Report_AppliesFirstProgressImmediately()
    {
        var scheduler = new FakeProgressScheduler();
        var applied = new List<(int completed, string detail)>();
        using var coalescer = new WorkflowProgressCoalescer(
            scheduler,
            (completed, detail) => applied.Add((completed, detail)),
            TimeSpan.FromMilliseconds(100));

        coalescer.Report(1, "first");
        scheduler.DrainPosted();

        var item = Assert.Single(applied);
        Assert.Equal(1, item.completed);
        Assert.Equal("first", item.detail);
    }

    [Fact]
    public void Report_CoalescesRapidUpdatesAndKeepsMostRecentDetail()
    {
        var scheduler = new FakeProgressScheduler();
        var applied = new List<(int completed, string detail)>();
        using var coalescer = new WorkflowProgressCoalescer(
            scheduler,
            (completed, detail) => applied.Add((completed, detail)),
            TimeSpan.FromMilliseconds(100));

        coalescer.Report(1, "first");
        scheduler.DrainPosted();

        coalescer.Report(2, "second");
        coalescer.Report(3, "third");
        scheduler.DrainPosted();

        Assert.Single(applied);

        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(99));
        scheduler.DrainPosted();
        Assert.Single(applied);

        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(1));
        scheduler.DrainPosted();

        Assert.Equal(2, applied.Count);
        Assert.Equal(3, applied[1].completed);
        Assert.Equal("third", applied[1].detail);
    }

    [Fact]
    public void FlushImmediate_AppliesLatestPendingProgressWithoutWaitingForThrottle()
    {
        var scheduler = new FakeProgressScheduler();
        var applied = new List<(int completed, string detail)>();
        using var coalescer = new WorkflowProgressCoalescer(
            scheduler,
            (completed, detail) => applied.Add((completed, detail)),
            TimeSpan.FromMilliseconds(100));

        coalescer.Report(1, "first");
        scheduler.DrainPosted();

        coalescer.Report(2, "second");
        coalescer.Report(3, "final before success");
        coalescer.FlushImmediate();

        Assert.Equal(2, applied.Count);
        Assert.Equal(3, applied[1].completed);
        Assert.Equal("final before success", applied[1].detail);

        scheduler.AdvanceBy(TimeSpan.FromMilliseconds(100));
        scheduler.DrainPosted();
        Assert.Equal(2, applied.Count);
    }

    sealed class FakeProgressScheduler : IWorkflowProgressCoalescerScheduler
    {
        readonly Queue<Action> _posted = new();
        readonly List<(long dueMilliseconds, Action action)> _scheduled = new();

        public long TimestampMilliseconds { get; private set; }

        public void Post(Action action)
        {
            if (action != null) _posted.Enqueue(action);
        }

        public void Schedule(Action action, TimeSpan delay)
        {
            if (action == null) return;
            _scheduled.Add((TimestampMilliseconds + Math.Max(0, (long)delay.TotalMilliseconds), action));
        }

        public void AdvanceBy(TimeSpan duration)
        {
            TimestampMilliseconds += Math.Max(0, (long)duration.TotalMilliseconds);
            var due = _scheduled
                .Where(item => item.dueMilliseconds <= TimestampMilliseconds)
                .OrderBy(item => item.dueMilliseconds)
                .ToList();
            foreach (var item in due)
            {
                _scheduled.Remove(item);
                item.action();
            }
        }

        public void DrainPosted()
        {
            while (_posted.Count > 0)
            {
                _posted.Dequeue()();
            }
        }
    }
}
