namespace Meziantou.Framework.Threading;

/// <summary>Options that configure <see cref="MixedConsumerProducer.Process"/>, including a bound on the number of pending items.</summary>
/// <remarks>
/// <see cref="ParallelOptions.MaxDegreeOfParallelism"/> limits the number of items being processed at the same time, while
/// <see cref="MaxPendingItems"/> limits the number of items waiting to be processed. They are distinct limits.
/// </remarks>
public sealed class MixedConsumerProducerOptions : ParallelOptions
{
    /// <summary>Gets or sets the maximum number of items waiting to be processed, not counting the items being processed. The default value, -1, does not limit them.</summary>
    /// <remarks>
    /// <para>When the limit is reached, the enumeration of the initial items waits until an item is dequeued, and
    /// <see cref="MixedConsumerProducerContext{T}.EnqueueAsync"/> waits as well. When every consumer would wait, the item is
    /// processed by the caller of <see cref="MixedConsumerProducerContext{T}.EnqueueAsync"/> before it returns, so the processing
    /// cannot deadlock and no item is dropped.</para>
    /// <para><see cref="MixedConsumerProducerContext{T}.Enqueue"/> cannot wait: it throws an <see cref="InvalidOperationException"/>
    /// when the limit is reached. <see cref="MixedConsumerProducerContext{T}.TryEnqueue"/> returns <see langword="false"/>.</para>
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is 0 or less than -1.</exception>
    public int MaxPendingItems
    {
        get;
        set
        {
            if (value is 0 or < -1)
                throw new ArgumentOutOfRangeException(nameof(value), value, "The value must be -1 or greater than 0.");

            field = value;
        }
    } = -1;
}
