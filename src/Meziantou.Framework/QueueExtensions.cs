namespace Meziantou.Framework;

/// <summary>
/// Provides extension methods for <see cref="Queue{T}"/>.
/// </summary>
public static class QueueExtensions
{
    /// <summary>Enqueues multiple items to the queue.</summary>
    public static void EnqueueRange<T>(this Queue<T> queue, params T[] items)
    {
        foreach (var item in items)
        {
            queue.Enqueue(item);
        }
    }

    /// <summary>Enqueues all items from an enumerable to the queue.</summary>
    public static void EnqueueRange<T>(this Queue<T> queue, IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            queue.Enqueue(item);
        }
    }

    /// <summary>Enqueues all items from a span to the queue.</summary>
    public static void EnqueueRange<T>(this Queue<T> queue, ReadOnlySpan<T> items)
    {
        foreach (var item in items)
        {
            queue.Enqueue(item);
        }
    }
}
