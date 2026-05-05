using System;
using System.Threading;

namespace PixelVaultNative
{
    internal interface IWorkflowProgressCoalescerScheduler
    {
        long TimestampMilliseconds { get; }
        void Post(Action action);
        void Schedule(Action action, TimeSpan delay);
    }

    internal sealed class WorkflowProgressCoalescer : IDisposable
    {
        public static readonly TimeSpan DefaultMinimumInterval = TimeSpan.FromMilliseconds(100);

        readonly object _sync = new object();
        readonly IWorkflowProgressCoalescerScheduler _scheduler;
        readonly Action<int, string> _applyProgress;
        readonly long _minimumIntervalMilliseconds;

        bool _disposed;
        bool _hasPending;
        bool _applyQueued;
        bool _timerQueued;
        int _pendingCompleted;
        string _pendingDetail = string.Empty;
        long _lastAppliedMilliseconds = -1;
        long _timerVersion;

        public WorkflowProgressCoalescer(Action<Action> post, Action<int, string> applyProgress, TimeSpan minimumInterval)
            : this(new TimerWorkflowProgressCoalescerScheduler(post), applyProgress, minimumInterval)
        {
        }

        internal WorkflowProgressCoalescer(
            IWorkflowProgressCoalescerScheduler scheduler,
            Action<int, string> applyProgress,
            TimeSpan minimumInterval)
        {
            _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
            _applyProgress = applyProgress ?? throw new ArgumentNullException(nameof(applyProgress));
            _minimumIntervalMilliseconds = Math.Max(0, (long)minimumInterval.TotalMilliseconds);
        }

        public void Report(int completed, string detail)
        {
            bool postNow = false;
            bool schedule = false;
            TimeSpan scheduleDelay = TimeSpan.Zero;
            long scheduleVersion = 0;

            lock (_sync)
            {
                if (_disposed) return;

                _pendingCompleted = completed;
                _pendingDetail = detail ?? string.Empty;
                _hasPending = true;

                if (!_applyQueued && !_timerQueued)
                {
                    var delayMilliseconds = DelayUntilNextApplyMilliseconds(_scheduler.TimestampMilliseconds);
                    if (delayMilliseconds <= 0)
                    {
                        _applyQueued = true;
                        postNow = true;
                    }
                    else
                    {
                        _timerQueued = true;
                        scheduleVersion = ++_timerVersion;
                        scheduleDelay = TimeSpan.FromMilliseconds(delayMilliseconds);
                        schedule = true;
                    }
                }
            }

            if (postNow) _scheduler.Post(ApplyPending);
            if (schedule) _scheduler.Schedule(() => TimerElapsed(scheduleVersion), scheduleDelay);
        }

        public void FlushImmediate()
        {
            int completed;
            string detail;

            lock (_sync)
            {
                if (_disposed || !_hasPending) return;

                completed = _pendingCompleted;
                detail = _pendingDetail;
                _hasPending = false;
                _applyQueued = false;
                _timerQueued = false;
                _timerVersion++;
                _lastAppliedMilliseconds = _scheduler.TimestampMilliseconds;
            }

            _applyProgress(completed, detail);
        }

        public void Dispose()
        {
            lock (_sync)
            {
                _disposed = true;
                _hasPending = false;
                _applyQueued = false;
                _timerQueued = false;
                _timerVersion++;
            }
        }

        long DelayUntilNextApplyMilliseconds(long nowMilliseconds)
        {
            if (_lastAppliedMilliseconds < 0) return 0;
            var elapsedMilliseconds = Math.Max(0, nowMilliseconds - _lastAppliedMilliseconds);
            return Math.Max(0, _minimumIntervalMilliseconds - elapsedMilliseconds);
        }

        void TimerElapsed(long version)
        {
            var postNow = false;
            lock (_sync)
            {
                if (_disposed || version != _timerVersion) return;
                _timerQueued = false;
                if (!_hasPending || _applyQueued) return;

                _applyQueued = true;
                postNow = true;
            }

            if (postNow) _scheduler.Post(ApplyPending);
        }

        void ApplyPending()
        {
            int completed;
            string detail;

            lock (_sync)
            {
                _applyQueued = false;
                if (_disposed || !_hasPending) return;

                completed = _pendingCompleted;
                detail = _pendingDetail;
                _hasPending = false;
                _lastAppliedMilliseconds = _scheduler.TimestampMilliseconds;
            }

            _applyProgress(completed, detail);
        }

        sealed class TimerWorkflowProgressCoalescerScheduler : IWorkflowProgressCoalescerScheduler
        {
            readonly Action<Action> _post;

            public TimerWorkflowProgressCoalescerScheduler(Action<Action> post)
            {
                _post = post ?? throw new ArgumentNullException(nameof(post));
            }

            public long TimestampMilliseconds => Environment.TickCount64;

            public void Post(Action action)
            {
                if (action == null) return;
                _post(action);
            }

            public void Schedule(Action action, TimeSpan delay)
            {
                if (action == null) return;

                Timer timer = null;
                timer = new Timer(_ =>
                {
                    try
                    {
                        _post(action);
                    }
                    finally
                    {
                        timer?.Dispose();
                    }
                }, null, delay < TimeSpan.Zero ? TimeSpan.Zero : delay, Timeout.InfiniteTimeSpan);
            }
        }
    }
}
