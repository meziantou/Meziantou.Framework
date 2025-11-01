using System.Threading.Channels;

namespace Meziantou.Framework.Threading;

/// <summary>Provides a context for <see cref="MixedConsumerProducer"/> that allows enqueuing additional items to be processed.</summary>
/// <typeparam name="T">The type of items to process.</typeparam>
public sealed class MixedConsumerProducerContext<T>
{
    private readonly ChannelWriter<T> _writer;

    internal MixedConsumerProducerContext(ChannelWriter<T> writer)
    {
        _writer = writer;
    }

    /// <summary>Enqueues an item to be processed.</summary>
    /// <param name="item">The item to enqueue.</param>
    public void Enqueue(T item)
    {
        if (!_writer.TryWrite(item))
            throw new InvalidOperationException("Item cannot be enqueued");
    }
}
