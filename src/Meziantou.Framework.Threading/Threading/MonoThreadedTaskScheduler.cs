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

    /// <summary>Initializes a new instance of the <see cref="MonoThreadedTaskScheduler"/> class.</summary>
    public MonoThreadedTaskScheduler()
        : this(threadName: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="MonoThreadedTaskScheduler"/> class with the specified thread name.</summary>
    /// <param name="threadName">The name of the worker thread.</param>
    public MonoThreadedTaskScheduler(string? threadName)
    {
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
        if (_stop is not null)
        {
            _stop.Set();
            _stop.Dispose();
        }

        _dequeue?.Dispose();

        if (DequeueOnDispose)
        {
            Dequeue();
        }

        if (Thread is not null && Thread.IsAlive)
        {
            Thread.Join(DisposeThreadJoinTimeout);
        }

        Thread = null;
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
            // note: Stop must be first in array (in case both events happen at the same exact time)
            var i = WaitHandle.WaitAny([_stop, _dequeue], WaitTimeout);
            if (i == 0)
                break;

            // note: we can dequeue on _dequeue event, or on timeout
            Dequeue();
        }
        while (true);
    }

    protected override IEnumerable<Task> GetScheduledTasks() => _tasks;

    protected override void QueueTask(Task task)
    {
        ArgumentNullException.ThrowIfNull(task);

        _tasks.Enqueue(task);
        _dequeue.Set();
    }

    protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued)
    {
        return false;
    }
}
