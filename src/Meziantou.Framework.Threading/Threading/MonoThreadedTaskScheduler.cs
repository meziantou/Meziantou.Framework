using System.Collections.Concurrent;

namespace Meziantou.Framework.Threading;

/// <summary>Provides a task scheduler that executes tasks on a single dedicated thread.</summary>
/// <example>
/// <code><![CDATA[
/// using var scheduler = new MonoThreadedTaskScheduler("MyWorkerThread");
/// var task = Task.Factory.StartNew(
///     () => Console.WriteLine($"Running on thread: {Thread.CurrentThread.Name}"),
///     CancellationToken.None,
///     TaskCreationOptions.None,
///     scheduler);
/// await task;
/// ]]></code>
/// </example>
public sealed class MonoThreadedTaskScheduler : TaskScheduler, IDisposable
{
    private const int DefaultDisposeThreadJoinTimeoutInMilliseconds = 1000;

    private readonly ConcurrentQueue<Task> _tasks = new();
    private readonly AutoResetEvent _stop = new(initialState: false);
    private readonly AutoResetEvent _dequeue = new(initialState: false);
    // note: Stop must be first in the array (in case both events happen at the same exact time)
    private readonly WaitHandle[] _waitHandles;
    // Serializes task acceptance with shutdown, so a task is either enqueued while the worker is guaranteed to
    // still observe it, or rejected.
    private readonly Lock _gate = new();
    private readonly Thread _thread;
    private volatile bool _shutdownRequested;
    private int _waitHandleReleaseCount;
    // Timeouts are stored as milliseconds so the worker thread reads them atomically, whatever the pointer size.
    private int _waitTimeout = Timeout.Infinite;
    private int _disposeThreadJoinTimeout = DefaultDisposeThreadJoinTimeoutInMilliseconds;
    private Exception? _workerException;

    /// <summary>Initializes a new instance of the <see cref="MonoThreadedTaskScheduler"/> class.</summary>
    public MonoThreadedTaskScheduler()
        : this(threadName: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="MonoThreadedTaskScheduler"/> class with the specified thread name.</summary>
    /// <param name="threadName">The name of the worker thread.</param>
    public MonoThreadedTaskScheduler(string? threadName)
    {
        _waitHandles = [_stop, _dequeue];

        _thread = new Thread(SafeThreadExecute)
        {
            IsBackground = true,
            Name = threadName,
        };

        // The worker reads the configuration, so nothing may be left to initialize once it is started.
        _thread.Start();
    }

    /// <summary>Gets or sets a value indicating whether to dequeue remaining tasks when the scheduler is disposed.</summary>
    /// <remarks>
    /// When <see langword="true"/>, every task the scheduler accepted is executed before the worker thread exits.
    /// When <see langword="false"/>, the tasks that have not started running when <see cref="Dispose"/> is called are
    /// abandoned and never complete. In both cases no task is accepted once <see cref="Dispose"/> has been called.
    /// </remarks>
    public bool DequeueOnDispose { get; set; }

    /// <summary>Gets or sets the timeout to wait for the worker thread to complete when disposing. The default is one second.</summary>
    /// <remarks>The wait is skipped when <see cref="Dispose"/> is called from the scheduler's own thread.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative and is not <see cref="Timeout.InfiniteTimeSpan"/>, or represents more than <see cref="int.MaxValue"/> milliseconds.</exception>
    public TimeSpan DisposeThreadJoinTimeout
    {
        get => ToTimeSpan(Volatile.Read(ref _disposeThreadJoinTimeout));
        set => Volatile.Write(ref _disposeThreadJoinTimeout, ToMilliseconds(value, nameof(value)));
    }

    /// <summary>Gets or sets the timeout for waiting on the event handles. The default is <see cref="Timeout.InfiniteTimeSpan"/>.</summary>
    /// <remarks>
    /// Queued tasks and disposal both signal the worker thread, so polling is not needed to make progress. Set a
    /// finite value only to have the worker thread wake up periodically while the scheduler is idle.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative and is not <see cref="Timeout.InfiniteTimeSpan"/>, or represents more than <see cref="int.MaxValue"/> milliseconds.</exception>
    public TimeSpan WaitTimeout
    {
        get => ToTimeSpan(Volatile.Read(ref _waitTimeout));
        set
        {
            Volatile.Write(ref _waitTimeout, ToMilliseconds(value, nameof(value)));

            // The worker thread may already be blocked on the previous timeout, which is infinite by default.
            // Waking it up makes the new value take effect now instead of never.
            lock (_gate)
            {
                if (!_shutdownRequested)
                {
                    _dequeue.Set();
                }
            }
        }
    }

    /// <summary>Gets or sets the timeout for dequeueing tasks.</summary>
    [Obsolete("This property is not used by the scheduler and will be removed in a future version.")]
    public TimeSpan DequeueTimeout { get; set; }

    /// <summary>Gets the number of tasks currently queued to the scheduler.</summary>
    public int QueueCount => _tasks.Count;

    /// <summary>Gets the exception that terminated the worker thread, or <see langword="null"/> when the worker thread is still running or has stopped normally.</summary>
    /// <remarks>
    /// A terminated worker thread cannot be replaced without breaking the guarantee that every task runs on the same
    /// thread. The scheduler therefore stops accepting tasks once this property is not <see langword="null"/>.
    /// </remarks>
    public Exception? WorkerException => Volatile.Read(ref _workerException);

    public void Dispose()
    {
        lock (_gate)
        {
            if (_shutdownRequested)
                return;

            // Requesting the shutdown before signaling the event means every task QueueTask accepted was enqueued
            // before the worker can observe the stop request, and that no task is accepted afterwards. Every
            // accepted task therefore has a defined outcome: it runs, or it is abandoned per DequeueOnDispose.
            _shutdownRequested = true;
            _stop.Set();
        }

        // Joining the worker thread from the worker thread itself (Dispose called by a task running on this
        // scheduler) can never complete: it would deadlock on an infinite timeout, and stall for
        // DisposeThreadJoinTimeout otherwise. The worker finishes its shutdown, final drain included, as soon as
        // the current task returns.
        if (_thread != Thread.CurrentThread)
        {
            _thread.Join(Volatile.Read(ref _disposeThreadJoinTimeout));
        }

        ReleaseWaitHandles();
    }

    // Disposing a wait handle while the worker is still blocked on it (in ThreadExecute's WaitAny) is a race
    // condition that can throw or corrupt the wait. Both the worker (once it has stopped using the handles) and
    // Dispose (once it has stopped waiting for the worker) call this, and the second one releases them.
    private void ReleaseWaitHandles()
    {
        if (Interlocked.Increment(ref _waitHandleReleaseCount) != 2)
            return;

        _stop.Dispose();
        _dequeue.Dispose();
    }

    private static int ToMilliseconds(TimeSpan value, string paramName)
    {
        if (value == Timeout.InfiniteTimeSpan)
            return Timeout.Infinite;

        var milliseconds = (long)value.TotalMilliseconds;
        ArgumentOutOfRangeException.ThrowIfLessThan(milliseconds, 0, paramName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(milliseconds, int.MaxValue, paramName);
        return (int)milliseconds;
    }

    private static TimeSpan ToTimeSpan(int milliseconds)
    {
        return milliseconds == Timeout.Infinite ? Timeout.InfiniteTimeSpan : TimeSpan.FromMilliseconds(milliseconds);
    }

    private bool ExecuteTask(Task task)
    {
        return TryExecuteTask(task);
    }

    private void Dequeue(bool untilEmpty)
    {
        // Outside of the final drain, stop as soon as a shutdown is requested: DequeueOnDispose must decide whether
        // the queued tasks run, not whether a drain happened to be in progress when Dispose was called.
        while (untilEmpty || !_shutdownRequested)
        {
            if (!_tasks.TryDequeue(out var task))
                break;

            ExecuteTask(task);
        }
    }

    private void SafeThreadExecute()
    {
        try
        {
            ThreadExecute();
        }
        catch (Exception ex)
        {
            // Rethrowing here would take the process down, but silently returning would leave a scheduler that
            // accepts tasks nobody will ever run. Record the failure instead; QueueTask reports it to callers.
            Volatile.Write(ref _workerException, ex);
        }
        finally
        {
            ReleaseWaitHandles();
        }
    }

    private void ThreadExecute()
    {
        do
        {
            var i = WaitHandle.WaitAny(_waitHandles, Volatile.Read(ref _waitTimeout));
            if (i == 0)
                break;

            // note: we can dequeue on _dequeue event, or on timeout
            Dequeue(untilEmpty: false);
        }
        while (true);

        // The final drain runs on the worker thread rather than on the thread calling Dispose, so queued
        // tasks never execute on two threads at once.
        if (DequeueOnDispose)
        {
            Dequeue(untilEmpty: true);
        }
    }

    /// <summary>Gets the maximum concurrency level supported by this scheduler, which is always 1.</summary>
    public override int MaximumConcurrencyLevel => 1;

    protected override IEnumerable<Task> GetScheduledTasks() => _tasks;

    protected override void QueueTask(Task task)
    {
        ArgumentNullException.ThrowIfNull(task);

        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_shutdownRequested, this);

            var workerException = WorkerException;
            if (workerException is not null)
                throw new InvalidOperationException("The worker thread of the task scheduler has terminated unexpectedly", workerException);

            _tasks.Enqueue(task);
            _dequeue.Set();
        }
    }

    protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
    {
        return false;
    }
}
