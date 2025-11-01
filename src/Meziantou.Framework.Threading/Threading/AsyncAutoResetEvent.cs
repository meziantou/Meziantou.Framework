using System.Diagnostics;

namespace Meziantou.Framework.Threading;

/// <summary>Represents an asynchronous auto-reset event that signals a single waiting task when set.</summary>
/// <example>
/// <code><![CDATA[
/// var autoResetEvent = new AsyncAutoResetEvent(initialState: false);
/// 
/// // In one task
/// await autoResetEvent.WaitAsync();
/// Console.WriteLine("Event was set!");
/// 
/// // In another task
/// autoResetEvent.Set();
/// ]]></code>
/// </example>
[DebuggerDisplay("Signaled: {_signaled}")]
public sealed class AsyncAutoResetEvent
{
    private readonly Queue<WaiterCompletionSource> _signalAwaiters = new();
    private readonly bool _allowInliningAwaiters;
    internal readonly Action<object> _onCancellationRequestHandler;
    private bool _signaled;

    /// <summary>Initializes a new instance of the <see cref="AsyncAutoResetEvent"/> class with a Boolean value indicating whether to set the initial state to signaled.</summary>
    /// <param name="initialState"><see langword="true"/> to set the initial state to signaled; <see langword="false"/> to set the initial state to non-signaled.</param>
    public AsyncAutoResetEvent(bool initialState)
        : this(initialState, allowInliningAwaiters: false)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="AsyncAutoResetEvent"/> class with a Boolean value indicating whether to set the initial state to signaled and whether to allow inlining of continuations.</summary>
    /// <param name="initialState"><see langword="true"/> to set the initial state to signaled; <see langword="false"/> to set the initial state to non-signaled.</param>
    /// <param name="allowInliningAwaiters"><see langword="true"/> to allow continuations to be executed synchronously on the thread that calls <see cref="Set"/>; <see langword="false"/> to execute continuations asynchronously.</param>
    public AsyncAutoResetEvent(bool initialState, bool allowInliningAwaiters)
    {
        _signaled = initialState;
        _allowInliningAwaiters = allowInliningAwaiters;
        _onCancellationRequestHandler = OnCancellationRequest;
    }

    /// <summary>Asynchronously waits for the event to be set.</summary>
    /// <returns>A task that completes when the event is set.</returns>
    public Task WaitAsync()
    {
        return WaitAsync(CancellationToken.None);
    }

    /// <summary>Asynchronously waits for the event to be set.</summary>
    /// <param name="cancellationToken">A cancellation token to observe while waiting.</param>
    /// <returns>A task that completes when the event is set.</returns>
    public Task WaitAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled(cancellationToken);

        lock (_signalAwaiters)
        {
            if (_signaled)
            {
                _signaled = false;
                return Task.CompletedTask;
            }
            else
            {
                var waiter = new WaiterCompletionSource(this, _allowInliningAwaiters, cancellationToken);
                if (cancellationToken.IsCancellationRequested)
                {
                    waiter.TrySetCanceled(cancellationToken);
                }
                else
                {
                    _signalAwaiters.Enqueue(waiter);
                }

                return waiter.Task;
            }
        }
    }

    /// <summary>Sets the state of the event to signaled, allowing one waiting task to proceed.</summary>
    public void Set()
    {
        WaiterCompletionSource? toRelease = null;
        lock (_signalAwaiters)
        {
            if (_signalAwaiters.Count > 0)
            {
                toRelease = _signalAwaiters.Dequeue();
            }
            else if (!_signaled)
            {
                _signaled = true;
            }
        }

        if (toRelease is not null)
        {
            toRelease.Registration.Dispose();
            toRelease.TrySetResult();
        }
    }

    private void OnCancellationRequest(object state)
    {
        var tcs = (WaiterCompletionSource)state;
        bool removed;
        lock (_signalAwaiters)
        {
            removed = RemoveMidQueue(_signalAwaiters, tcs);
        }

        // We only cancel the task if we removed it from the queue.
        // If it wasn't in the queue, either it has already been signaled
        // or it hasn't even been added to the queue yet. If the latter,
        // the Task will be canceled later so long as the signal hasn't been awarded
        // to this Task yet.
        if (removed)
        {
            tcs.TrySetCanceled(tcs.CancellationToken);
        }
    }

    private static bool RemoveMidQueue<T>(Queue<T> queue, T valueToRemove)
        where T : class
    {
        var originalCount = queue.Count;
        var dequeueCounter = 0;
        var found = false;
        while (dequeueCounter < originalCount)
        {
            dequeueCounter++;
            var dequeued = queue.Dequeue();
            if (!found && dequeued == valueToRemove)
            { // only find 1 match
                found = true;
            }
            else
            {
                queue.Enqueue(dequeued);
            }
        }

        return found;
    }

    private sealed class WaiterCompletionSource : TaskCompletionSource
    {
        internal WaiterCompletionSource(AsyncAutoResetEvent owner, bool allowInliningContinuations, CancellationToken cancellationToken)
            : base(GetOptions(allowInliningContinuations))
        {
            CancellationToken = cancellationToken;
            Registration = cancellationToken.Register(owner._onCancellationRequestHandler!, this);
        }

        internal CancellationToken CancellationToken { get; }
        internal CancellationTokenRegistration Registration { get; }

        private static TaskCreationOptions GetOptions(bool allowInliningContinuations)
        {
            return allowInliningContinuations ? TaskCreationOptions.None : TaskCreationOptions.RunContinuationsAsynchronously;
        }
    }
}
