using System.Collections.Concurrent;

namespace Meziantou.Framework;

public static class ConcurrentQueueExtensions
{
    public static void EnqueueRange<T>(this ConcurrentQueue<T> queue, params T[] items)
    {
        foreach (var item in items)
        {
            queue.Enqueue(item);
        }
    }

    public static void EnqueueRange<T>(this ConcurrentQueue<T> queue, IEnumerable<T> items)
    {
        foreach (var item in items)
        {
            queue.Enqueue(item);
        }
    }

    public static void EnqueueRange<T>(this ConcurrentQueue<T> queue, ReadOnlySpan<T> items)
    {
        foreach (var item in items)
        {
            queue.Enqueue(item);
        }
    }
}
