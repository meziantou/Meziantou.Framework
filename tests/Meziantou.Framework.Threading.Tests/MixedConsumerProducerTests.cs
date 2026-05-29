#pragma warning disable CA1861 // Avoid constant arrays as arguments
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
        await MixedConsumerProducer.Process([1], new ParallelOptions() { MaxDegreeOfParallelism = 1 }, (context, item, cancellationToken) =>
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
        await MixedConsumerProducer.Process([0], new ParallelOptions() { MaxDegreeOfParallelism = 16 }, (context, item, cancellationToken) =>
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

    [Fact]
    public async Task Process_PropagatesActionExceptions()
    {
        var exception = await Assert.ThrowsAsync<AggregateException>(async () =>
        {
            await MixedConsumerProducer.Process([1, 2, 3], new ParallelOptions() { MaxDegreeOfParallelism = 2 }, (context, item, cancellationToken) =>
            {
                throw new InvalidOperationException("boom " + item);
            });
        });

        Assert.Equal(3, exception.InnerExceptions.Count);
        Assert.All(exception.InnerExceptions, e => Assert.IsType<InvalidOperationException>(e));
    }

    [Fact]
    public async Task Process_PartialFailure_ProcessesAllAndAggregatesOnlyFailures()
    {
        var processed = 0;
        var exception = await Assert.ThrowsAsync<AggregateException>(async () =>
        {
            await MixedConsumerProducer.Process([1, 2, 3, 4], new ParallelOptions() { MaxDegreeOfParallelism = 1 }, (context, item, cancellationToken) =>
            {
                Interlocked.Increment(ref processed);
                if (item % 2 == 0)
                    throw new InvalidOperationException("even " + item);

                return ValueTask.CompletedTask;
            });
        });

        Assert.Equal(4, processed); // every item was attempted even though some failed
        Assert.Equal(2, exception.InnerExceptions.Count); // items 2 and 4
    }

    [Fact]
    public async Task Process_PreCanceledToken_DoesNotProcessAndThrows()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var ran = false;
        var options = new ParallelOptions() { MaxDegreeOfParallelism = 2, CancellationToken = cts.Token };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await MixedConsumerProducer.Process([1, 2, 3], options, (context, item, cancellationToken) =>
            {
                ran = true;
                return ValueTask.CompletedTask;
            });
        });

        Assert.False(ran);
    }

    [Fact]
    public async Task Process_SingleItemWithoutEnqueue()
    {
        var processed = 0;
        await MixedConsumerProducer.Process([42], new ParallelOptions() { MaxDegreeOfParallelism = 4 }, (context, item, cancellationToken) =>
        {
            Assert.Equal(42, item);
            Interlocked.Increment(ref processed);
            return ValueTask.CompletedTask;
        });

        Assert.Equal(1, processed);
    }
}
