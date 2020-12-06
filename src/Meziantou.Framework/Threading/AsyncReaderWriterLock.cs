using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Meziantou.Framework.Threading
{
    public sealed class AsyncReaderWriterLock
    {
        private readonly Task<Releaser> _readerReleaser;
        private readonly Task<Releaser> _writerReleaser;

        private readonly Queue<TaskCompletionSource<Releaser>> _waitingWriters = new();
        private TaskCompletionSource<Releaser> _waitingReader = new();
        private int _readersWaiting;
        private int _status;

        public AsyncReaderWriterLock()
        {
            _readerReleaser = Task.FromResult(new Releaser(this, writer: false));
            _writerReleaser = Task.FromResult(new Releaser(this, writer: true));
        }

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

            if (toWake != null)
            {
                toWake.SetResult(new Releaser(this, writer: true));
            }
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

            if (toWake != null)
            {
                toWake.SetResult(new Releaser(this, toWakeIsWriter));
            }
        }

        [StructLayout(LayoutKind.Auto)]
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
                if (_toRelease != null)
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
}
