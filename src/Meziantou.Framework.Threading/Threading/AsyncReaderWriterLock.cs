using System.Runtime.InteropServices;

namespace Meziantou.Framework.Threading;

/// <summary>Provides an asynchronous reader-writer lock that allows multiple readers or a single writer.</summary>
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

    private readonly Queue<TaskCompletionSource<Releaser>> _waitingWriters = new();
    private TaskCompletionSource<Releaser> _waitingReader = new();
    private int _readersWaiting;
    private int _status;

    /// <summary>Initializes a new instance of the <see cref="AsyncReaderWriterLock"/> class.</summary>
    public AsyncReaderWriterLock()
    {
        _readerReleaser = Task.FromResult(new Releaser(this, writer: false));
        _writerReleaser = Task.FromResult(new Releaser(this, writer: true));
    }

    /// <summary>Asynchronously acquires the reader lock. Multiple readers can hold the lock simultaneously.</summary>
    /// <returns>A task that returns a disposable releaser. Disposing the releaser releases the reader lock.</returns>
    public Task<Releaser> ReaderLockAsync()
    {
        lock (_waitingWriters)
        {
            if (_status >= 0 && _waitingWriters.Count == 0)
            {
                _status += 1;
                return _readerReleaser;
            }
            else
            {
                _readersWaiting += 1;
                return _waitingReader.Task;
            }
        }
    }

    /// <summary>Asynchronously acquires the writer lock. Only one writer can hold the lock at a time.</summary>
    /// <returns>A task that returns a disposable releaser. Disposing the releaser releases the writer lock.</returns>
    public Task<Releaser> WriterLockAsync()
    {
        lock (_waitingWriters)
        {
            if (_status == 0)
            {
                _status = -1;
                return _writerReleaser;
            }
            else
            {
                var waiter = new TaskCompletionSource<Releaser>(TaskCreationOptions.RunContinuationsAsynchronously);
                _waitingWriters.Enqueue(waiter);
                return waiter.Task;
            }
        }
    }

    private void ReaderRelease()
    {
        TaskCompletionSource<Releaser>? toWake = null;

        lock (_waitingWriters)
        {
            _status -= 1;
            if (_status == 0 && _waitingWriters.Count > 0)
            {
                _status = -1;
                toWake = _waitingWriters.Dequeue();
            }
        }

        toWake?.SetResult(new Releaser(this, writer: true));
    }

    private void WriterRelease()
    {
        TaskCompletionSource<Releaser>? toWake = null;
        var toWakeIsWriter = false;

        lock (_waitingWriters)
        {
            if (_waitingWriters.Count > 0)
            {
                toWake = _waitingWriters.Dequeue();
                toWakeIsWriter = true;
            }
            else if (_readersWaiting > 0)
            {
                toWake = _waitingReader;
                _status = _readersWaiting;
                _readersWaiting = 0;
                _waitingReader = new TaskCompletionSource<Releaser>();
            }
            else
            {
                _status = 0;
            }
        }

        toWake?.SetResult(new Releaser(this, toWakeIsWriter));
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
}
