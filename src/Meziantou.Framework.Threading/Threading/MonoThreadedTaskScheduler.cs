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
    private int _disposed;
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

        Thread = new Thread(SafeThreadExecute)
        {
            IsBackground = true,
            Name = threadName,
        };

        // The worker reads the configuration, so nothing may be left to initialize once it is started.
        Thread.Start();
    }

    private Thread? Thread { get; set; }

    /// <summary>Gets or sets a value indicating whether to dequeue remaining tasks when the scheduler is disposed.</summary>
    public bool DequeueOnDispose { get; set; }

    /// <summary>Gets or sets the timeout to wait for the worker thread to complete when disposing. The default is one second.</summary>
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
            if (Volatile.Read(ref _disposed) == 0)
            {
                _dequeue.Set();
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
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        // Signal the worker thread to stop. It drains the remaining tasks itself (see ThreadExecute), so
        // the single-threaded execution guarantee holds even when the join below times out.
        _stop.Set();

        var thread = Thread;
        var exited = thread is null || !thread.IsAlive || thread.Join(Volatile.Read(ref _disposeThreadJoinTimeout));

        Thread = null;

        // Disposing a wait handle while the worker is still blocked on it (in ThreadExecute's WaitAny) is a
        // race condition that can throw or corrupt the wait. When the join times out the worker is still
        // running, so the handles are left to be reclaimed by the GC instead of being pulled out from under it.
        if (exited)
        {
            _stop.Dispose();
            _dequeue.Dispose();
        }
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

    private void Dequeue()
    {
        do
        {
            if (!_tasks.TryDequeue(out var task))
                break;

            ExecuteTask(task);
        }
        while (true);
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
    }

    private void ThreadExecute()
    {
        do
        {
            var i = WaitHandle.WaitAny(_waitHandles, Volatile.Read(ref _waitTimeout));
            if (i == 0)
                break;

            // note: we can dequeue on _dequeue event, or on timeout
            Dequeue();
        }
        while (true);

        // The final drain runs on the worker thread rather than on the thread calling Dispose, so queued
        // tasks never execute on two threads at once.
        if (DequeueOnDispose)
        {
            Dequeue();
        }
    }

    /// <summary>Gets the maximum concurrency level supported by this scheduler, which is always 1.</summary>
    public override int MaximumConcurrencyLevel => 1;

    protected override IEnumerable<Task> GetScheduledTasks() => _tasks;

    protected override void QueueTask(Task task)
    {
        ArgumentNullException.ThrowIfNull(task);
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        var workerException = WorkerException;
        if (workerException is not null)
            throw new InvalidOperationException("The worker thread of the task scheduler has terminated unexpectedly", workerException);

        _tasks.Enqueue(task);
        _dequeue.Set();
    }

    protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
    {
        return false;
    }
}
