using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Meziantou.Framework.Threading
{
    [DebuggerDisplay("Signaled: {signaled}")]
    public sealed class AsyncAutoResetEvent
    {
        private readonly Queue<WaiterCompletionSource> _signalAwaiters = new Queue<WaiterCompletionSource>();
        private readonly bool _allowInliningAwaiters;
        internal readonly Action<object> _onCancellationRequestHandler;
        private bool _signaled;

        public AsyncAutoResetEvent()
            : this(allowInliningAwaiters: false)
        {
        }

        public AsyncAutoResetEvent(bool allowInliningAwaiters)
        {
            _allowInliningAwaiters = allowInliningAwaiters;
            _onCancellationRequestHandler = OnCancellationRequest;
        }

        public Task WaitAsync()
        {
            return WaitAsync(CancellationToken.None);
        }

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

            if (toRelease is object)
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
}
