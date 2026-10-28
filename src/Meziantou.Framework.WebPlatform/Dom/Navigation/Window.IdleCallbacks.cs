using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Dom;

// Cooperative Scheduling of Background Tasks (https://w3c.github.io/requestidlecallback/): the idle callbacks of a window.
// The idle periods are started by the event loop (see DomEventLoop.IdleCallbacks.cs).
public partial class Window
{
    /// <summary>https://w3c.github.io/requestidlecallback/#dfn-list-of-idle-request-callbacks</summary>
    private List<(uint Handle, IdleRequestCallback Callback)>? _idleRequestCallbacks;

    /// <summary>https://w3c.github.io/requestidlecallback/#dfn-list-of-runnable-idle-callbacks</summary>
    private List<(uint Handle, IdleRequestCallback Callback)>? _runnableIdleCallbacks;

    /// <summary>https://w3c.github.io/requestidlecallback/#dfn-idle-callback-identifier</summary>
    private uint _idleCallbackIdentifier;

    /// <summary>Whether the window has idle callbacks that wait for an idle period.</summary>
    internal bool HasIdleCallbacks => _idleRequestCallbacks?.Count > 0 || _runnableIdleCallbacks?.Count > 0;

    /// <summary>
    /// Requests a call of <paramref name="callback"/> in an idle period of the event loop
    /// (https://w3c.github.io/requestidlecallback/#the-requestidlecallback-method): when no task is queued, until the next
    /// timer, the next animation frame, or 50 milliseconds. A callback requested by an idle callback runs in the next idle
    /// period. With a timeout, the callback is queued as a task once the timeout elapsed, if no idle period ran it.
    /// </summary>
    /// <param name="callback">The callback.</param>
    /// <param name="options">The timeout of the callback, or null.</param>
    /// <returns>The handle of the callback, for <see cref="CancelIdleCallback"/>.</returns>
    /// <seealso href="https://w3c.github.io/requestidlecallback/#dom-window-requestidlecallback"/>
    [IdlMember("requestIdleCallback", "https://w3c.github.io/requestidlecallback/#dom-window-requestidlecallback")]
    public uint RequestIdleCallback(IdleRequestCallback callback, IdleRequestOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(callback);
        var handle = ++_idleCallbackIdentifier;
        (_idleRequestCallbacks ??= []).Add((handle, callback));
        EventLoop.OnIdleCallbackRequested(this);

        if (options?.Timeout is > 0 and var timeout)
        {
            // The timeout task is queued on the idle-task task source: it runs only if the scripts of the window can run
            EventLoop.QueueTimer(TimeSpan.FromMilliseconds(timeout), () =>
            {
                if (CanRunScript)
                {
                    InvokeIdleCallbackTimeout(handle);
                }
            });
        }

        return handle;
    }

    /// <summary>Cancels an idle callback (https://w3c.github.io/requestidlecallback/#the-cancelidlecallback-method). Does nothing when the handle is unknown.</summary>
    /// <param name="handle">The handle returned by <see cref="RequestIdleCallback"/>.</param>
    /// <seealso href="https://w3c.github.io/requestidlecallback/#dom-window-cancelidlecallback"/>
    [IdlMember("cancelIdleCallback", "https://w3c.github.io/requestidlecallback/#dom-window-cancelidlecallback")]
    public void CancelIdleCallback(uint handle)
    {
        RemoveIdleCallback(_idleRequestCallbacks, handle);
        RemoveIdleCallback(_runnableIdleCallbacks, handle);
    }

    /// <summary>
    /// https://w3c.github.io/requestidlecallback/#start-an-idle-period-algorithm: the requested callbacks become runnable,
    /// and their invocation is queued. Returns false when there is no callback to run.
    /// </summary>
    internal bool StartIdlePeriod(Func<long> getDeadline)
    {
        if (_idleRequestCallbacks is { Count: > 0 } requested)
        {
            (_runnableIdleCallbacks ??= []).AddRange(requested);
            requested.Clear();
        }

        if (_runnableIdleCallbacks is not { Count: > 0 })
            return false;

        EventLoop.QueueIdleTask(() => InvokeIdleCallbacks(getDeadline));
        return true;
    }

    /// <summary>https://w3c.github.io/requestidlecallback/#invoke-idle-callbacks-algorithm</summary>
    private void InvokeIdleCallbacks(Func<long> getDeadline)
    {
        // The callbacks of a document that is not fully active (e.g. in the back/forward cache) run in an idle period once it is fully active again
        if (!CanRunScript || _runnableIdleCallbacks is not { Count: > 0 } runnable)
            return;

        if (EventLoop.TimeProvider.GetTimestamp() >= getDeadline())
            return;

        var (_, callback) = runnable[0];
        runnable.RemoveAt(0);
        InvokeIdleCallback(callback, new IdleDeadline(this, getDeadline, didTimeout: false));

        if (runnable.Count > 0)
        {
            EventLoop.QueueIdleTask(() => InvokeIdleCallbacks(getDeadline));
        }
    }

    /// <summary>https://w3c.github.io/requestidlecallback/#invoke-idle-callback-timeout-algorithm</summary>
    private void InvokeIdleCallbackTimeout(uint handle)
    {
        var callback = RemoveIdleCallback(_idleRequestCallbacks, handle) ?? RemoveIdleCallback(_runnableIdleCallbacks, handle);
        if (callback is not null)
        {
            InvokeIdleCallback(callback, new IdleDeadline(this, getDeadline: null, didTimeout: true));
        }
    }

    /// <summary>
    /// Calls an idle callback and reports its exception. The callback runs with the <c>background</c> scheduling state
    /// (https://wicg.github.io/scheduling-apis/#sec-patches-requestidlecallback): <c>scheduler.yield()</c> continues with
    /// the <c>background</c> priority.
    /// </summary>
    private void InvokeIdleCallback(IdleRequestCallback callback, IdleDeadline deadline)
    {
        var eventLoop = EventLoop;
        var continuationState = eventLoop.CurrentContinuationState;
        eventLoop.CurrentContinuationState = SchedulerContinuationState.Create(Scheduler, new SchedulingState(AbortSource: null, TaskSignal.CreateFixedPriorityUnabortableSignal(TaskPriority.Background)));
        try
        {
            callback(deadline);
            eventLoop.PerformMicrotaskCheckpoint();
        }
#pragma warning disable CA1031 // The exceptions of the callbacks are reported
        catch (Exception ex)
#pragma warning restore CA1031
        {
            ScriptErrors.Report(ex, this);
        }
        finally
        {
            eventLoop.CurrentContinuationState = continuationState;
        }
    }

    private static IdleRequestCallback? RemoveIdleCallback(List<(uint Handle, IdleRequestCallback Callback)>? callbacks, uint handle)
    {
        if (callbacks is null)
            return null;

        var index = callbacks.FindIndex(item => item.Handle == handle);
        if (index < 0)
            return null;

        var callback = callbacks[index].Callback;
        callbacks.RemoveAt(index);
        return callback;
    }
}
