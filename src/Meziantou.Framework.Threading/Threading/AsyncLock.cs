using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Threading;

/// <summary>Provides an asynchronous lock that can be awaited to ensure exclusive access to a resource.</summary>
/// <example>
/// <code><![CDATA[
/// var asyncLock = new AsyncLock();
/// 
/// async Task AccessResourceAsync()
/// {
///     using (await asyncLock.LockAsync())
///     {
///         // Critical section - only one task can execute this at a time
///         await DoWorkAsync();
///     }
/// }
/// ]]></code>
/// </example>
[DebuggerDisplay("Signaled: {_signaled}")]
public sealed class AsyncLock
{
    private readonly WaiterQueue<WaiterCompletionSource> _signalAwaiters = new();
    private readonly Lock _lock = new();
    private readonly bool _allowInliningAwaiters;
    private readonly Action<object> _onCancellationRequestHandler;
    private bool _signaled = true;

    /// <summary>Initializes a new instance of the <see cref="AsyncLock"/> class.</summary>
    public AsyncLock()
        : this(allowInliningAwaiters: false)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="AsyncLock"/> class with a Boolean value indicating whether to allow inlining of continuations.</summary>
    /// <param name="allowInliningAwaiters"><see langword="true"/> to allow continuations to be executed synchronously on the thread that releases the lock; <see langword="false"/> to execute continuations asynchronously.</param>
    public AsyncLock(bool allowInliningAwaiters)
    {
        _allowInliningAwaiters = allowInliningAwaiters;
        _onCancellationRequestHandler = OnCancellationRequest;
    }

    /// <summary>Asynchronously acquires the lock.</summary>
    /// <returns>A task that returns a disposable lease. Disposing the lease releases the lock.</returns>
    public ValueTask<AsyncLockLease> LockAsync()
    {
        return LockAsync(CancellationToken.None);
    }

    /// <summary>Asynchronously acquires the lock.</summary>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the lock.</param>
    /// <returns>A task that returns a disposable lease. Disposing the lease releases the lock.</returns>
    public ValueTask<AsyncLockLease> LockAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return ValueTask.FromCanceled<AsyncLockLease>(cancellationToken);

        WaiterCompletionSource waiter;
        bool canceled;
        lock (_lock)
        {
            if (_signaled)
            {
                _signaled = false;
                return new ValueTask<AsyncLockLease>(new AsyncLockLease(this));
            }

            waiter = new WaiterCompletionSource(this, _allowInliningAwaiters, cancellationToken);
            canceled = cancellationToken.IsCancellationRequested;
            if (!canceled)
            {
                _signalAwaiters.Enqueue(waiter);
            }
        }

        // The token was canceled between the registration and this check, so the waiter was never
        // queued and must be completed here. This has to happen outside the lock: TrySetCanceled can
        // inline continuations, and Registration.Dispose blocks until a callback running on another
        // thread completes. That callback is OnCancellationRequest, which takes this same lock.
        if (canceled)
        {
            waiter.TrySetCanceled(cancellationToken);
            waiter.Registration.Dispose();
        }

        return new ValueTask<AsyncLockLease>(waiter.Task);
    }

    /// <summary>Attempts to acquire the lock synchronously without blocking.</summary>
    /// <param name="lockObject">When this method returns, contains a disposable lease if the lock was acquired; otherwise, an empty lease.</param>
    /// <returns><see langword="true"/> if the lock was acquired; otherwise, <see langword="false"/>.</returns>
    public bool TryLock(out AsyncLockLease lockObject)
    {
        // Acquire read: _signaled is written under _lock, so an unsynchronized read could otherwise
        // observe a stale value indefinitely and make a caller spinning on TryLock never see a release.
        if (Volatile.Read(ref _signaled))
        {
            lock (_lock)
            {
                if (_signaled)
                {
                    _signaled = false;
                    lockObject = new AsyncLockLease(this);
                    return true;
                }
            }
        }

        lockObject = new AsyncLockLease();
        return false;
    }

    internal void Release()
    {
        WaiterCompletionSource? toRelease;
        lock (_lock)
        {
            toRelease = _signalAwaiters.Dequeue();
            if (toRelease is null && !_signaled)
            {
                _signaled = true;
            }
        }

        if (toRelease is not null)
        {
            toRelease.Registration.Dispose();
            toRelease.TrySetResult(new AsyncLockLease(this));
        }
    }

    private void OnCancellationRequest(object state)
    {
        var tcs = (WaiterCompletionSource)state;
        bool removed;
        lock (_lock)
        {
            removed = _signalAwaiters.Remove(tcs);
        }

        // We only cancel the task if we removed it from the queue.
        // If it wasn't in the queue, either it has already been signaled
        // or it hasn't even been added to the queue yet. If the latter,
        // the Task will be canceled later so long as the signal hasn't been awarded
        // to this Task yet.
        if (removed)
        {
            tcs.TrySetCanceled(tcs.CancellationToken);
            tcs.Registration.Dispose();
        }
    }

    /// <summary>Represents a disposable lease for an <see cref="AsyncLock"/>. Disposing the lease releases the lock.</summary>
    [StructLayout(LayoutKind.Auto)]
    [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "Not meant to be used directly")]
    public readonly struct AsyncLockLease : IDisposable
    {
        private readonly AsyncLock? _parent;

        internal AsyncLockLease(AsyncLock? parent)
        {
            _parent = parent;
        }

        public void Dispose()
        {
            _parent?.Release();
        }
    }

    private sealed class WaiterCompletionSource : TaskCompletionSource<AsyncLockLease>, IWaiterQueueNode<WaiterCompletionSource>
    {
        internal WaiterCompletionSource(AsyncLock owner, bool allowInliningContinuations, CancellationToken cancellationToken)
            : base(GetOptions(allowInliningContinuations))
        {
            CancellationToken = cancellationToken;
            Registration = cancellationToken.Register(owner._onCancellationRequestHandler!, this);
        }

        internal CancellationToken CancellationToken { get; }
        internal CancellationTokenRegistration Registration { get; }

        public WaiterCompletionSource? Previous { get; set; }
        public WaiterCompletionSource? Next { get; set; }
        public bool IsQueued { get; set; }

        private static TaskCreationOptions GetOptions(bool allowInliningContinuations)
        {
            return allowInliningContinuations ? TaskCreationOptions.None : TaskCreationOptions.RunContinuationsAsynchronously;
        }
    }
}
