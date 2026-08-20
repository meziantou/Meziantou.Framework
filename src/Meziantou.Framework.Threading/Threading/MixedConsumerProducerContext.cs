using System.Threading.Channels;

namespace Meziantou.Framework.Threading;

/// <summary>Provides a context for <see cref="MixedConsumerProducer"/> that allows enqueuing additional items to be processed.</summary>
/// <typeparam name="T">The type of items to process.</typeparam>
public sealed class MixedConsumerProducerContext<T>
{
    private readonly ChannelWriter<T> _writer;

    // Number of items that have been written to the channel but not fully processed yet. Reaching 0
    // means no item is pending and no running action can produce a new one, so the channel can be
    // completed to let the consumers stop.
    private int _pendingItems;

    internal MixedConsumerProducerContext(ChannelWriter<T> writer)
    {
        _writer = writer;
    }

    /// <summary>Enqueues an item to be processed.</summary>
    /// <param name="item">The item to enqueue.</param>
    public void Enqueue(T item)
    {
        // Count the item before writing it, so the count cannot transiently reach 0 while the item
        // is in flight. The caller is processing an item of its own, so the count is at least 1 here.
        Interlocked.Increment(ref _pendingItems);
        if (!_writer.TryWrite(item))
        {
            Interlocked.Decrement(ref _pendingItems);
            throw new InvalidOperationException("Item cannot be enqueued");
        }
    }

    internal void OnItemProcessed()
    {
        if (Interlocked.Decrement(ref _pendingItems) == 0)
        {
            _writer.Complete();
        }
    }
}
