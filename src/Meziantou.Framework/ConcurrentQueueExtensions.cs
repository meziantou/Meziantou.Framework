using System.Collections.Concurrent;

namespace Meziantou.Framework;

/// <summary>
/// Provides extension methods for <see cref="ConcurrentQueue{T}"/>.
/// </summary>
/// <example>
/// <code>
/// var queue = new ConcurrentQueue&lt;int&gt;();
/// queue.EnqueueRange(1, 2, 3, 4, 5);
/// </code>
/// </example>
public static class ConcurrentQueueExtensions
{
    /// <summary>Enqueues multiple items to the queue.</summary>
    public static void EnqueueRange<T>(this ConcurrentQueue<T> queue, params T[] items)
    {
        foreach (var item in items)
        {
            queue.Enqueue(item);
        }
    }

    /// <summary>Enqueues all items from an enumerable to the queue.</summary>
    public static void EnqueueRange<T>(this ConcurrentQueue<T> queue, IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            queue.Enqueue(item);
        }
    }

    /// <summary>Enqueues all items from a span to the queue.</summary>
    public static void EnqueueRange<T>(this ConcurrentQueue<T> queue, ReadOnlySpan<T> items)
    {
        foreach (var item in items)
        {
            queue.Enqueue(item);
        }
    }
}
