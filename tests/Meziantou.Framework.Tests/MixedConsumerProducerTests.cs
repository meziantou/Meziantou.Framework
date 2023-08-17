#pragma warning disable CA1861 // Avoid constant arrays as arguments
using Xunit;

namespace Meziantou.Framework.Threading.Tests;
public sealed class MixedConsumerProducerTests
{
    [Fact]
    public async Task Process_EmptyData()
    {
        await MixedConsumerProducer.Process(Array.Empty<int>(), new ParallelOptions() { MaxDegreeOfParallelism = 1 }, (context, item, cancellationToken) => ValueTask.CompletedTask);
    }

    [Fact]
    public async Task Process_NoParallelism()
    {
        var count = 0;
        await MixedConsumerProducer.Process(new[] { 1 }, new ParallelOptions() { MaxDegreeOfParallelism = 1 }, (context, item, cancellationToken) =>
        {
            if (item < 100)
            {
                context.Enqueue(item + 1);
            }

            Interlocked.Increment(ref count);
            return ValueTask.CompletedTask;
        });

        Assert.Equal(100, count);
    }

    [Fact]
    public async Task Process()
    {
        var count = 0;
        await MixedConsumerProducer.Process(new[] { 0 }, new ParallelOptions() { MaxDegreeOfParallelism = 16 }, (context, item, cancellationToken) =>
        {
            if (item < 15)
            {
                context.Enqueue(item + 1);
                context.Enqueue(item + 2);
            }

            Interlocked.Increment(ref count);
            return ValueTask.CompletedTask;
        });

        Assert.Equal(3193, count);
    }
}
