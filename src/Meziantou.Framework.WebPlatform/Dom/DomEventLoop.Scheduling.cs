namespace Meziantou.Framework.WebPlatform.Dom;

// Prioritized Task Scheduling (https://wicg.github.io/scheduling-apis/#sec-patches-html): the scheduler task queues of the
// schedulers of the event loop, the selection of the next task, and the continuation state.
public sealed partial class DomEventLoop
{
    /// <summary>The effective priority of the user-visible tasks: they are scheduled as the tasks of the other task sources.</summary>
    private const int UserVisibleEffectivePriority = 2;

    /// <summary>
    /// The lowest effective priority that precedes the tasks of the other task sources: the user-visible continuations, and
    /// the user-blocking tasks and continuations.
    /// </summary>
    private const int ElevatedEffectivePriority = 3;

    /// <summary>
    /// The task of the task queue that stands for a task of a scheduler: when it is its turn, the oldest task of the
    /// scheduler task queue of the highest priority runs, unless it was queued after the task of the slot. The user-visible
    /// tasks of the schedulers are thus ordered with the tasks of the other task sources (e.g. the timers), and the pending
    /// tasks of the schedulers are pending work of the event loop.
    /// </summary>
    private static readonly Action SchedulerTaskSlot = static () => { };

    /// <summary>The scheduler task queues that have tasks (of the schedulers whose relevant agent's event loop is this event loop).</summary>
    private readonly List<SchedulerTaskQueue> _schedulerTaskQueues = [];

    /// <summary>The enqueue orders of the tasks of the <see cref="SchedulerTaskSlot"/> of the task queue, in the order of the slots.</summary>
    private readonly Queue<long> _schedulerTaskSlots = new();

    /// <summary>https://wicg.github.io/scheduling-apis/#event-loop-next-enqueue-order</summary>
    private long _nextSchedulerEnqueueOrder = 1;

    /// <summary>The last snapshot of <see cref="CurrentContinuationState"/>: the promise reactions created with the same state share it.</summary>
    private ContinuationStateSnapshot? _continuationStateSnapshot;

    /// <summary>
    /// https://wicg.github.io/scheduling-apis/#event-loop-current-continuation-state: the scheduling states that
    /// <see cref="Scheduler.Yield"/> inherits. The state of a task of a scheduler is set while the task and the microtasks of
    /// its microtask checkpoint run. The promise reactions (HostMakeJobCallback and HostCallJobCallback, see
    /// <see cref="CaptureContinuationState"/>) and the microtasks of <c>queueMicrotask()</c> restore the state they were
    /// created with.
    /// </summary>
    internal SchedulerContinuationState? CurrentContinuationState { get; set; }

    /// <summary>
    /// Returns the current continuation state, captured by a job callback (https://wicg.github.io/scheduling-apis/#sec-patches-html-hostmakejobcallback).
    /// A continuation state is immutable: the snapshot is its clone.
    /// </summary>
    internal ContinuationStateSnapshot CaptureContinuationState()
    {
        var state = CurrentContinuationState;
        var snapshot = _continuationStateSnapshot;
        if (snapshot is null || !ReferenceEquals(snapshot.State, state))
        {
            _continuationStateSnapshot = snapshot = new ContinuationStateSnapshot(this, state);
        }

        return snapshot;
    }

    internal long GetNextSchedulerEnqueueOrder() => _nextSchedulerEnqueueOrder++;

    /// <summary>https://wicg.github.io/scheduling-apis/#queue-a-scheduler-task</summary>
    internal void QueueSchedulerTask(SchedulerTaskQueue queue, SchedulerTask task)
    {
        if (IsDisposed)
            return;

        if (queue.IsEmpty && !_schedulerTaskQueues.Contains(queue))
        {
            _schedulerTaskQueues.Add(queue);
        }

        queue.Add(task);
        QueueTask(SchedulerTaskSlot, task.EnqueueOrder);
    }

    /// <summary>
    /// https://wicg.github.io/scheduling-apis/#select-the-next-scheduler-task-queue-from-all-schedulers: the queue of the
    /// highest effective priority (at least <paramref name="minimumEffectivePriority"/>) that has a runnable task, and whose
    /// first task is the oldest; null when there is none.
    /// </summary>
    /// <remarks>
    /// The specification leaves the choice between the next task of the schedulers and the tasks of the other task sources
    /// to the user agent, and suggests the strategy that <see cref="RunTask"/> follows, as Chromium: the queues whose
    /// effective priority is 3 or higher (the user-visible continuations, the user-blocking tasks and continuations) run
    /// before the other tasks, the user-visible tasks are scheduled as the other tasks (e.g. the timers), in the order they
    /// were queued, and the background tasks and continuations run when no other task is queued.
    /// </remarks>
    private SchedulerTaskQueue? SelectNextSchedulerTaskQueue(int minimumEffectivePriority)
    {
        SchedulerTaskQueue? result = null;
        var resultPriority = -1;
        for (var i = _schedulerTaskQueues.Count - 1; i >= 0; i--)
        {
            var queue = _schedulerTaskQueues[i];

            // The tasks of a document that is no longer fully active (or of a closing worker) never run: there is no
            // back/forward cache that could make the document fully active again
            if (queue.First is not { } first || !queue.Scheduler.IsRunnable)
            {
                _schedulerTaskQueues.RemoveAt(i);
                continue;
            }

            var priority = queue.EffectivePriority;
            if (priority < minimumEffectivePriority)
                continue;

            if (priority > resultPriority || (priority == resultPriority && first.EnqueueOrder < result!.First!.EnqueueOrder))
            {
                result = queue;
                resultPriority = priority;
            }
        }

        return result;
    }

    /// <summary>Runs the oldest task of a scheduler task queue, followed by a microtask checkpoint, with the continuation state of the task.</summary>
    private void RunSchedulerTask(SchedulerTaskQueue queue)
    {
        var task = queue.First!;
        queue.Remove(task);
        var continuationState = CurrentContinuationState;
        CurrentContinuationState = task.ContinuationState;
        try
        {
            task.Steps();
            PerformMicrotaskCheckpoint();
        }
        finally
        {
            CurrentContinuationState = continuationState;
        }
    }
}

/// <summary>The continuation state captured by a job callback, and the event loop whose state it restores when the callback is called.</summary>
internal sealed class ContinuationStateSnapshot(DomEventLoop eventLoop, SchedulerContinuationState? state)
{
    public DomEventLoop EventLoop { get; } = eventLoop;

    public SchedulerContinuationState? State { get; } = state;
}
