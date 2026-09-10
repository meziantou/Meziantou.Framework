using System.Collections.Concurrent;
using System.Runtime.ExceptionServices;

namespace Meziantou.Framework.Collections.Concurrent;

/// <summary>
/// The synchronization context used by <see cref="ConcurrentObservableCollection{T}"/> when the thread creating it has none.
/// </summary>
/// <remarks>
/// The base <see cref="SynchronizationContext"/> posts every callback to the thread pool independently, so its callbacks can
/// run concurrently and none of them runs with the context installed. The collection recognizes its own thread by looking at
/// <see cref="SynchronizationContext.Current"/>, so its notifications all appeared to come from a foreign thread and could
/// not read the collection they notify about. This context runs the callbacks on the thread pool too, but one at a time and
/// with itself installed as the current context.
/// </remarks>
internal sealed class SerializedThreadPoolSynchronizationContext : SynchronizationContext
{
    private readonly ConcurrentQueue<(SendOrPostCallback Callback, object? State)> _callbacks = new();

    // Set while a drain is scheduled or running, and cleared only once that drain has no more callbacks to run, so a
    // second drain is never queued while one is in progress.
    private int _isDraining;
    private Thread? _drainingThread;

    public override void Post(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);

        _callbacks.Enqueue((d, state));
        if (TryStartDraining())
        {
            ThreadPool.UnsafeQueueUserWorkItem(static context => context.Drain(), this, preferLocal: false);
        }
    }

    public override void Send(SendOrPostCallback d, object? state)
    {
        ArgumentNullException.ThrowIfNull(d);

        // Running the callback inline on any other thread would break the serialization guarantee, so it goes through
        // the queue like the posted ones. The drain thread itself must run it inline: waiting for the queue it is
        // currently draining would deadlock.
        if (Volatile.Read(ref _drainingThread) == Thread.CurrentThread)
        {
            d(state);
            return;
        }

        using var completed = new ManualResetEventSlim(initialState: false);
        ExceptionDispatchInfo? exception = null;
        Post(_ =>
        {
            try
            {
                d(state);
            }
            catch (Exception ex)
            {
                exception = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                completed.Set();
            }
        }, state: null);

        completed.Wait();
        exception?.Throw();
    }

    // The collection identifies its thread by comparing the context instances, so a copy must stay the same context.
    // Otherwise flowing the context, as an await or SynchronizationContext.CreateCopy does, would hand out an instance
    // the collection considers foreign.
    public override SynchronizationContext CreateCopy() => this;

    private bool TryStartDraining() => Interlocked.CompareExchange(ref _isDraining, 1, 0) == 0;

    private void Drain()
    {
        var previousContext = Current;
        SetSynchronizationContext(this);
        Volatile.Write(ref _drainingThread, Thread.CurrentThread);
        try
        {
            do
            {
                while (_callbacks.TryDequeue(out var callback))
                {
                    callback.Callback(callback.State);
                }

                // Publish that no drain is running any more before looking at the queue again: a callback posted
                // between the last dequeue and this write found the flag set and queued no drain of its own.
                Volatile.Write(ref _isDraining, 0);
            }
            while (!_callbacks.IsEmpty && TryStartDraining());
        }
        catch
        {
            // A callback that throws must not leave the flag set: no drain would ever be queued again.
            Volatile.Write(ref _isDraining, 0);
            throw;
        }
        finally
        {
            Volatile.Write(ref _drainingThread, null);
            SetSynchronizationContext(previousContext);
        }
    }
}
