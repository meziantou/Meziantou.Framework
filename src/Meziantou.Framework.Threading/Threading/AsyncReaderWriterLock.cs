using System.Runtime.InteropServices;

namespace Meziantou.Framework.Threading;

/// <summary>Provides an asynchronous reader-writer lock that allows multiple readers or a single writer.</summary>
/// <remarks>
/// The returned task must be awaited and the resulting <see cref="Releaser"/> disposed, otherwise the lock stays
/// held forever. To give up on an acquisition, pass a <see cref="CancellationToken"/> rather than abandoning the
/// task: a waiter that is never awaited is still granted ownership when its turn comes, and nothing will release it.
/// </remarks>
/// <example>
/// <code><![CDATA[
/// var rwLock = new AsyncReaderWriterLock();
/// 
/// // Multiple readers can execute concurrently
/// async Task ReadAsync()
/// {
///     using (await rwLock.ReaderLockAsync())
///     {
///         // Read data
///     }
/// }
/// 
/// // Only one writer can execute at a time
/// async Task WriteAsync()
/// {
///     using (await rwLock.WriterLockAsync())
///     {
///         // Write data
///     }
/// }
/// ]]></code>
/// </example>
public sealed class AsyncReaderWriterLock
{
    private readonly Task<Releaser> _readerReleaser;
    private readonly Task<Releaser> _writerReleaser;
    private readonly Action<object?> _onCancellationRequestHandler;
    private readonly Lock _lock = new();

    private readonly WaiterQueue<Waiter> _waitingWriters = new();
    private readonly WaiterQueue<Waiter> _waitingReaders = new();

    // 0 when the lock is free, the number of readers holding it when positive, -1 when a writer holds it.
    private int _status;

    /// <summary>Initializes a new instance of the <see cref="AsyncReaderWriterLock"/> class.</summary>
    public AsyncReaderWriterLock()
    {
        _readerReleaser = Task.FromResult(new Releaser(this, writer: false));
        _writerReleaser = Task.FromResult(new Releaser(this, writer: true));
        _onCancellationRequestHandler = OnCancellationRequest;
    }

    /// <summary>Asynchronously acquires the reader lock. Multiple readers can hold the lock simultaneously.</summary>
    /// <returns>A task that returns a disposable releaser. Disposing the releaser releases the reader lock.</returns>
    public Task<Releaser> ReaderLockAsync()
    {
        return ReaderLockAsync(CancellationToken.None);
    }

    /// <summary>Asynchronously acquires the reader lock. Multiple readers can hold the lock simultaneously.</summary>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the lock.</param>
    /// <returns>A task that returns a disposable releaser. Disposing the releaser releases the reader lock.</returns>
    public Task<Releaser> ReaderLockAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<Releaser>(cancellationToken);

        Waiter waiter;
        bool canceled;
        lock (_lock)
        {
            // Queued writers block new readers, otherwise a steady stream of readers would starve them.
            if (_status >= 0 && _waitingWriters.Count == 0)
            {
                _status += 1;
                return _readerReleaser;
            }

            waiter = new Waiter(this, writer: false, cancellationToken);
            canceled = cancellationToken.IsCancellationRequested;
            if (!canceled)
            {
                _waitingReaders.Enqueue(waiter);
            }
        }

        return CompleteIfCanceled(waiter, canceled, cancellationToken);
    }

    /// <summary>Asynchronously acquires the writer lock. Only one writer can hold the lock at a time.</summary>
    /// <returns>A task that returns a disposable releaser. Disposing the releaser releases the writer lock.</returns>
    public Task<Releaser> WriterLockAsync()
    {
        return WriterLockAsync(CancellationToken.None);
    }

    /// <summary>Asynchronously acquires the writer lock. Only one writer can hold the lock at a time.</summary>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the lock.</param>
    /// <returns>A task that returns a disposable releaser. Disposing the releaser releases the writer lock.</returns>
    public Task<Releaser> WriterLockAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled<Releaser>(cancellationToken);

        Waiter waiter;
        bool canceled;
        lock (_lock)
        {
            if (_status == 0)
            {
                _status = -1;
                return _writerReleaser;
            }

            waiter = new Waiter(this, writer: true, cancellationToken);
            canceled = cancellationToken.IsCancellationRequested;
            if (!canceled)
            {
                _waitingWriters.Enqueue(waiter);
            }
        }

        return CompleteIfCanceled(waiter, canceled, cancellationToken);
    }

    private static Task<Releaser> CompleteIfCanceled(Waiter waiter, bool canceled, CancellationToken cancellationToken)
    {
        // The token was canceled between the registration and this check, so the waiter was never queued and must
        // be completed here. This has to happen outside the lock: TrySetCanceled can inline continuations, and
        // Registration.Dispose blocks until a callback running on another thread completes. That callback is
        // OnCancellationRequest, which takes the same lock.
        if (canceled)
        {
            waiter.TrySetCanceled(cancellationToken);
            waiter.Registration.Dispose();
        }

        return waiter.Task;
    }

    private void ReaderRelease()
    {
        GrantedWaiters toWake;
        lock (_lock)
        {
            _status -= 1;
            toWake = GrantOwnership();
        }

        CompleteWaiters(toWake);
    }

    private void WriterRelease()
    {
        GrantedWaiters toWake;
        lock (_lock)
        {
            _status = 0;
            toWake = GrantOwnership();
        }

        CompleteWaiters(toWake);
    }

    /// <summary>Hands the free lock to the next waiters. Must be called while holding <see cref="_lock"/>; the
    /// returned waiters are completed outside it.</summary>
    private GrantedWaiters GrantOwnership()
    {
        // A writer holds the lock, nothing can be granted until it releases.
        if (_status < 0)
            return default;

        if (_waitingWriters.Count > 0)
        {
            // Writers still have priority over the queued readers, so nothing is granted until the lock is free.
            if (_status > 0)
                return default;

            _status = -1;
            return new GrantedWaiters(_waitingWriters.Dequeue()!);
        }

        var readerCount = _waitingReaders.Count;
        if (readerCount > 0)
        {
            // Every queued reader is admitted at once. Readers only ever queue behind a writer, so once no writer
            // is left they can join the readers already holding the lock instead of waiting for those to release.
            // This also covers the case where the writers that were blocking them have all been canceled, which
            // would otherwise leave the readers queued forever.
            _status += readerCount;
            if (readerCount == 1)
                return new GrantedWaiters(_waitingReaders.Dequeue()!);

            var readers = new List<Waiter>(readerCount);
            while (_waitingReaders.Dequeue() is { } reader)
            {
                readers.Add(reader);
            }

            return new GrantedWaiters(readers);
        }

        return default;
    }

    private void CompleteWaiters(GrantedWaiters granted)
    {
        if (granted.Waiter is { } single)
        {
            CompleteWaiter(single);
        }
        else if (granted.Waiters is { } waiters)
        {
            foreach (var waiter in waiters)
            {
                CompleteWaiter(waiter);
            }
        }
    }

    private void CompleteWaiter(Waiter waiter)
    {
        // A waiter that was dequeued here can no longer be canceled: OnCancellationRequest only completes a
        // waiter it removed from the queue itself, so exactly one of the two paths owns it.
        waiter.Registration.Dispose();
        waiter.TrySetResult(new Releaser(this, waiter.IsWriter));
    }

    private void OnCancellationRequest(object? state)
    {
        var waiter = (Waiter)state!;
        bool removed;
        GrantedWaiters toWake;
        lock (_lock)
        {
            removed = (waiter.IsWriter ? _waitingWriters : _waitingReaders).Remove(waiter);

            // Removing a waiter can unblock the ones queued behind it: the lock may now be free, or the canceled
            // writer may have been the last one holding back readers that are compatible with the current owners.
            toWake = removed ? GrantOwnership() : default;
        }

        // Both of these must run outside the lock: Registration.Dispose blocks until a callback running on
        // another thread completes, and that callback is this method, which takes the same lock.
        CompleteWaiters(toWake);

        // We only cancel the task if we removed it from the queue. If it wasn't in the queue, either it has
        // already been granted the lock or it hasn't even been added to the queue yet.
        if (removed)
        {
            waiter.TrySetCanceled(waiter.CancellationToken);
            waiter.Registration.Dispose();
        }
    }

    /// <summary>Represents a disposable releaser for an <see cref="AsyncReaderWriterLock"/>. Disposing the releaser releases either the reader or writer lock.</summary>
    [StructLayout(LayoutKind.Auto)]
    [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "<Pending>")]
    public readonly struct Releaser : IDisposable
    {
        private readonly AsyncReaderWriterLock _toRelease;
        private readonly bool _writer;

        internal Releaser(AsyncReaderWriterLock toRelease, bool writer)
        {
            _toRelease = toRelease;
            _writer = writer;
        }

        public void Dispose()
        {
            if (_toRelease is not null)
            {
                if (_writer)
                {
                    _toRelease.WriterRelease();
                }
                else
                {
                    _toRelease.ReaderRelease();
                }
            }
        }
    }

    /// <summary>The waiters a grant handed the lock to. A single waiter is held inline, so the writer handoffs and
    /// the lone-reader grants that make up the common case cost no list allocation.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly struct GrantedWaiters
    {
        public GrantedWaiters(Waiter waiter)
        {
            Waiter = waiter;
            Waiters = null;
        }

        public GrantedWaiters(List<Waiter> waiters)
        {
            Waiter = null;
            Waiters = waiters;
        }

        /// <summary>The only granted waiter, or <see langword="null"/> when <see cref="Waiters"/> holds them.</summary>
        public Waiter? Waiter { get; }

        /// <summary>The granted waiters when there is more than one, otherwise <see langword="null"/>.</summary>
        public List<Waiter>? Waiters { get; }
    }

    private sealed class Waiter : TaskCompletionSource<Releaser>, IWaiterQueueNode<Waiter>
    {
        internal Waiter(AsyncReaderWriterLock owner, bool writer, CancellationToken cancellationToken)
            : base(TaskCreationOptions.RunContinuationsAsynchronously)
        {
            IsWriter = writer;
            CancellationToken = cancellationToken;
            Registration = cancellationToken.Register(owner._onCancellationRequestHandler, this);
        }

        internal bool IsWriter { get; }
        internal CancellationToken CancellationToken { get; }
        internal CancellationTokenRegistration Registration { get; }

        public Waiter? Previous { get; set; }
        public Waiter? Next { get; set; }
        public bool IsQueued { get; set; }
    }
}
