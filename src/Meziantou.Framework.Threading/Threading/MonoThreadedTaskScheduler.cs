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

        _thread.Start();

        DisposeThreadJoinTimeout = TimeSpan.FromMilliseconds(1000);
        WaitTimeout = TimeSpan.FromMilliseconds(100);
    }

    /// <summary>Gets or sets a value indicating whether to dequeue remaining tasks when the scheduler is disposed.</summary>
    /// <remarks>
    /// When <see langword="true"/>, every task the scheduler accepted is executed before the worker thread exits.
    /// When <see langword="false"/>, the tasks that have not started running when <see cref="Dispose"/> is called are
    /// abandoned and never complete. In both cases no task is accepted once <see cref="Dispose"/> has been called.
    /// </remarks>
    public bool DequeueOnDispose { get; set; }

    /// <summary>Gets or sets the timeout to wait for the worker thread to complete when disposing.</summary>
    /// <remarks>The wait is skipped when <see cref="Dispose"/> is called from the scheduler's own thread.</remarks>
    public TimeSpan DisposeThreadJoinTimeout { get; set; }

    /// <summary>Gets or sets the timeout for waiting on the event handle.</summary>
    public TimeSpan WaitTimeout { get; set; }

    /// <summary>Gets or sets the timeout for dequeueing tasks.</summary>
    public TimeSpan DequeueTimeout { get; set; }

    /// <summary>Gets the number of tasks currently queued to the scheduler.</summary>
    public int QueueCount => _tasks.Count;

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
            _thread.Join(DisposeThreadJoinTimeout);
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
        catch
        {
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
            var i = WaitHandle.WaitAny(_waitHandles, WaitTimeout);
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

            _tasks.Enqueue(task);
            _dequeue.Set();
        }
    }

    protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
    {
        return false;
    }
}
