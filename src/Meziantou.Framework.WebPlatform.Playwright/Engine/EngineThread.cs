using System.Collections.Concurrent;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Playwright.Engine;

/// <summary>
/// The thread of the browsing contexts of a browser context: the browsing contexts and the script engine are not thread-safe,
/// so they are used from this thread only. It runs the work posted by the protocol (with its own synchronization context:
/// the continuations of the asynchronous operations run on it), and the tasks and the timers of the event loops of the pages.
/// </summary>
internal sealed class EngineThread : IDisposable
{
    // The number of tasks of an event loop run before the posted work runs again (a page that queues tasks endlessly does
    // not block the protocol)
    private const int TasksPerIteration = 64;

    private readonly Thread _thread;
    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _work = new();
#pragma warning disable CA2213 // A continuation can signal the event after the thread is stopped: it is not disposed
    private readonly AutoResetEvent _signal = new(initialState: false);
#pragma warning restore CA2213
    private readonly List<DomEventLoop> _eventLoops = [];
    private readonly EngineSynchronizationContext _synchronizationContext;
    private volatile bool _stopped;

    public EngineThread(string name)
    {
        _synchronizationContext = new EngineSynchronizationContext(this);
        _thread = new Thread(Run, maxStackSize: 16 * 1024 * 1024)
        {
            IsBackground = true,
            Name = name,
        };
        _thread.Start();
    }

    public bool IsCurrentThread => Environment.CurrentManagedThreadId == _thread.ManagedThreadId;

    /// <summary>Gets the synchronization context of the thread.</summary>
    public SynchronizationContext SynchronizationContext => _synchronizationContext;

    /// <summary>Wakes the thread up (new work, or a task queued in an event loop from another thread).</summary>
    public void Signal() => _signal.Set();

    public void Post(SendOrPostCallback callback, object? state)
    {
        // Once the thread is stopped, the continuations run where they are posted: the browsing contexts are disposed, so they
        // complete (or fail) at once instead of waiting forever
        if (_stopped)
        {
            Execute(callback, state);
            return;
        }

        _work.Enqueue((callback, state));
        _signal.Set();
    }

    /// <summary>Runs a function on the thread.</summary>
    public Task<T> InvokeAsync<T>(Func<T> function)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(_ =>
        {
            try
            {
                completion.TrySetResult(function());
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }, state: null);
        return completion.Task;
    }

    /// <summary>Runs an action on the thread.</summary>
    public Task InvokeAsync(Action action) => InvokeAsync(() =>
    {
        action();
        return true;
    });

    /// <summary>Runs an asynchronous function on the thread: its continuations run on the thread.</summary>
    public Task<T> InvokeAsync<T>(Func<Task<T>> function)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(_ => _ = RunAsync(), state: null);
        return completion.Task;

        async Task RunAsync()
        {
            try
            {
                completion.TrySetResult(await function().ConfigureAwait(true));
            }
            catch (Exception ex)
            {
                completion.TrySetException(ex);
            }
        }
    }

    /// <summary>Runs an asynchronous action on the thread: its continuations run on the thread.</summary>
    public Task InvokeAsync(Func<Task> function) => InvokeAsync(async () =>
    {
        await function().ConfigureAwait(true);
        return true;
    });

    /// <summary>Runs the tasks and the timers of an event loop on the thread (called on the thread).</summary>
    public void AddEventLoop(DomEventLoop eventLoop)
    {
        if (!_eventLoops.Contains(eventLoop))
        {
            _eventLoops.Add(eventLoop);
            eventLoop.WorkSignal = Signal;
        }
    }

    private void Run()
    {
        SynchronizationContext.SetSynchronizationContext(_synchronizationContext);
        while (!_stopped)
        {
            var didWork = false;

            // The work posted before this iteration (the work posted by the work runs at the next iteration)
            for (var count = _work.Count; count > 0 && _work.TryDequeue(out var item); count--)
            {
                Execute(item.Callback, item.State);
                didWork = true;
            }

            TimeSpan? nextTimer = null;
            for (var i = _eventLoops.Count - 1; i >= 0; i--)
            {
                var eventLoop = _eventLoops[i];
                if (eventLoop.IsDisposed)
                {
                    _eventLoops.RemoveAt(i);
                    continue;
                }

                try
                {
                    eventLoop.PerformMicrotaskCheckpoint();
                    var due = eventLoop.PromoteDueTimers();
                    for (var task = 0; task < TasksPerIteration && eventLoop.RunTask(); task++)
                    {
                        didWork = true;
                    }

                    due = Min(due, eventLoop.PromoteDueTimers());
                    nextTimer = Min(nextTimer, due);
                    if (eventLoop.HasPendingWork)
                    {
                        didWork = true;
                    }
                }
#pragma warning disable CA1031 // An exception of a task of a page must not stop the thread of the other pages
                catch (Exception)
#pragma warning restore CA1031
                {
                    didWork = true;
                }
            }

            if (didWork || !_work.IsEmpty)
                continue;

            var timeout = nextTimer is { } delay ? (int)Math.Clamp(Math.Ceiling(delay.TotalMilliseconds), 0, int.MaxValue) : Timeout.Infinite;
            _signal.WaitOne(timeout);
        }

        // The work posted before the thread stopped runs (it fails on the disposed browsing contexts)
        while (_work.TryDequeue(out var remaining))
        {
            Execute(remaining.Callback, remaining.State);
        }
    }

    private static void Execute(SendOrPostCallback callback, object? state)
    {
        try
        {
            callback(state);
        }
#pragma warning disable CA1031 // The continuations report their own exceptions (to their tasks)
        catch (Exception)
#pragma warning restore CA1031
        {
        }
    }

    private static TimeSpan? Min(TimeSpan? a, TimeSpan? b) => a is null ? b : b is null ? a : a.Value < b.Value ? a : b;

    public void Dispose()
    {
        // The event is not disposed: a continuation can still signal it (it is released with the thread)
        _stopped = true;
        _signal.Set();
        if (!IsCurrentThread)
        {
            _thread.Join(TimeSpan.FromSeconds(10));
        }
    }

    private sealed class EngineSynchronizationContext(EngineThread thread) : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => thread.Post(d, state);

        public override void Send(SendOrPostCallback d, object? state)
        {
            if (thread.IsCurrentThread)
            {
                d(state);
                return;
            }

            thread.InvokeAsync(() => d(state)).GetAwaiter().GetResult();
        }

        public override SynchronizationContext CreateCopy() => this;
    }
}
