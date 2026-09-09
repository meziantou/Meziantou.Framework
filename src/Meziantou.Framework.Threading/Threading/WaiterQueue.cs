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

    public int Count { get; private set; }

    public void Enqueue(T waiter)
    {
        Debug.Assert(!waiter.IsQueued, "The waiter is already queued");

        waiter.Previous = _tail;
        waiter.IsQueued = true;

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
        if (!waiter.IsQueued)
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
        waiter.IsQueued = false;
        Count--;
        return true;
    }
}
