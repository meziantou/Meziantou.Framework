using System.Threading.Channels;

namespace Meziantou.Framework.Threading;

public sealed class MixedConsumerProducerContext<T>
{
    private readonly ChannelWriter<T> _writer;

    internal MixedConsumerProducerContext(ChannelWriter<T> writer)
    {
        _writer = writer;
    }

    public void Enqueue(T item)
    {
        if (!_writer.TryWrite(item))
            throw new InvalidOperationException("Item cannot be enqueued");
    }
}
