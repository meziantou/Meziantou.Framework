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
    private int _disposed;

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

        Thread.Start();

        DisposeThreadJoinTimeout = TimeSpan.FromMilliseconds(1000);
        WaitTimeout = TimeSpan.FromMilliseconds(100);
    }

    private Thread? Thread { get; set; }

    /// <summary>Gets or sets a value indicating whether to dequeue remaining tasks when the scheduler is disposed.</summary>
    public bool DequeueOnDispose { get; set; }

    /// <summary>Gets or sets the timeout to wait for the worker thread to complete when disposing.</summary>
    public TimeSpan DisposeThreadJoinTimeout { get; set; }

    /// <summary>Gets or sets the timeout for waiting on the event handle.</summary>
    public TimeSpan WaitTimeout { get; set; }

    /// <summary>Gets or sets the timeout for dequeueing tasks.</summary>
    public TimeSpan DequeueTimeout { get; set; }

    /// <summary>Gets the number of tasks currently queued to the scheduler.</summary>
    public int QueueCount => _tasks.Count;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        // Signal the worker thread to stop. It drains the remaining tasks itself (see ThreadExecute), so
        // the single-threaded execution guarantee holds even when the join below times out.
        _stop.Set();

        var thread = Thread;
        var exited = thread is null || !thread.IsAlive || thread.Join(DisposeThreadJoinTimeout);

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
        catch
        {
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

    protected override IEnumerable<Task> GetScheduledTasks() => _tasks;

    protected override void QueueTask(Task task)
    {
        ArgumentNullException.ThrowIfNull(task);
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        _tasks.Enqueue(task);
        _dequeue.Set();
    }

    protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
    {
        return false;
    }
}
