using Meziantou.Framework.WebPlatform.Dom;
using Microsoft.AspNetCore.Components;

namespace Meziantou.AspNetCore.Testing.Components.Internals;

/// <summary>
/// The dispatcher of the renderer: the work items and the continuations of the components run as tasks of the event loop
/// of the browsing context, so the components and the DOM are only accessed by the thread that runs the event loop (the
/// test). The work items run inline when the caller already runs on the event loop.
/// </summary>
internal sealed class EventLoopDispatcher : Dispatcher
{
    [ThreadStatic]
    private static EventLoopDispatcher? s_current;

    private readonly DomEventLoop _eventLoop;
    private readonly EventLoopSynchronizationContext _synchronizationContext;

    public EventLoopDispatcher(DomEventLoop eventLoop)
    {
        _eventLoop = eventLoop;
        _synchronizationContext = new EventLoopSynchronizationContext(this);
    }

    public override bool CheckAccess() => s_current == this;

    /// <summary>Runs the caller on the event loop until the scope is disposed.</summary>
    public Scope Enter()
    {
        var previousDispatcher = s_current;
        var previousContext = SynchronizationContext.Current;
        s_current = this;
        SynchronizationContext.SetSynchronizationContext(_synchronizationContext);
        return new Scope(previousDispatcher, previousContext);
    }

    public void Post(Action action) => _eventLoop.QueueTask(() =>
    {
        using (Enter())
        {
            action();
        }
    });

    public override Task InvokeAsync(Action workItem)
    {
        if (CheckAccess())
        {
            try
            {
                workItem();
                return Task.CompletedTask;
            }
            catch (Exception ex)
            {
                return Task.FromException(ex);
            }
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            try
            {
                workItem();
                tcs.TrySetResult();
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });
        return tcs.Task;
    }

    public override Task InvokeAsync(Func<Task> workItem)
    {
        if (CheckAccess())
        {
            try
            {
                return workItem();
            }
            catch (Exception ex)
            {
                return Task.FromException(ex);
            }
        }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            try
            {
                var task = workItem();
                _ = task.ContinueWith(t => CopyResult(t, tcs), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });
        return tcs.Task;
    }

    public override Task<TResult> InvokeAsync<TResult>(Func<TResult> workItem)
    {
        if (CheckAccess())
        {
            try
            {
                return Task.FromResult(workItem());
            }
            catch (Exception ex)
            {
                return Task.FromException<TResult>(ex);
            }
        }

        var tcs = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            try
            {
                tcs.TrySetResult(workItem());
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });
        return tcs.Task;
    }

    public override Task<TResult> InvokeAsync<TResult>(Func<Task<TResult>> workItem)
    {
        if (CheckAccess())
        {
            try
            {
                return workItem();
            }
            catch (Exception ex)
            {
                return Task.FromException<TResult>(ex);
            }
        }

        var tcs = new TaskCompletionSource<TResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        Post(() =>
        {
            try
            {
                var task = workItem();
                _ = task.ContinueWith(t =>
                {
                    if (t.IsCanceled)
                    {
                        tcs.TrySetCanceled();
                    }
                    else if (t.Exception is { } exception)
                    {
                        tcs.TrySetException(exception.InnerExceptions);
                    }
                    else
                    {
                        tcs.TrySetResult(t.Result);
                    }
                }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            }
            catch (Exception ex)
            {
                tcs.TrySetException(ex);
            }
        });
        return tcs.Task;
    }

    private static void CopyResult(Task task, TaskCompletionSource tcs)
    {
        if (task.IsCanceled)
        {
            tcs.TrySetCanceled();
        }
        else if (task.Exception is { } exception)
        {
            tcs.TrySetException(exception.InnerExceptions);
        }
        else
        {
            tcs.TrySetResult();
        }
    }

    internal readonly struct Scope(EventLoopDispatcher? previousDispatcher, SynchronizationContext? previousContext) : IDisposable
    {
        public void Dispose()
        {
            s_current = previousDispatcher;
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private sealed class EventLoopSynchronizationContext(EventLoopDispatcher dispatcher) : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state) => dispatcher.Post(() => d(state));

        public override void Send(SendOrPostCallback d, object? state)
        {
            if (dispatcher.CheckAccess())
            {
                d(state);
                return;
            }

            throw new NotSupportedException("Synchronous calls to the event loop of the browsing context are not supported from another thread");
        }

        public override SynchronizationContext CreateCopy() => this;
    }
}
