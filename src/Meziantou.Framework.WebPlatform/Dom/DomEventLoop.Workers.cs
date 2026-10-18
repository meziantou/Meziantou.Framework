namespace Meziantou.Framework.WebPlatform.Dom;

// The worker event loops (https://html.spec.whatwg.org/multipage/webappapis.html#worker-event-loop): each worker runs its
// own event loop on its own thread. The event loop of the owner of a worker (the event loop of the browsing contexts of
// its window, or the event loop of its parent worker) considers that an asynchronous operation is in progress while the
// worker has a task to run or waits for an operation, so that it is not idle while the worker may still post a message.
public sealed partial class DomEventLoop
{
    [ThreadStatic]
    private static DomEventLoop? s_currentWorkerLoop;

    private static readonly TimeSpan FakeClockPollingInterval = TimeSpan.FromMilliseconds(10);

    private readonly List<DomEventLoop> _childLoops = [];

    // Wakes up the thread of a worker: a task, a timer or the end of an operation. The thread does not depend on the thread
    // pool to wake up (the hosts may block its threads, e.g. the test runners)
    private readonly ManualResetEventSlim? _workerSignal;

    // Whether the loop of a worker holds an operation of its owner (guarded by _tasksLock)
    private bool _busy;

    // Whether the thread of the worker runs a task that is not blocked by a synchronous wait (e.g. a synchronous fetch)
    private int _runningTask;
    private int _blockingWaits;
    private int _runningOperations;

    // Whether the thread of the worker has started (until the event loop is disposed at the end of its cleanup), and whether
    // the worker is terminated or closing
    private int _threadStarted;
    private int _ending;

    // Incremented each time a worker created by the documents of the event loop (or by its workers) stops running
    private long _workerActivity;

    /// <summary>Creates the event loop of a worker, whose owner is the event loop of the document or of the worker that created it.</summary>
    internal DomEventLoop(DomEventLoop owner, Action onOwnerDisposed)
    {
        Owner = owner;
        OnOwnerDisposed = onOwnerDisposed;
        TimeProvider = owner.TimeProvider;
        _workerSignal = new ManualResetEventSlim(initialState: false);
        lock (owner._childLoops)
        {
            if (owner.IsDisposed)
            {
                _isDisposed = true;
                _disposed.TrySetResult();
                return;
            }

            owner._childLoops.Add(this);
        }
    }

    /// <summary>
    /// Creates a child event loop that has no thread and only tracks the operations that run in parallel for its owner (e.g.
    /// the jobs of the service workers, see <see cref="BeginRunningOperation"/>): the owner is busy, and a host that
    /// advances a fake clock waits for them, as for the tasks of a worker.
    /// </summary>
    internal static DomEventLoop CreateOperationTracker(DomEventLoop owner) => new(owner, static () => { }) { _isOperationTracker = true };

    // Whether the event loop only tracks operations (it has no thread that releases its owner when it is idle)
    private bool _isOperationTracker;

    /// <summary>The event loop of the owner of the worker, or null for the event loop of a browsing context group.</summary>
    internal DomEventLoop? Owner { get; }

    /// <summary>Called when the owner of the worker is disposed: the worker is terminated.</summary>
    private Action? OnOwnerDisposed { get; }

    /// <summary>Gets whether workers created by the documents of the event loop (or by its workers) are alive.</summary>
    internal bool HasWorkers => HasChildLoops;

    private bool HasChildLoops
    {
        get
        {
            lock (_childLoops)
            {
                return _childLoops.Count > 0;
            }
        }
    }

    /// <summary>
    /// Gets whether a worker created by the documents of the event loop (or by its workers) has a task to run, or runs a
    /// task that is not waiting for a synchronous operation: a host that advances a fake clock waits for the workers
    /// before advancing it.
    /// </summary>
    public bool HasRunningWorkers
    {
        get
        {
            DomEventLoop[] children;
            lock (_childLoops)
            {
                children = [.. _childLoops];
            }

            foreach (var child in children)
            {
                if (child.IsRunning || child.HasRunningWorkers)
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Gets a value that changes each time a worker created by the documents of the event loop (or by its workers) stops
    /// running (see <see cref="HasRunningWorkers"/>): it ran a task, or its task waits for a synchronous operation. A worker
    /// may run between two reads of <see cref="HasRunningWorkers"/> (e.g. its timer becomes due when another thread reads
    /// the clock) and add a timer or queue a task meanwhile: a host that advances a fake clock reads this value before
    /// computing the next timer, and does not advance the clock if it changed.
    /// </summary>
    public long WorkerActivity => Interlocked.Read(ref _workerActivity);

    /// <summary>
    /// Called by the event loop of a worker before it stops running: a host that sees that the worker is not running also
    /// sees the new <see cref="WorkerActivity"/> of its owners.
    /// </summary>
    private void OnStoppingRunning()
    {
        for (var owner = Owner; owner is not null; owner = owner.Owner)
        {
            Interlocked.Increment(ref owner._workerActivity);
        }
    }

    private bool IsRunning
    {
        get
        {
            if (IsDisposed)
                return false;

            if (Volatile.Read(ref _runningOperations) > 0)
                return true;

            // A terminated worker runs until its thread has cleaned up (e.g. revoked its blob URLs, https://w3c.github.io/FileAPI/#lifeTime)
            if (Volatile.Read(ref _ending) != 0 && Volatile.Read(ref _threadStarted) != 0)
                return true;

            // A task that waits for a synchronous operation blocks the thread: the queued tasks cannot run until it ends
            if (Volatile.Read(ref _runningTask) > 0)
                return Volatile.Read(ref _blockingWaits) == 0;

            lock (_tasksLock)
            {
                return _tasks.Count > 0;
            }
        }
    }

    /// <summary>
    /// Registers an operation of a worker that does not wait for a timer (e.g. the fetch of its script): the worker is
    /// considered running (see <see cref="HasRunningWorkers"/>) until <see cref="EndRunningOperation"/>.
    /// </summary>
    internal void BeginRunningOperation()
    {
        Interlocked.Increment(ref _runningOperations);
        BeginOperation();
    }

    internal void EndRunningOperation()
    {
        OnStoppingRunning();
        Interlocked.Decrement(ref _runningOperations);
        EndOperation();
        if (_isOperationTracker)
        {
            MarkIdleIfNoWork();
        }
    }

    /// <summary>Returns the time until the next timer of the workers of the event loop (zero when a timer is due), or null.</summary>
    private TimeSpan? GetTimeUntilNextChildTimer()
    {
        DomEventLoop[] children;
        lock (_childLoops)
        {
            children = [.. _childLoops];
        }

        TimeSpan? result = null;
        foreach (var child in children)
        {
            result = Min(result, Min(child.GetTimeUntilNextOwnTimer(), child.GetTimeUntilNextChildTimer()));
        }

        return result;
    }

    /// <summary>
    /// Marks the event loop of a worker as busy: its owner considers that an operation is in progress until the worker is
    /// idle. Must be called while holding <c>_tasksLock</c>.
    /// </summary>
    private void MarkBusyLocked()
    {
        if (Owner is null || _busy || _isDisposed)
            return;

        _busy = true;
        Owner.BeginOperation();
    }

    /// <summary>Releases the operation of the owner when the worker has no task to run and no operation in progress.</summary>
    private void MarkIdleIfNoWork()
    {
        lock (_tasksLock)
        {
            if (!_busy || _tasks.Count > 0 || Volatile.Read(ref _pendingOperations) > 0)
                return;

            _busy = false;
        }

        Owner!.EndOperation();
    }

    private void ReleaseOwner()
    {
        if (Owner is not { } owner)
            return;

        bool wasBusy;
        lock (_tasksLock)
        {
            wasBusy = _busy;
            _busy = false;
        }

        lock (owner._childLoops)
        {
            owner._childLoops.Remove(this);
        }

        if (wasBusy)
        {
            owner.EndOperation();
        }
    }

    private void DisposeChildLoops()
    {
        DomEventLoop[] children;
        lock (_childLoops)
        {
            children = [.. _childLoops];
            _childLoops.Clear();
        }

        foreach (var child in children)
        {
            child.OnOwnerDisposed?.Invoke();
        }
    }

    /// <summary>Wakes up the thread of the worker (a task or a timer was queued, or the worker is terminated).</summary>
    internal void SignalWork() => _workerSignal?.Set();

    /// <summary>Called on the thread of a worker when it starts.</summary>
    internal void OnWorkerThreadStarted() => Volatile.Write(ref _threadStarted, 1);

    /// <summary>
    /// Called when the worker is terminated or its event loop ends: a host that advances a fake clock waits until its thread
    /// has cleaned up, as the event loop is disposed at the end of the cleanup.
    /// </summary>
    internal void OnWorkerEnding() => Volatile.Write(ref _ending, 1);

    /// <summary>
    /// Marks the task that runs on the current thread (of a worker) as waiting for a synchronous operation (e.g. a
    /// synchronous fetch or <c>importScripts()</c>) until <paramref name="operation"/> completes, <paramref name="cancellationToken"/>
    /// is canceled or the returned scope is disposed: a host that advances a fake clock may advance it while the worker
    /// waits. The wait ends synchronously when the operation completes (on the thread that completes it), so that the host
    /// does not advance the clock again before the thread of the worker resumes.
    /// </summary>
    internal static BlockingWait EnterBlockingWait(Task operation, CancellationToken cancellationToken)
    {
        var wait = new BlockingWait(s_currentWorkerLoop);
        if (wait.Loop is not null)
        {
            _ = operation.ContinueWith(static (_, state) => ((BlockingWait)state!).Dispose(), wait, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            wait.Registration = cancellationToken.Register(static state => ((BlockingWait)state!).Dispose(), wait);
        }

        return wait;
    }

    internal sealed class BlockingWait : IDisposable
    {
        private int _ended;

        public BlockingWait(DomEventLoop? loop)
        {
            Loop = loop;
            if (loop is not null)
            {
                loop.OnStoppingRunning();
                Interlocked.Increment(ref loop._blockingWaits);
            }
        }

        public DomEventLoop? Loop { get; }

        public CancellationTokenRegistration Registration { get; set; }

        public void Dispose()
        {
            if (Loop is not null && Interlocked.Exchange(ref _ended, 1) == 0)
            {
                Interlocked.Decrement(ref Loop._blockingWaits);
            }
        }
    }

    /// <summary>
    /// Runs the event loop of a worker on the current thread until <paramref name="cancellationToken"/> is canceled (the
    /// worker is terminated), the event loop is disposed, or <paramref name="shouldStop"/> returns true when the worker is
    /// idle (e.g. a closing worker that has no more tasks).
    /// </summary>
    internal void RunWorker(Func<bool> shouldStop, CancellationToken cancellationToken)
    {
        s_currentWorkerLoop = this;
        try
        {
            while (!cancellationToken.IsCancellationRequested && !IsDisposed)
            {
                Interlocked.Increment(ref _runningTask);
                try
                {
                    PerformMicrotaskCheckpoint();
                }
                finally
                {
                    OnStoppingRunning();
                    Interlocked.Decrement(ref _runningTask);
                }

                var delay = PromoteOwnDueTimers();
                while (!cancellationToken.IsCancellationRequested && RunWorkerTask())
                {
                    delay = PromoteOwnDueTimers();
                }

                if (cancellationToken.IsCancellationRequested || IsDisposed)
                    return;

                // The signal is reset before checking the queue: a task queued afterwards sets it
                var signal = _workerSignal!;
                signal.Reset();
                lock (_tasksLock)
                {
                    if (_tasks.Count > 0)
                        continue;
                }

                MarkIdleIfNoWork();
                if (shouldStop())
                    return;

                try
                {
                    // The timer of the clock (a fake clock fires it when it is advanced, on the thread that advances it). The
                    // timers of another clock are also checked periodically: the time of a fake clock may also advance
                    // without firing its timers (e.g. FakeTimeProvider.AutoAdvanceAmount)
                    using var timer = delay is { } next ? TimeProvider.CreateTimer(static state => ((ManualResetEventSlim)state!).Set(), signal, next < TimeSpan.Zero ? TimeSpan.Zero : next, Timeout.InfiniteTimeSpan) : null;
                    var timeout = delay is not null && TimeProvider != TimeProvider.System ? FakeClockPollingInterval : Timeout.InfiniteTimeSpan;
                    signal.Wait(timeout, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
        finally
        {
            s_currentWorkerLoop = null;
        }
    }

    private bool RunWorkerTask()
    {
        Interlocked.Increment(ref _runningTask);
        try
        {
            return RunTask();
        }
        finally
        {
            OnStoppingRunning();
            Interlocked.Decrement(ref _runningTask);
        }
    }
}
