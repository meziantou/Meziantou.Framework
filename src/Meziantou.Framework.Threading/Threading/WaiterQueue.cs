using System.Diagnostics;

namespace Meziantou.Framework.Threading;

/// <summary>A FIFO queue of waiters that removes a known waiter in constant time.</summary>
/// <remarks>
/// A <see cref="Queue{T}"/> can only remove a waiter by dequeuing and re-enqueuing every other one, so canceling
/// the whole queue costs O(n²) visits. Waiters are canceled one by one during a cancellation storm or a shutdown,
/// which makes that cost visible. This type is not thread-safe; the owner synchronizes access to it.
/// </remarks>
[DebuggerDisplay("Count = {Count}")]
internal sealed class WaiterQueue<T>
    where T : class, IWaiterQueueNode<T>
{
    private T? _head;
    private T? _tail;

    // Identifies the waiters currently linked in this queue: a waiter belongs to the queue only while its
    // QueueGeneration matches. Detaching every waiter at once then only has to advance this value, instead of
    // visiting each waiter. 0 is never used, so a removed waiter never looks queued.
    private int _generation = 1;

    public int Count { get; private set; }

    public void Enqueue(T waiter)
    {
        Debug.Assert(waiter.QueueGeneration != _generation, "The waiter is already queued");

        waiter.Previous = _tail;
        waiter.QueueGeneration = _generation;

        if (_tail is null)
        {
            _head = waiter;
        }
        else
        {
            _tail.Next = waiter;
        }

        _tail = waiter;
        Count++;
    }

    /// <summary>Removes and returns the oldest waiter, or <see langword="null"/> when the queue is empty.</summary>
    public T? Dequeue()
    {
        var head = _head;
        if (head is not null)
        {
            Remove(head);
        }

        return head;
    }

    /// <summary>Removes <paramref name="waiter"/> from the queue it was enqueued in.</summary>
    /// <returns><see langword="true"/> if the waiter was queued and got removed; <see langword="false"/> if it had
    /// already been removed by another caller.</returns>
    public bool Remove(T waiter)
    {
        if (waiter.QueueGeneration != _generation)
            return false;

        var previous = waiter.Previous;
        var next = waiter.Next;

        if (previous is null)
        {
            _head = next;
        }
        else
        {
            previous.Next = next;
        }

        if (next is null)
        {
            _tail = previous;
        }
        else
        {
            next.Previous = previous;
        }

        // Clear the links: a waiter usually outlives its removal, and would otherwise keep the rest of the queue alive.
        waiter.Previous = null;
        waiter.Next = null;
        waiter.QueueGeneration = 0;
        Count--;
        return true;
    }

    /// <summary>Removes every waiter at once, in constant time, and returns the oldest one.</summary>
    /// <returns>The first waiter of a chain that follows <see cref="IWaiterQueueNode{T}.Next"/> in FIFO order and ends
    /// with <see langword="null"/>, or <see langword="null"/> when the queue is empty.</returns>
    /// <remarks>
    /// The detached waiters no longer belong to the queue as soon as this method returns: <see cref="Remove"/> returns
    /// <see langword="false"/> for them, so the caller owns them. Their links are left in place so that the caller can
    /// walk the chain without holding the lock; the caller clears them while walking.
    /// </remarks>
    public T? DetachAll()
    {
        var head = _head;
        if (head is null)
            return null;

        _head = null;
        _tail = null;
        Count = 0;

        // Skip 0, which marks the waiters that are not queued. Wrapping around is harmless: a detached waiter is only
        // passed to Remove until its owner finishes handling it, long before 2^32 other detaches can happen.
        _generation++;
        if (_generation == 0)
        {
            _generation = 1;
        }

        return head;
    }
}
