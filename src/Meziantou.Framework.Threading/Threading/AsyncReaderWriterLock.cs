using System.Runtime.InteropServices;

namespace Meziantou.Framework.Threading;

/// <summary>Provides an asynchronous reader-writer lock that allows multiple readers or a single writer.</summary>
/// <remarks>
/// The returned value task must be awaited and the resulting <see cref="Releaser"/> disposed, otherwise the lock
/// stays held forever. To give up on an acquisition, pass a <see cref="CancellationToken"/> rather than abandoning
/// the value task: a waiter that is never awaited is still granted ownership when its turn comes, and nothing will
/// release it. To start an acquisition and await it later, call
/// <see cref="ValueTask{TResult}.AsTask"/> rather than storing the value task itself.
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
    private readonly Action<object?> _onCancellationRequestHandler;
    private readonly Lock _lock = new();

    private readonly WaiterQueue<Waiter> _waitingWriters = new();
    private readonly WaiterQueue<Waiter> _waitingReaders = new();

    // 0 when the lock is free, the number of readers holding it when positive, -1 when a writer holds it.
    private int _status;

    /// <summary>Initializes a new instance of the <see cref="AsyncReaderWriterLock"/> class.</summary>
    public AsyncReaderWriterLock()
    {
        _onCancellationRequestHandler = OnCancellationRequest;
    }

    /// <summary>Asynchronously acquires the reader lock. Multiple readers can hold the lock simultaneously.</summary>
    /// <returns>A value task that returns a disposable releaser. Disposing the releaser releases the reader lock.</returns>
    public ValueTask<Releaser> ReaderLockAsync()
    {
        return ReaderLockAsync(CancellationToken.None);
    }

    /// <summary>Asynchronously acquires the reader lock. Multiple readers can hold the lock simultaneously.</summary>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the lock.</param>
    /// <returns>A value task that returns a disposable releaser. Disposing the releaser releases the reader lock.</returns>
    public ValueTask<Releaser> ReaderLockAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return ValueTask.FromCanceled<Releaser>(cancellationToken);

        Waiter waiter;
        bool canceled;
        lock (_lock)
        {
            // Queued writers block new readers, otherwise a steady stream of readers would starve them.
            if (_status >= 0 && _waitingWriters.Count == 0)
            {
                _status += 1;
                return new ValueTask<Releaser>(new Releaser(new ReleaseToken(this, writer: false)));
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
    /// <returns>A value task that returns a disposable releaser. Disposing the releaser releases the writer lock.</returns>
    public ValueTask<Releaser> WriterLockAsync()
    {
        return WriterLockAsync(CancellationToken.None);
    }

    /// <summary>Asynchronously acquires the writer lock. Only one writer can hold the lock at a time.</summary>
    /// <param name="cancellationToken">A cancellation token to observe while waiting for the lock.</param>
    /// <returns>A value task that returns a disposable releaser. Disposing the releaser releases the writer lock.</returns>
    public ValueTask<Releaser> WriterLockAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return ValueTask.FromCanceled<Releaser>(cancellationToken);

        Waiter waiter;
        bool canceled;
        lock (_lock)
        {
            if (_status == 0)
            {
                _status = -1;
                return new ValueTask<Releaser>(new Releaser(new ReleaseToken(this, writer: true)));
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

    private static ValueTask<Releaser> CompleteIfCanceled(Waiter waiter, bool canceled, CancellationToken cancellationToken)
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

        return new ValueTask<Releaser>(waiter.Task);
    }

    private void ReaderRelease()
    {
        List<Waiter>? toWake;
        lock (_lock)
        {
            _status -= 1;
            toWake = GrantOwnership();
        }

        CompleteWaiters(toWake);
    }

    private void WriterRelease()
    {
        List<Waiter>? toWake;
        lock (_lock)
        {
            _status = 0;
            toWake = GrantOwnership();
        }

        CompleteWaiters(toWake);
    }

    /// <summary>Hands the free lock to the next waiters. Must be called while holding <see cref="_lock"/>; the
    /// returned waiters are completed outside it.</summary>
    private List<Waiter>? GrantOwnership()
    {
        // A writer holds the lock, nothing can be granted until it releases.
        if (_status < 0)
            return null;

        if (_waitingWriters.Count > 0)
        {
            // Writers still have priority over the queued readers, so nothing is granted until the lock is free.
            if (_status > 0)
                return null;

            _status = -1;
            return [_waitingWriters.Dequeue()!];
        }

        if (_waitingReaders.Count > 0)
        {
            // Every queued reader is admitted at once. Readers only ever queue behind a writer, so once no writer
            // is left they can join the readers already holding the lock instead of waiting for those to release.
            // This also covers the case where the writers that were blocking them have all been canceled, which
            // would otherwise leave the readers queued forever.
            var readers = new List<Waiter>(_waitingReaders.Count);
            while (_waitingReaders.Dequeue() is { } reader)
            {
                readers.Add(reader);
            }

            _status += readers.Count;
            return readers;
        }

        return null;
    }

    private void CompleteWaiters(List<Waiter>? waiters)
    {
        if (waiters is null)
            return;

        foreach (var waiter in waiters)
        {
            // A waiter that was dequeued here can no longer be canceled: OnCancellationRequest only completes a
            // waiter it removed from the queue itself, so exactly one of the two paths owns it.
            waiter.Registration.Dispose();
            waiter.TrySetResult(new Releaser(new ReleaseToken(this, waiter.IsWriter)));
        }
    }

    private void OnCancellationRequest(object? state)
    {
        var waiter = (Waiter)state!;
        bool removed;
        List<Waiter>? toWake;
        lock (_lock)
        {
            removed = (waiter.IsWriter ? _waitingWriters : _waitingReaders).Remove(waiter);

            // Removing a waiter can unblock the ones queued behind it: the lock may now be free, or the canceled
            // writer may have been the last one holding back readers that are compatible with the current owners.
            toWake = removed ? GrantOwnership() : null;
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
    /// <remarks>Only the first disposal of a releaser releases the lock. Disposing the same releaser again, or disposing
    /// a copy of an already-disposed releaser, does nothing instead of releasing an acquisition made in the meantime.</remarks>
    [StructLayout(LayoutKind.Auto)]
    [SuppressMessage("Design", "CA1034:Nested types should not be visible", Justification = "<Pending>")]
    public readonly struct Releaser : IDisposable
    {
        // Identifies the acquisition this releaser was handed out for, and carries everything needed to release it.
        // Copies of the releaser share the token, so only the first of them releases the lock: any later disposal is
        // a no-op instead of releasing an acquisition made in the meantime by somebody else.
        private readonly ReleaseToken? _token;

        internal Releaser(ReleaseToken token)
        {
            _token = token;
        }

        public void Dispose()
        {
            _token?.Release();
        }
    }

    /// <summary>Holds the state of a single acquisition. Shared by every copy of the <see cref="Releaser"/> handed out
    /// for it, so that only one of them can release the lock.</summary>
    internal sealed class ReleaseToken(AsyncReaderWriterLock owner, bool writer)
    {
        private int _released;

        internal void Release()
        {
            if (Interlocked.Exchange(ref _released, 1) != 0)
                return;

            if (writer)
            {
                owner.WriterRelease();
            }
            else
            {
                owner.ReaderRelease();
            }
        }
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
