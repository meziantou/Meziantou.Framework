namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The callback of <see cref="Scheduler.PostTask"/> (https://wicg.github.io/scheduling-apis/#callbackdef-schedulerposttaskcallback): its result fulfills the task.</summary>
/// <seealso href="https://wicg.github.io/scheduling-apis/#callbackdef-schedulerposttaskcallback"/>
[IdlInterface("SchedulerPostTaskCallback", "https://wicg.github.io/scheduling-apis/#callbackdef-schedulerposttaskcallback")]
public delegate object? SchedulerPostTaskCallback();

/// <summary>The options of <see cref="Scheduler.PostTask"/> (https://wicg.github.io/scheduling-apis/#dictdef-schedulerposttaskoptions).</summary>
/// <seealso href="https://wicg.github.io/scheduling-apis/#dictdef-schedulerposttaskoptions"/>
[IdlInterface("SchedulerPostTaskOptions", "https://wicg.github.io/scheduling-apis/#dictdef-schedulerposttaskoptions")]
public sealed class SchedulerPostTaskOptions
{
    /// <summary>
    /// Gets the signal that aborts the task; when it is a <see cref="TaskSignal"/> and <see cref="Priority"/> is null, the
    /// task also follows the priority of the signal.
    /// </summary>
    /// <seealso href="https://wicg.github.io/scheduling-apis/#dom-schedulerposttaskoptions-signal"/>
    [IdlMember("signal", "https://wicg.github.io/scheduling-apis/#dom-schedulerposttaskoptions-signal")]
    public AbortSignal? Signal { get; init; }

    /// <summary>Gets the priority of the task, which then does not change; null for the priority of <see cref="Signal"/>, or <see cref="TaskPriority.UserVisible"/>.</summary>
    /// <seealso href="https://wicg.github.io/scheduling-apis/#dom-schedulerposttaskoptions-priority"/>
    [IdlMember("priority", "https://wicg.github.io/scheduling-apis/#dom-schedulerposttaskoptions-priority")]
    public TaskPriority? Priority { get; init; }

    /// <summary>Gets the minimum time, in milliseconds, before the task is queued.</summary>
    /// <seealso href="https://wicg.github.io/scheduling-apis/#dom-schedulerposttaskoptions-delay"/>
    [IdlMember("delay", "https://wicg.github.io/scheduling-apis/#dom-schedulerposttaskoptions-delay")]
    public ulong Delay { get; init; }
}

/// <summary>
/// The scheduler of a window or of a worker (https://wicg.github.io/scheduling-apis/#sec-scheduler, see
/// <see cref="GlobalScope.Scheduler"/>): it posts prioritized tasks to the event loop. The tasks of all the schedulers of an
/// event loop run in the order of their priorities (<see cref="TaskPriority"/>), then in the order they were queued; a
/// continuation (<see cref="Yield"/>) runs before the tasks of its priority.
/// </summary>
/// <seealso href="https://wicg.github.io/scheduling-apis/#scheduler"/>
[IdlInterface("Scheduler", "https://wicg.github.io/scheduling-apis/#scheduler")]
public sealed class Scheduler
{
    private readonly GlobalScope _global;

    /// <summary>https://wicg.github.io/scheduling-apis/#scheduler-static-priority-task-queue-map</summary>
    private readonly Dictionary<(TaskPriority Priority, bool IsContinuation), SchedulerTaskQueue> _staticPriorityTaskQueues = [];

    /// <summary>https://wicg.github.io/scheduling-apis/#scheduler-dynamic-priority-task-queue-map</summary>
    private readonly Dictionary<(TaskSignal Signal, bool IsContinuation), SchedulerTaskQueue> _dynamicPriorityTaskQueues = [];

    internal Scheduler(GlobalScope global)
    {
        _global = global;
        if (global.ScriptEngine is { } scriptEngine)
        {
            scriptEngine.PropagatesContinuationState = true;
        }
    }

    /// <summary>Whether the tasks of the scheduler are runnable: the document of its window is fully active, or its worker is not closing.</summary>
    internal bool IsRunnable => _global.CanRunScript;

    /// <summary>
    /// Posts a task that calls <paramref name="callback"/> (https://wicg.github.io/scheduling-apis/#dom-scheduler-posttask).
    /// The returned task is completed with the result of the callback (when the caller is a script and the result is a
    /// promise, with its settlement), fails with the exception the callback throws, or with the abort reason of the signal of
    /// the options when it is aborted before the callback runs.
    /// </summary>
    /// <param name="callback">The callback of the task.</param>
    /// <param name="options">The signal, the priority and the delay of the task, or null.</param>
    /// <seealso href="https://wicg.github.io/scheduling-apis/#dom-scheduler-posttask"/>
    [IdlMember("postTask", "https://wicg.github.io/scheduling-apis/#dom-scheduler-posttask")]
    public Task<object?> PostTask(SchedulerPostTaskCallback callback, SchedulerPostTaskOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(callback);
        options ??= new SchedulerPostTaskOptions();

        // https://wicg.github.io/scheduling-apis/#schedule-a-posttask-task
        var result = new TaskCompletionSource<object?>(TaskCreationOptions.None);
        if (!IsRunnable)
        {
            // As in the browsers: the scheduler of a document that is not fully active schedules nothing
            result.SetException(DomException.NotSupported("The scheduler of a document that is not fully active cannot schedule tasks"));
            return result.Task;
        }

        var signal = options.Signal;
        if (signal is { Aborted: true })
        {
            result.SetException(ToException(signal.AbortReason));
            return result.Task;
        }

        var prioritySource = options.Priority is { } priority ? TaskSignal.CreateFixedPriorityUnabortableSignal(priority)
            : signal as TaskSignal ?? TaskSignal.CreateFixedPriorityUnabortableSignal(TaskPriority.UserVisible);
        var state = new SchedulingState(signal, prioritySource);
        var handle = new TaskHandle(signal, reason => result.TrySetException(ToException(reason)));
        signal?.AddAbortAlgorithm(handle.Abort);

        var eventLoop = _global.EventLoop;
        void EnqueueSteps()
        {
            handle.Queue = SelectTaskQueue(prioritySource, isContinuation: false);
            ScheduleTask(handle, SchedulerContinuationState.Create(this, state), () =>
            {
                try
                {
                    result.TrySetResult(callback());
                }
#pragma warning disable CA1031 // The exception of the callback rejects the promise of the task
                catch (Exception ex)
#pragma warning restore CA1031
                {
                    result.TrySetException(ex);
                }
            });
        }

        if (options.Delay > 0)
        {
            // "run steps after a timeout given scheduler's relevant global object, "scheduler-postTask", delay"
            eventLoop.QueueTimer(TimeSpan.FromMilliseconds(options.Delay), () =>
            {
                if (signal is null || !signal.Aborted)
                {
                    EnqueueSteps();
                }
            });
        }
        else
        {
            EnqueueSteps();
        }

        return result.Task;
    }

    /// <summary>
    /// Yields to the event loop (https://wicg.github.io/scheduling-apis/#dom-scheduler-yield): the returned task is completed
    /// by a continuation, which runs before the tasks of its priority. In a task posted with <see cref="PostTask"/> (and in
    /// its continuations), the continuation inherits the priority and the signal of the task: it fails with the abort reason
    /// when the signal is aborted. Otherwise its priority is <see cref="TaskPriority.UserVisible"/>.
    /// </summary>
    /// <seealso href="https://wicg.github.io/scheduling-apis/#dom-scheduler-yield"/>
    [IdlMember("yield", "https://wicg.github.io/scheduling-apis/#dom-scheduler-yield")]
    public Task Yield()
    {
        // https://wicg.github.io/scheduling-apis/#schedule-a-yield-continuation
        var result = new TaskCompletionSource(TaskCreationOptions.None);
        if (!IsRunnable)
        {
            result.SetException(DomException.NotSupported("The scheduler of a document that is not fully active cannot schedule tasks"));
            return result.Task;
        }

        var continuationState = _global.EventLoop.CurrentContinuationState;
        var inheritedState = continuationState?.Get(this);
        var abortSource = inheritedState?.AbortSource;
        if (abortSource is { Aborted: true })
        {
            result.SetException(ToException(abortSource.AbortReason));
            return result.Task;
        }

        var prioritySource = inheritedState?.PrioritySource ?? TaskSignal.CreateFixedPriorityUnabortableSignal(TaskPriority.UserVisible);
        var handle = new TaskHandle(abortSource, reason => result.TrySetException(ToException(reason)));
        abortSource?.AddAbortAlgorithm(handle.Abort);
        handle.Queue = SelectTaskQueue(prioritySource, isContinuation: true);

        // The reactions of the promise run with the scheduling state the continuation inherited (see SchedulerTask.ContinuationState)
        ScheduleTask(handle, continuationState, () => result.TrySetResult());
        return result.Task;
    }

    private static Exception ToException(object? reason) => reason as Exception ?? new AbortSignalException(reason);

    /// <summary>https://wicg.github.io/scheduling-apis/#select-the-scheduler-task-queue</summary>
    private SchedulerTaskQueue SelectTaskQueue(TaskSignal signal, bool isContinuation)
    {
        if (!signal.HasFixedPriority)
        {
            // The queue follows the priority of the signal (the priority change algorithm of the specification)
            if (!_dynamicPriorityTaskQueues.TryGetValue((signal, isContinuation), out var dynamicQueue))
            {
                dynamicQueue = new SchedulerTaskQueue(this, signal, isContinuation, queue => _dynamicPriorityTaskQueues.Remove((signal, isContinuation)));
                _dynamicPriorityTaskQueues.Add((signal, isContinuation), dynamicQueue);
            }

            return dynamicQueue;
        }

        var priority = signal.Priority;
        if (!_staticPriorityTaskQueues.TryGetValue((priority, isContinuation), out var staticQueue))
        {
            staticQueue = new SchedulerTaskQueue(this, signal, isContinuation, queue => _staticPriorityTaskQueues.Remove((priority, isContinuation)));
            _staticPriorityTaskQueues.Add((priority, isContinuation), staticQueue);
        }

        return staticQueue;
    }

    /// <summary>https://wicg.github.io/scheduling-apis/#schedule-a-task-to-invoke-an-algorithm</summary>
    private void ScheduleTask(TaskHandle handle, SchedulerContinuationState? continuationState, Action steps)
    {
        var eventLoop = _global.EventLoop;
        var task = new SchedulerTask(eventLoop.GetNextSchedulerEnqueueOrder(), continuationState, () =>
        {
            steps();
            handle.Complete();
        });

        handle.Task = task;
        eventLoop.QueueSchedulerTask(handle.Queue!, task);
    }

    /// <summary>https://wicg.github.io/scheduling-apis/#task-handle</summary>
    private sealed class TaskHandle(AbortSignal? signal, Action<object?> reject)
    {
        public SchedulerTask? Task { get; set; }

        public SchedulerTaskQueue? Queue { get; set; }

        /// <summary>The abort steps: the promise is rejected with the abort reason, and the task is removed from its queue.</summary>
        public void Abort()
        {
            reject(signal!.AbortReason);
            if (Task is not null && Queue is { } queue)
            {
                queue.Remove(Task);
                queue.RemoveIfEmpty();
            }
        }

        /// <summary>The task complete steps.</summary>
        public void Complete()
        {
            signal?.RemoveAbortAlgorithm(Abort);
            Queue?.RemoveIfEmpty();
        }
    }
}

/// <summary>
/// https://wicg.github.io/scheduling-apis/#scheduling-state: the signals of the task a continuation descends from.
/// </summary>
/// <param name="AbortSource">The signal that aborts the continuations, or null.</param>
/// <param name="PrioritySource">The signal whose priority the continuations have.</param>
internal sealed record SchedulingState(AbortSignal? AbortSource, TaskSignal PrioritySource);

/// <summary>
/// https://wicg.github.io/scheduling-apis/#continuation-state: the scheduling states of the schedulers of an event loop
/// (the state map), which the microtasks inherit. The states are immutable: cloning a state shares it.
/// </summary>
internal sealed class SchedulerContinuationState
{
    private readonly (Scheduler Scheduler, SchedulingState State)[] _states;

    private SchedulerContinuationState((Scheduler Scheduler, SchedulingState State)[] states) => _states = states;

    /// <summary>The continuation state of a task of a scheduler: "set the current scheduling state for scheduler to state", from a null continuation state.</summary>
    public static SchedulerContinuationState Create(Scheduler scheduler, SchedulingState state) => new([(scheduler, state)]);

    /// <summary>https://wicg.github.io/scheduling-apis/#get-the-continuation-state-value</summary>
    public SchedulingState? Get(Scheduler scheduler)
    {
        foreach (var (key, state) in _states)
        {
            if (ReferenceEquals(key, scheduler))
                return state;
        }

        return null;
    }
}

/// <summary>https://wicg.github.io/scheduling-apis/#scheduler-task: a task of a scheduler task queue.</summary>
/// <param name="enqueueOrder">The enqueue order: the tasks of the queues of a priority run in this order.</param>
/// <param name="continuationState">
/// The continuation state of the event loop while the task and its microtasks run: the scheduling state of a posted task,
/// or the state a continuation inherited.
/// </param>
/// <param name="steps">The steps of the task.</param>
internal sealed class SchedulerTask(long enqueueOrder, SchedulerContinuationState? continuationState, Action steps)
{
    public long EnqueueOrder { get; } = enqueueOrder;

    public SchedulerContinuationState? ContinuationState { get; } = continuationState;

    public Action Steps { get; } = steps;

    /// <summary>The node of the task in its queue, or null once it is removed.</summary>
    public LinkedListNode<SchedulerTask>? Node { get; set; }
}

/// <summary>https://wicg.github.io/scheduling-apis/#scheduler-task-queue</summary>
/// <param name="scheduler">The scheduler of the queue.</param>
/// <param name="prioritySource">The signal whose priority is the priority of the queue (it changes with the signal).</param>
/// <param name="isContinuation">Whether the tasks of the queue are continuations.</param>
/// <param name="removalSteps">The removal steps, which remove the queue from the map of its scheduler.</param>
internal sealed class SchedulerTaskQueue(Scheduler scheduler, TaskSignal prioritySource, bool isContinuation, Action<SchedulerTaskQueue> removalSteps)
{
    private readonly LinkedList<SchedulerTask> _tasks = new();
    private bool _removed;

    public Scheduler Scheduler { get; } = scheduler;

    public bool IsEmpty => _tasks.Count == 0;

    /// <summary>The oldest task of the queue.</summary>
    public SchedulerTask? First => _tasks.First?.Value;

    /// <summary>
    /// https://wicg.github.io/scheduling-apis/#scheduler-task-queue-effective-priority: from 0 (the background tasks) to 5
    /// (the user-blocking continuations); the continuations of a priority precede its tasks.
    /// </summary>
    public int EffectivePriority => (prioritySource.Priority switch
    {
        TaskPriority.Background => 0,
        TaskPriority.UserVisible => 2,
        _ => 4,
    }) + (isContinuation ? 1 : 0);

    public void Add(SchedulerTask task) => task.Node = _tasks.AddLast(task);

    public void Remove(SchedulerTask task)
    {
        if (task.Node is { } node)
        {
            _tasks.Remove(node);
            task.Node = null;
        }
    }

    /// <summary>"If queue is empty, then run queue's removal steps".</summary>
    public void RemoveIfEmpty()
    {
        if (_tasks.Count == 0 && !_removed)
        {
            _removed = true;
            removalSteps(this);
        }
    }
}
