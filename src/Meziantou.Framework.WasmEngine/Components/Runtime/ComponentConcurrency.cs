using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.Versioning;

namespace Meziantou.Framework.WasmEngine.Components.Runtime;

/// <summary>
/// The component model state of a store (canonical ABI "Store"): the waiting threads, the lock down after a trap, the
/// operations of the host that complete later, and the workers that run the threads of the asynchronous calls.
/// </summary>
internal sealed class ComponentStoreState : IDisposable
{
    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _posted = new();
    private readonly SemaphoreSlim _postedSignal = new(0);
    private readonly ConcurrentBag<ContinuationWorker> _idleWorkers = [];
    private readonly HashSet<Continuation> _liveContinuations = [];
    private int _pendingHostOperations;
    private bool _disposed;

    public ComponentStoreState(ManagedStore store)
    {
        Store = store;
        SynchronizationContext = new ComponentSynchronizationContext(this);
    }

    public ManagedStore Store { get; }

    /// <summary>The threads waiting for a condition, in the order they started to wait.</summary>
    public List<ComponentThread> Waiting { get; } = [];

    public int NestingDepth { get; set; }

    public bool IsLockedDown { get; private set; }

    /// <summary>The synchronization context of the host code that runs between the calls of the components: its continuations run on the thread of the store.</summary>
    public ComponentSynchronizationContext SynchronizationContext { get; }

    public bool HasPendingHostOperations => Volatile.Read(ref _pendingHostOperations) > 0 || !_posted.IsEmpty;

    public void LockDown() => IsLockedDown = true;

    public void ThrowIfLockedDown()
    {
        if (IsLockedDown)
            throw new WasmTrapException("the store is locked down: a component trapped");
    }

    public void BeginHostOperation() => Interlocked.Increment(ref _pendingHostOperations);

    public void EndHostOperation() => Interlocked.Decrement(ref _pendingHostOperations);

    public void Post(SendOrPostCallback callback, object? state)
    {
        if (_disposed)
            return;

        _posted.Enqueue((callback, state));
        try
        {
            _postedSignal.Release();
        }
        catch (ObjectDisposedException)
        {
            // The store is disposed: nothing waits for the callback anymore
        }
    }

    /// <summary>Runs the callbacks posted by the host operations that completed: returns whether one ran.</summary>
    public bool RunPosted()
    {
        var ran = false;
        while (_posted.TryDequeue(out var item))
        {
            ran = true;
            var previous = System.Threading.SynchronizationContext.Current;
            System.Threading.SynchronizationContext.SetSynchronizationContext(SynchronizationContext);
            try
            {
                item.Callback(item.State);
            }
            finally
            {
                System.Threading.SynchronizationContext.SetSynchronizationContext(previous);
            }
        }

        return ran;
    }

    /// <summary>Waits until a host operation posts a callback (the callbacks are run by <see cref="RunPosted"/>).</summary>
    [UnsupportedOSPlatform("browser")]
    public void WaitForPosted()
    {
        while (_posted.IsEmpty)
        {
            if (Volatile.Read(ref _pendingHostOperations) <= 0)
                return;

            Store.CheckComponentInterrupt();
            _postedSignal.Wait(TimeSpan.FromMilliseconds(50));
        }
    }

    /// <summary>
    /// Runs the waiting threads that are ready and the host callbacks until <paramref name="done"/> is true: the
    /// candidates are the threads of <paramref name="instance"/>, or all the waiting threads of the store when null.
    /// </summary>
    public void RunUntil(Func<bool> done, ComponentInstanceState? instance)
    {
        while (!done())
        {
            if (RunPosted())
                continue;

            var candidate = FindReadyThread(instance);
            if (candidate is not null)
            {
                candidate.Resume();
                continue;
            }

            if (HasPendingHostOperations)
            {
                if (OperatingSystem.IsBrowser())
                    throw new PlatformNotSupportedException("The asynchronous operations of the host cannot be awaited synchronously on the browser");

                WaitForPosted();
                continue;
            }

            throw ComponentTrap.Create("deadlock detected: the call cannot complete");
        }
    }

    /// <summary>Resumes the continuations parked to let an inline thread lower on another stack run, until none is left.</summary>
    public void RunParked()
    {
        while (Waiting.FirstOrDefault(thread => thread.Parked) is { } thread)
        {
            thread.Resume();
        }
    }

    public ComponentThread? FindReadyThread(ComponentInstanceState? instance)
    {
        foreach (var thread in Waiting)
        {
            if ((instance is null || thread.Task.Instance == instance) && thread.Ready())
                return thread;
        }

        return null;
    }

    [UnsupportedOSPlatform("browser")]
    public void Register(Continuation continuation)
    {
        lock (_liveContinuations)
        {
            _liveContinuations.Add(continuation);
        }
    }

    [UnsupportedOSPlatform("browser")]
    public void Unregister(Continuation continuation)
    {
        lock (_liveContinuations)
        {
            _liveContinuations.Remove(continuation);
        }
    }

    [UnsupportedOSPlatform("browser")]
    public void StartOnWorker(Action body)
    {
        if (!_idleWorkers.TryTake(out var worker))
        {
            worker = new ContinuationWorker(this);
        }

        worker.Start(body);
    }

    [UnsupportedOSPlatform("browser")]
    public void ReturnWorker(ContinuationWorker worker)
    {
        if (_disposed)
        {
            worker.Stop();
            return;
        }

        _idleWorkers.Add(worker);
    }

    /// <summary>Releases the threads that are blocked: their calls end with an exception that does not reach the host.</summary>
    public void Dispose()
    {
        _disposed = true;
        if (!OperatingSystem.IsBrowser())
        {
            Continuation[] continuations;
            lock (_liveContinuations)
            {
                continuations = [.. _liveContinuations];
                _liveContinuations.Clear();
            }

            foreach (var continuation in continuations)
            {
                continuation.Abandon();
            }

            while (_idleWorkers.TryTake(out var worker))
            {
                worker.Stop();
            }
        }

        _postedSignal.Dispose();
    }
}

/// <summary>Posts the continuations of the host code to the store, so that they run on the thread that runs the components.</summary>
internal sealed class ComponentSynchronizationContext(ComponentStoreState store) : SynchronizationContext
{
    public override void Post(SendOrPostCallback d, object? state) => store.Post(d, state);

    public override void Send(SendOrPostCallback d, object? state) => throw new NotSupportedException("The store cannot run a callback synchronously");

    public override SynchronizationContext CreateCopy() => this;
}

/// <summary>An OS thread that runs the bodies of continuations, with its own interpreter for the store.</summary>
[UnsupportedOSPlatform("browser")]
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "The worker ends when its store is disposed: the semaphore is released, and its thread exits")]
internal sealed class ContinuationWorker
{
    private const int StackSize = 16 * 1024 * 1024;

    private readonly ComponentStoreState _store;
    private readonly SemaphoreSlim _signal = new(0);
    private Action? _body;
    private bool _stopped;

    public ContinuationWorker(ComponentStoreState store)
    {
        _store = store;
        var thread = new Thread(Run, StackSize) { IsBackground = true, Name = "WasmEngine component thread" };
        thread.Start();
    }

    public void Start(Action body)
    {
        _body = body;
        _signal.Release();
    }

    public void Stop()
    {
        _stopped = true;
        _signal.Release();
    }

    private void Run()
    {
        var machine = _store.Store.CreateThreadMachine();
        try
        {
            while (true)
            {
                _signal.Wait();
                if (_stopped)
                    return;

                var body = _body!;
                _body = null;
                body();
                _store.ReturnWorker(this);
            }
        }
        finally
        {
            _store.Store.ReleaseThreadMachine(machine);
        }
    }
}

/// <summary>Thrown in the threads of a store that is disposed: it unwinds their calls.</summary>
[SuppressMessage("Design", "CA1064:Exceptions should be public", Justification = "The exception never reaches the host: the threads catch it")]
internal sealed class ContinuationAbandonedException : Exception
{
    public ContinuationAbandonedException()
        : base("The store of the component is disposed")
    {
    }

    public ContinuationAbandonedException(string message)
        : base(message)
    {
    }

    public ContinuationAbandonedException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>A suspended computation (canonical ABI "Continuation"): a body that runs on a worker, and resumes when its lock is released.</summary>
[UnsupportedOSPlatform("browser")]
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "A thread can wait for the semaphore until the store is disposed")]
internal sealed class Continuation
{
    [ThreadStatic]
    private static Handler? s_handler;

    private readonly SemaphoreSlim _lock = new(0);
    private bool _abandoned;

    public Handler? Handler { get; set; }

    /// <summary>The thread whose continuation runs on the current OS thread, or null outside of a continuation.</summary>
    public static ComponentThread? RunningThread => s_handler?.CurrentThread;

    public static Continuation New(ComponentStoreState store, Func<ComponentThread?> body)
    {
        var continuation = new Continuation();
        store.Register(continuation);
        store.StartOnWorker(() =>
        {
            if (!continuation.Acquire())
                return;

            s_handler = continuation.Handler;
            ComponentThread.Current = s_handler!.CurrentThread;
            ComponentThread? result = null;
            Exception? error = null;
            try
            {
                result = body();
            }
            catch (ContinuationAbandonedException)
            {
                return;
            }
#pragma warning disable CA1031 // The exception is rethrown by the thread that resumed the continuation
            catch (Exception ex)
#pragma warning restore CA1031
            {
                error = ex;
            }
            finally
            {
                store.Unregister(continuation);
            }

            var handler = s_handler!;
            s_handler = null;
            ComponentThread.Current = null;
            handler.Continuation = null;
            handler.Result = result;
            handler.Error = error;
            handler.Lock.Release();
        });

        return continuation;
    }

    /// <summary>Resumes a continuation, and returns when it blocks (with its new continuation and the thread it switches to) or ends (null).</summary>
    public static (Continuation? Continuation, ComponentThread? SwitchTo) Resume(Continuation continuation, ComponentThread currentThread)
    {
        var handler = new Handler(currentThread);
        continuation.Handler = handler;
        continuation._lock.Release();
        handler.Lock.Wait();
        if (handler.Error is { } error)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
        }

        return (handler.Continuation, handler.Result);
    }

    /// <summary>Blocks the current continuation: control returns to the thread that resumed it.</summary>
    public static void Block(ComponentStoreState store, ComponentThread? switchTo)
    {
        var continuation = new Continuation();
        store.Register(continuation);
        var handler = s_handler ?? throw new InvalidOperationException("The thread is not running in a continuation");
        handler.Continuation = continuation;
        handler.Result = switchTo;
        handler.Lock.Release();
        if (!continuation.Acquire())
            throw new ContinuationAbandonedException();

        store.Unregister(continuation);
        s_handler = continuation.Handler;
        ComponentThread.Current = s_handler!.CurrentThread;
    }

    private bool Acquire()
    {
        _lock.Wait();
        return !_abandoned;
    }

    public void Abandon()
    {
        _abandoned = true;
        _lock.Release();
    }
}

/// <summary>The link between a resumed continuation and the thread that resumed it.</summary>
internal sealed class Handler(ComponentThread currentThread)
{
    public SemaphoreSlim Lock { get; } = new(0);

    public ComponentThread CurrentThread { get; } = currentThread;

    public Continuation? Continuation { get; set; }

    public ComponentThread? Result { get; set; }

    public Exception? Error { get; set; }
}

/// <summary>
/// A thread of the component model (canonical ABI "Thread"). The thread of a synchronous call runs inline, on the stack of
/// its caller: when it waits, it runs the other ready threads of its instance. The other threads are continuations.
/// </summary>
internal sealed class ComponentThread
{
    [ThreadStatic]
    private static ComponentThread? s_current;

    private Continuation? _continuation;
    private bool _running;
    private bool _exited;
    private bool _inlineBlocked;
    private int _osThreadId;

    private ComponentThread(ComponentTask task, bool inline)
    {
        Task = task;
        IsInline = inline;
    }

    public static ComponentThread? Current
    {
        get => s_current;
        set => s_current = value;
    }

    public ComponentTask Task { get; }

    public bool IsInline { get; }

    public Func<bool>? ReadyFunc { get; private set; }

    public int? Index { get; set; }

    public long[] Storage { get; } = new long[2];

    public bool Cancellable { get; set; }

    private ComponentStoreState Store => Task.Instance.Store;

    /// <summary>Creates a suspended thread whose body runs in a continuation.</summary>
    public static ComponentThread CreateContinuation(ComponentTask task, Action body)
    {
        if (OperatingSystem.IsBrowser())
            throw new PlatformNotSupportedException("The asynchronous functions and the threads of the components are not supported on the browser");

        var thread = new ComponentThread(task, inline: false);
        thread._continuation = Continuation.New(task.Instance.Store, () =>
        {
            body();
            return null;
        });

        return thread;
    }

    /// <summary>Creates a running thread whose body runs inline on the current OS thread.</summary>
    public static ComponentThread CreateInline(ComponentTask task) => new(task, inline: true) { _running = true, _osThreadId = Environment.CurrentManagedThreadId };

    /// <summary>Whether the continuation of the thread blocked to let an inline thread lower on another stack run: it runs before the call of the host returns.</summary>
    public bool Parked { get; private set; }

    public bool Running => _running && !_inlineBlocked;

    public bool Exited => _exited;

    public bool Blocked => !Running && !Exited;

    public bool Suspended => Blocked && ReadyFunc is null;

    public bool Waiting => Blocked && ReadyFunc is not null;

    public bool Ready() => Waiting && ReadyFunc!();

    public void MarkExited()
    {
        _running = false;
        _exited = true;
    }

    public void StartWaiting(Func<bool> readyFunc)
    {
        ReadyFunc = readyFunc;
        if (!IsInline)
        {
            Store.Waiting.Add(this);
        }
    }

    public void StopWaiting()
    {
        ReadyFunc = null;
        if (!IsInline)
        {
            Store.Waiting.Remove(this);
        }
    }

    public void ResumeLater() => StartWaiting(() => true);

    /// <summary>Resumes a blocked thread (and the threads it switches to) until it blocks again or ends.</summary>
    public void Resume()
    {
        if (IsInline)
        {
            // The loop of the inline thread sees that it is ready
            if (!Waiting)
            {
                ResumeLater();
            }

            return;
        }

        if (Waiting)
        {
            StopWaiting();
        }

        Parked = false;
        if (OperatingSystem.IsBrowser())
            throw new PlatformNotSupportedException("The asynchronous functions and the threads of the components are not supported on the browser");

        var thread = this;
        while (thread is not null)
        {
            var continuation = thread._continuation!;
            thread._continuation = null;
            thread._running = true;
            ComponentThread? next;
            try
            {
                (thread._continuation, next) = Continuation.Resume(continuation, thread);
            }
            finally
            {
                thread._running = false;
            }

            if (thread._continuation is null)
            {
                thread._exited = true;
            }

            if (next is { IsInline: true })
            {
                // The inline thread runs on the stack that waits for it: it sees that it is ready when control returns to it
                next.Resume();
                break;
            }

            thread = next;
        }
    }

    private void Block(ComponentThread? switchTo)
    {
        if (IsInline)
        {
            InlineWait(switchTo);
            return;
        }

        if (OperatingSystem.IsBrowser())
            throw new PlatformNotSupportedException("The asynchronous functions and the threads of the components are not supported on the browser");

        Continuation.Block(Store, switchTo);
    }

    /// <summary>Waits, on the stack of the inline thread, until its ready function is true.</summary>
    private void InlineWait(ComponentThread? switchTo)
    {
        // The thread is blocked while the thread it switches to runs: that thread can resume it
        _inlineBlocked = true;
        try
        {
            switchTo?.Resume();
            var store = Store;
            var instance = Task.Instance;
            if (!OperatingSystem.IsBrowser() && switchTo is { IsInline: true } && switchTo._osThreadId != Environment.CurrentManagedThreadId && Continuation.RunningThread is { } running)
            {
                // The thread to switch to waits lower on the stack of another OS thread: the continuation that runs this
                // thread blocks, so that control returns to it. The continuation is resumed later, as a ready thread.
                ParkContinuation(running);
            }

            // A thread that yields lets another ready thread of its instance run first
            var ranOther = switchTo is not null;
            while (true)
            {
                if (ranOther && ReadyFunc is not null && ReadyFunc())
                    break;

                if (store.RunPosted())
                {
                    ranOther = true;
                    continue;
                }

                var candidate = store.FindReadyThread(instance);
                if (candidate is not null && candidate != this)
                {
                    candidate.Resume();
                    ranOther = true;
                    continue;
                }

                if (ReadyFunc is not null && ReadyFunc())
                    break;

                if (store.HasPendingHostOperations)
                {
                    if (OperatingSystem.IsBrowser())
                        throw new PlatformNotSupportedException("The asynchronous operations of the host cannot be awaited synchronously on the browser");

                    store.WaitForPosted();
                    continue;
                }

                throw ComponentTrap.Create("deadlock detected: the synchronous call cannot complete");
            }

            ReadyFunc = null;
        }
        finally
        {
            _inlineBlocked = false;
        }
    }

    private void ParkContinuation(ComponentThread running)
    {
        if (OperatingSystem.IsBrowser())
            throw new PlatformNotSupportedException("The asynchronous functions and the threads of the components are not supported on the browser");

        var current = Current;
        running.Parked = true;
        running.StartWaiting(() => true);
        Continuation.Block(Store, switchTo: null);
        Current = current;
    }

    public void Suspend() => Block(switchTo: null);

    public void WaitUntil(Func<bool> readyFunc)
    {
        StartWaiting(readyFunc);
        Block(switchTo: null);
    }

    public void Yield() => WaitUntil(() => true);

    public void SuspendThenResume(ComponentThread other) => Block(switchTo: other);

    public void YieldThenResume(ComponentThread other)
    {
        StartWaiting(() => true);
        Block(switchTo: other);
    }

    public void SuspendThenPromote(ComponentThread other)
    {
        if (other.Ready())
        {
            other.StopWaiting();
            SuspendThenResume(other);
        }
        else
        {
            Suspend();
        }
    }

    public void YieldThenPromote(ComponentThread other)
    {
        if (other.Ready())
        {
            other.StopWaiting();
            YieldThenResume(other);
        }
        else
        {
            Yield();
        }
    }
}

/// <summary>The canonical options of a lift, a lower or a built-in, resolved to the core definitions of an instance.</summary>
internal sealed class CanonicalOptions
{
    public static CanonicalOptions Default { get; } = new();

    public ComponentStringEncoding StringEncoding { get; init; }

    public WasmMemory? Memory { get; init; }

    public WasmFunction? Realloc { get; init; }

    public WasmFunction? PostReturn { get; init; }

    public bool Async { get; init; }

    public WasmFunction? Callback { get; init; }

    public bool Is64 => Memory?.Type.Is64 ?? false;

    public int PointerSize => Is64 ? 8 : 4;

}

/// <summary>A call of a lifted function (canonical ABI "Task").</summary>
internal sealed class ComponentTask
{
    public enum TaskState
    {
        Initial,
        Started,
        PendingCancel,
        CancelDelivered,
        Resolved,
    }

    public ComponentTask(ComponentFunctionType type, CanonicalOptions options, ComponentInstanceState instance, Func<ComponentValue[]> onStart, Action<ComponentValue[]?> onResolve)
    {
        Type = type;
        Options = options;
        Instance = instance;
        OnStart = onStart;
        OnResolve = onResolve;
    }

    public ComponentFunctionType Type { get; }

    public CanonicalOptions Options { get; }

    public ComponentInstanceState Instance { get; }

    public Func<ComponentValue[]> OnStart { get; }

    public Action<ComponentValue[]?> OnResolve { get; }

    public TaskState State { get; set; }

    public int BorrowCount { get; set; }

    public ComponentThread? ImplicitThread { get; set; }

    public bool NeedsExclusive() => !Options.Async || Options.Callback is not null;

    public bool EnterImplicitThread(ComponentThread thread)
    {
        ImplicitThread = thread;
        if (Type.IsAsync)
        {
            var instance = Instance;
            bool HasBackpressure() => instance.Backpressure > 0 || (NeedsExclusive() && instance.ExclusiveThread is not null);
            if (HasBackpressure() || instance.WaitingToEnter > 0)
            {
                instance.WaitingToEnter++;
                thread.WaitUntil(() => !HasBackpressure());
                instance.WaitingToEnter--;
                if (DeliverPendingCancel())
                {
                    Cancel();
                    return false;
                }
            }

            if (NeedsExclusive())
            {
                instance.ExclusiveThread = thread;
            }
        }

        thread.Index = Instance.Threads.Add(thread);
        return true;
    }

    public void ExitImplicitThread()
    {
        Instance.Threads.Remove(ImplicitThread!.Index!.Value);
        if (Type.IsAsync && NeedsExclusive())
        {
            Instance.ExclusiveThread = null;
        }
    }

    public void RequestCancellation()
    {
        if (State == TaskState.Initial)
        {
            State = TaskState.PendingCancel;
            ImplicitThread!.Resume();
        }
        else if (State == TaskState.Started)
        {
            State = TaskState.PendingCancel;
            if (ImplicitThread!.Cancellable && ImplicitThread.Ready())
            {
                ImplicitThread.Resume();
            }
        }
    }

    public bool HasPendingCancel => State == TaskState.PendingCancel;

    public bool DeliverPendingCancel()
    {
        if (!HasPendingCancel)
            return false;

        State = TaskState.CancelDelivered;
        return true;
    }

    public ComponentValue[] Start()
    {
        State = TaskState.Started;
        return OnStart();
    }

    public void Return(ComponentValue[] result)
    {
        ComponentTrap.ThrowIf(State == TaskState.Resolved, "cannot call task.return more than once");
        ComponentTrap.ThrowIf(BorrowCount > 0, "borrow handles still remain at the end of the call");
        OnResolve(result);
        State = TaskState.Resolved;
    }

    public void Cancel()
    {
        ComponentTrap.ThrowIf(State != TaskState.CancelDelivered, "task.cancel called when the cancellation was not delivered");
        ComponentTrap.ThrowIf(BorrowCount > 0, "borrow handles still remain at the end of the call");
        OnResolve(null);
        State = TaskState.Resolved;
    }
}

internal enum EventCode
{
    None = 0,
    Subtask = 1,
    StreamRead = 2,
    StreamWrite = 3,
    FutureRead = 4,
    FutureWrite = 5,
    TaskCancelled = 6,
}

/// <summary>An object that a thread can wait for (canonical ABI "Waitable").</summary>
internal abstract class Waitable
{
    private Func<(EventCode Code, uint Index, uint Payload)>? _pendingEvent;

    public WaitableSet? Set { get; private set; }

    public bool HasSyncWaiter { get; set; }

    /// <summary>The action called when an event becomes pending: the host operations that wait for the waitable.</summary>
    public Action? OnPendingEvent { get; set; }

    public void SetPendingEvent(Func<(EventCode Code, uint Index, uint Payload)> pendingEvent)
    {
        _pendingEvent = pendingEvent;
        OnPendingEvent?.Invoke();
    }

    public bool HasPendingEvent() => _pendingEvent is not null;

    public bool InWaitableSet => Set is not null;

    public void WaitForPendingEvent()
    {
        HasSyncWaiter = true;
        try
        {
            ComponentThread.Current!.WaitUntil(HasPendingEvent);
        }
        finally
        {
            HasSyncWaiter = false;
        }
    }

    public (EventCode Code, uint Index, uint Payload) GetPendingEvent()
    {
        var pendingEvent = _pendingEvent!;
        _pendingEvent = null;
        return pendingEvent();
    }

    public void Join(WaitableSet? set)
    {
        Set?.Elements.Remove(this);
        Set = set;
        set?.Elements.Add(this);
    }

    public virtual void Drop() => Join(null);
}

/// <summary>A set of waitables (canonical ABI "WaitableSet").</summary>
internal sealed class WaitableSet
{
    public List<Waitable> Elements { get; } = [];

    public int WaitingCount { get; set; }

    public bool HasPendingEvent() => Elements.Exists(element => element.HasPendingEvent());

    public (EventCode Code, uint Index, uint Payload) GetPendingEvent()
    {
        foreach (var element in Elements)
        {
            if (element.HasPendingEvent())
                return element.GetPendingEvent();
        }

        throw new InvalidOperationException("The set has no pending event");
    }

    public (EventCode Code, uint Index, uint Payload) Wait()
    {
        WaitingCount++;
        try
        {
            ComponentThread.Current!.WaitUntil(HasPendingEvent);
        }
        finally
        {
            WaitingCount--;
        }

        return GetPendingEvent();
    }

    public (EventCode Code, uint Index, uint Payload) Poll() => HasPendingEvent() ? GetPendingEvent() : (EventCode.None, 0, 0);

    public (EventCode Code, uint Index, uint Payload) WaitFromCallback()
    {
        var thread = ComponentThread.Current!;
        var instance = thread.Task.Instance;
        WaitingCount++;
        try
        {
            thread.WaitUntil(() => instance.ExclusiveThread is null && (HasPendingEvent() || thread.Task.HasPendingCancel));
        }
        finally
        {
            WaitingCount--;
        }

        return thread.Task.DeliverPendingCancel() ? (EventCode.TaskCancelled, 0, 0) : GetPendingEvent();
    }

    public void Drop()
    {
        ComponentTrap.ThrowIf(Elements.Count > 0, "cannot drop a waitable set with waiters");
        ComponentTrap.ThrowIf(WaitingCount > 0, "cannot drop a waitable set with waiters");
    }
}

/// <summary>The call of a lowered function, seen by the caller (canonical ABI "Subtask").</summary>
internal sealed class Subtask : Waitable
{
    public enum SubtaskState
    {
        Starting = 0,
        Started = 1,
        Returned = 2,
        CancelledBeforeStarted = 3,
        CancelledBeforeReturned = 4,
    }

    private List<ResourceHandle>? _lenders = [];

    public SubtaskState State { get; set; }

    public Action? OnCancel { get; set; }

    public bool CancellationRequested { get; set; }

    public List<WasmValue> FlatResults { get; private set; } = [];

    public bool Resolved() => State is SubtaskState.Returned or SubtaskState.CancelledBeforeStarted or SubtaskState.CancelledBeforeReturned;

    public void AddLender(ResourceHandle handle)
    {
        handle.LendCount++;
        _lenders!.Add(handle);
    }

    public void Resolve(SubtaskState state, List<WasmValue> flatResults)
    {
        State = state;
        FlatResults = flatResults;
    }

    public void DeliverResolve()
    {
        foreach (var handle in _lenders!)
        {
            handle.LendCount--;
        }

        _lenders = null;
    }

    public bool ResolveDelivered() => _lenders is null;

    public override void Drop()
    {
        ComponentTrap.ThrowIf(!ResolveDelivered(), "cannot drop a subtask which has not yet resolved");
        base.Drop();
    }
}

/// <summary>A handle to a resource in the table of an instance (canonical ABI "ResourceHandle").</summary>
internal sealed class ResourceHandle(ComponentResource resource, bool own, ComponentTask? borrowScope)
{
    public ComponentResource Resource { get; } = resource;

    public ComponentResourceType Type => Resource.Type;

    public bool Own { get; } = own;

    public ComponentTask? BorrowScope { get; } = borrowScope;

    public int LendCount { get; set; }
}

/// <summary>An <c>error-context</c> in the table of an instance.</summary>
internal sealed class ErrorContextHandle(ComponentErrorContext errorContext)
{
    public ComponentErrorContext ErrorContext { get; } = errorContext;
}

/// <summary>The state of a component instance (canonical ABI "ComponentInstance").</summary>
internal sealed class ComponentInstanceState(ComponentStoreState store)
{
    public ComponentStoreState Store { get; } = store;

    /// <summary>The handles: resource handles, waitables, waitable sets and error contexts.</summary>
    public ComponentTable<object> Handles { get; } = new();

    public ComponentTable<ComponentThread> Threads { get; } = new();

    public bool MayLeave { get; set; } = true;

    public int Backpressure { get; set; }

    public int WaitingToEnter { get; set; }

    public ComponentThread? ExclusiveThread { get; set; }
}
