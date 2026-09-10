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

    // Number of times the lock has been released. Every lease carries the value this counter had while its
    // acquisition was current, and releasing advances it. A lease (or any copy of it) can therefore only
    // release the acquisition it was created for: once that acquisition is over, its value is stale forever.
    private long _releaseCount;

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
                return new ValueTask<AsyncLockLease>(CreateLease());
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
                    lockObject = CreateLease();
                    return true;
                }
            }
        }

        lockObject = new AsyncLockLease();
        return false;
    }

    /// <summary>Creates the lease for the acquisition that is now current. Must be called once the acquisition is
    /// granted, so the lease carries the release count that is only valid while that acquisition lasts.</summary>
    private AsyncLockLease CreateLease()
    {
        return new AsyncLockLease(this, Interlocked.Read(ref _releaseCount));
    }

    /// <summary>Releases the acquisition identified by <paramref name="releaseCount"/>, if it is still the current one.</summary>
    /// <returns><see langword="true"/> if this call released the lock; <see langword="false"/> if the acquisition was already released.</returns>
    internal bool TryRelease(long releaseCount)
    {
        // Only the first release of a given acquisition wins the exchange, so disposing a lease twice, or disposing
        // a copy of an already-disposed lease, cannot release a lock that somebody else acquired in the meantime.
        if (Interlocked.CompareExchange(ref _releaseCount, releaseCount + 1, releaseCount) != releaseCount)
            return false;

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

            // The exchange above is the only one that can have advanced the counter, since the lease handed out here
            // is the only one able to advance it next, so the next acquisition is identified by releaseCount + 1.
            toRelease.TrySetResult(new AsyncLockLease(this, releaseCount + 1));
        }

        return true;
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
    /// <remarks>Only the first disposal of a lease releases the lock. Disposing the same lease again, or disposing a
    /// copy of an already-disposed lease, does nothing instead of releasing an acquisition made in the meantime.</remarks>
    [StructLayout(LayoutKind.Auto)]
    [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "Not meant to be used directly")]
    public readonly struct AsyncLockLease : IDisposable
    {
        private readonly AsyncLock? _parent;
        private readonly long _releaseCount;

        internal AsyncLockLease(AsyncLock? parent, long releaseCount)
        {
            _parent = parent;
            _releaseCount = releaseCount;
        }

        public void Dispose()
        {
            TryRelease();
        }

        /// <summary>Releases the lock unless this lease, or a copy of it, was already disposed.</summary>
        /// <returns><see langword="true"/> if this call released the lock; otherwise, <see langword="false"/>.</returns>
        internal bool TryRelease()
        {
            return _parent?.TryRelease(_releaseCount) is true;
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
