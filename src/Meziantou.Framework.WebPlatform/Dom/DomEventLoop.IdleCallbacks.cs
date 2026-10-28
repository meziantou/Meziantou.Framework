namespace Meziantou.Framework.WebPlatform.Dom;

// Cooperative Scheduling of Background Tasks (https://w3c.github.io/requestidlecallback/): the idle periods of a window
// event loop, which run the idle callbacks of its windows (see Window.IdleCallbacks.cs).
public sealed partial class DomEventLoop
{
    /// <summary>The maximum duration of an idle period (https://w3c.github.io/requestidlecallback/#start-an-idle-period-algorithm).</summary>
    private static readonly TimeSpan MaximumIdlePeriod = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// The minimum duration of an idle period: the event loop is not idle in a shorter gap between two tasks (e.g. the
    /// 4 ms of the nested timers of a busy loop).
    /// </summary>
    private static readonly TimeSpan MinimumIdlePeriod = TimeSpan.FromMilliseconds(5);

    /// <summary>The windows of the event loop that have idle callbacks.</summary>
    private readonly List<Window> _windowsWithIdleCallbacks = [];

    /// <summary>The tasks of the idle-task task source: they run when no other task is queued.</summary>
    private readonly Queue<Action> _idleTasks = new();

    /// <summary>Whether an idle period started and no other task ran since then.</summary>
    private bool _isIdlePeriod;

    /// <summary>The deadline of the current idle period (see <see cref="_isIdlePeriod"/>).</summary>
    private Func<long>? _idlePeriodDeadline;

    /// <summary>The time of the timer queued to start the next idle period, or null.</summary>
    private long? _idlePeriodWakeUp;

    /// <summary>
    /// Registers a window that requested an idle callback. A callback requested during an idle period runs in the next
    /// one: a timer wakes the event loop at the end of the current idle period.
    /// </summary>
    internal void OnIdleCallbackRequested(Window window)
    {
        if (!_windowsWithIdleCallbacks.Contains(window))
        {
            _windowsWithIdleCallbacks.Add(window);
        }

        if (_isIdlePeriod && _idlePeriodDeadline is { } getDeadline)
        {
            QueueIdlePeriodWakeUp(getDeadline());
        }
    }

    /// <summary>Queues a task of the idle-task task source.</summary>
    internal void QueueIdleTask(Action task) => _idleTasks.Enqueue(task);

    /// <summary>Marks the end of the idle period: a task that is not an idle task runs.</summary>
    private void EndIdlePeriod() => _isIdlePeriod = false;

    /// <summary>
    /// Runs the next idle task when no other task is queued, starting an idle period when there is none
    /// (https://html.spec.whatwg.org/multipage/webappapis.html#event-loop-processing-model, "start an idle period"). Returns
    /// false when there is nothing to run.
    /// </summary>
    private bool RunIdleTask()
    {
        if (_idleTasks.Count == 0 && !TryStartIdlePeriod())
            return false;

        var task = _idleTasks.Dequeue();
        var continuationState = CurrentContinuationState;
        CurrentContinuationState = null;
        try
        {
            task();
            PerformMicrotaskCheckpoint();
        }
        finally
        {
            CurrentContinuationState = continuationState;
        }

        return true;
    }

    /// <summary>
    /// https://w3c.github.io/requestidlecallback/#start-an-idle-period-algorithm for the fully active windows of the event
    /// loop. The deadline is the earliest of 50 milliseconds after the start of the idle period, the next timer, and the next
    /// animation frame (a timer of the event loop). It is computed again each time it is read (e.g. by
    /// <see cref="IdleDeadline.TimeRemaining"/>): a timer queued during the idle period ends it sooner.
    /// </summary>
    private bool TryStartIdlePeriod()
    {
        if (_windowsWithIdleCallbacks.Count == 0 || IsDisposed)
            return false;

        _windowsWithIdleCallbacks.RemoveAll(static window => !window.HasIdleCallbacks);
        if (!_windowsWithIdleCallbacks.Exists(static window => window.CanRunScript))
            return false;

        var now = TimeProvider.GetTimestamp();

        // The callbacks requested during an idle period run in the next one
        if (_isIdlePeriod && _idlePeriodDeadline is { } currentDeadline)
        {
            var end = currentDeadline();
            if (now < end)
            {
                QueueIdlePeriodWakeUp(end);
                return false;
            }
        }

        var start = now;
        long GetDeadline() => GetIdlePeriodDeadline(start);
        if (GetDeadline() - now < (long)(MinimumIdlePeriod.TotalSeconds * TimeProvider.TimestampFrequency))
        {
            // The next timer wakes the event loop
            _isIdlePeriod = false;
            return false;
        }

        _isIdlePeriod = true;
        _idlePeriodDeadline = GetDeadline;
        var started = false;
        foreach (var window in _windowsWithIdleCallbacks.ToArray())
        {
            if (window.CanRunScript && window.StartIdlePeriod(GetDeadline))
            {
                started = true;
            }
        }

        return started && _idleTasks.Count > 0;
    }

    /// <summary>The deadline of an idle period that started at <paramref name="start"/>: 50 milliseconds later, or the next timer of the event loop (a timer, an animation frame...) when it is sooner.</summary>
    private long GetIdlePeriodDeadline(long start)
    {
        var deadline = start + (long)(MaximumIdlePeriod.TotalSeconds * TimeProvider.TimestampFrequency);
        lock (_timersLock)
        {
            if (_timers.TryPeek(out _, out var next) && next.Due < deadline)
            {
                deadline = next.Due;
            }
        }

        return deadline;
    }

    /// <summary>Queues a timer that wakes the event loop at <paramref name="due"/> to start the next idle period.</summary>
    private void QueueIdlePeriodWakeUp(long due)
    {
        if (_idlePeriodWakeUp == due)
            return;

        _idlePeriodWakeUp = due;
        var delay = TimeSpan.FromSeconds((double)(due - TimeProvider.GetTimestamp()) / TimeProvider.TimestampFrequency);
        QueueTimer(delay, () =>
        {
            if (_idlePeriodWakeUp == due)
            {
                _idlePeriodWakeUp = null;
            }
        });
    }

    private void ClearIdleCallbacks()
    {
        _windowsWithIdleCallbacks.Clear();
        _idleTasks.Clear();
        _isIdlePeriod = false;
        _idlePeriodDeadline = null;
    }
}
