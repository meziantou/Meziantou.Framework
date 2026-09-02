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

    private readonly Queue<Waiter> _waitingWriters = new();
    private readonly Queue<Waiter> _waitingReaders = new();

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
        if (_status != 0)
            return null;

        if (_waitingWriters.Count > 0)
        {
            _status = -1;
            return [_waitingWriters.Dequeue()];
        }

        if (_waitingReaders.Count > 0)
        {
            // Every queued reader is admitted at once. This also covers the case where the writers that were
            // blocking them have all been canceled, which would otherwise leave the readers queued forever.
            var readers = new List<Waiter>(_waitingReaders.Count);
            while (_waitingReaders.Count > 0)
            {
                readers.Add(_waitingReaders.Dequeue());
            }

            _status = readers.Count;
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
            waiter.TrySetResult(new Releaser(this, waiter.IsWriter));
        }
    }

    private void OnCancellationRequest(object? state)
    {
        var waiter = (Waiter)state!;
        bool removed;
        List<Waiter>? toWake;
        lock (_lock)
        {
            removed = RemoveMidQueue(waiter.IsWriter ? _waitingWriters : _waitingReaders, waiter);

            // Removing a waiter can leave the lock free with others still queued behind it. GrantOwnership is a
            // no-op unless _status is 0, so this only does something when that actually happened.
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

    private static bool RemoveMidQueue(Queue<Waiter> queue, Waiter valueToRemove)
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

    private sealed class Waiter : TaskCompletionSource<Releaser>
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
    }
}
