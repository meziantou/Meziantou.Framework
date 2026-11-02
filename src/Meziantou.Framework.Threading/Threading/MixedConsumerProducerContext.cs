using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Meziantou.Framework.Threading;

/// <summary>Provides a context for <see cref="MixedConsumerProducer"/> that allows enqueuing additional items to be processed.</summary>
/// <typeparam name="T">The type of items to process.</typeparam>
public sealed class MixedConsumerProducerContext<T>
{
    private readonly Channel<T> _channel;
    private readonly bool _isBounded;
    private readonly CancellationToken _processingToken;
    private readonly Func<MixedConsumerProducerContext<T>, T, ValueTask> _processItem;

    // Number of items that have been written to the channel but not fully processed yet. Reaching 0
    // means no item is pending and no running action can produce a new one, so the channel can be
    // completed to let the consumers stop.
    private int _pendingItems;

    // Number of EnqueueAsync calls waiting for room in a full channel.
    private int _waitingEnqueuers;
    private volatile bool _isCompleted;

    internal MixedConsumerProducerContext(Channel<T> channel, bool isBounded, Func<MixedConsumerProducerContext<T>, T, ValueTask> processItem, CancellationToken processingToken)
    {
        _channel = channel;
        _isBounded = isBounded;
        _processingToken = processingToken;
        _processItem = processItem;
    }

    // Number of consumers draining the channel. Only the consumers can make room in a full channel.
    internal int ConsumerCount { get; set; }

    // Number of items waiting in the channel.
    internal int QueuedItemCount => _channel.Reader.Count;

    /// <summary>Enqueues an item to be processed.</summary>
    /// <param name="item">The item to enqueue.</param>
    /// <exception cref="InvalidOperationException">The item cannot be enqueued, for instance because <see cref="MixedConsumerProducerOptions.MaxPendingItems"/> items are already pending. Use <see cref="EnqueueAsync"/> to wait instead.</exception>
    public void Enqueue(T item)
    {
        if (!TryEnqueue(item))
        {
            if (_isBounded && !_isCompleted)
                throw new InvalidOperationException($"Item cannot be enqueued because the maximum number of pending items is reached. Use {nameof(EnqueueAsync)} to wait until an item is dequeued.");

            throw new InvalidOperationException("Item cannot be enqueued");
        }
    }

    /// <summary>Tries to enqueue an item to be processed without waiting.</summary>
    /// <param name="item">The item to enqueue.</param>
    /// <returns><see langword="true"/> if the item is enqueued; <see langword="false"/> if it cannot be enqueued, for instance because <see cref="MixedConsumerProducerOptions.MaxPendingItems"/> items are already pending.</returns>
    public bool TryEnqueue(T item)
    {
        // Count the item before writing it, so the count cannot transiently reach 0 while the item
        // is in flight. The caller is either processing an item of its own or holding a producer
        // reservation, so the count is at least 1 here.
        Interlocked.Increment(ref _pendingItems);
        if (!_channel.Writer.TryWrite(item))
        {
            Interlocked.Decrement(ref _pendingItems);
            return false;
        }

        return true;
    }

    /// <summary>Enqueues an item to be processed, waiting while <see cref="MixedConsumerProducerOptions.MaxPendingItems"/> items are pending.</summary>
    /// <param name="item">The item to enqueue.</param>
    /// <param name="cancellationToken">A token to cancel the wait.</param>
    /// <returns>A task that completes once the item is enqueued, or once it is processed when every consumer would wait for room.</returns>
    /// <remarks>When every consumer would wait for room, nothing could dequeue an item anymore. In that case the item is processed by the caller before this method returns, and an exception thrown while processing it is reported by <see cref="MixedConsumerProducer.Process"/> like the exceptions of the other items.</remarks>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> or the token of the processing was canceled.</exception>
    /// <exception cref="InvalidOperationException">The processing is completed.</exception>
    public ValueTask EnqueueAsync(T item, CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
            return ValueTask.FromCanceled(cancellationToken);

        if (TryEnqueue(item))
            return ValueTask.CompletedTask;

        if (!_isBounded || _isCompleted)
            return ValueTask.FromException(new InvalidOperationException("Item cannot be enqueued"));

        return EnqueueSlowAsync(item, cancellationToken);
    }

    private async ValueTask EnqueueSlowAsync(T item, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _pendingItems);
        var processInline = false;
        try
        {
            // A consumer waiting here cannot dequeue anything. If every consumer waited, the channel would
            // stay full forever, so the last one processes its item itself instead.
            if (Interlocked.Increment(ref _waitingEnqueuers) >= ConsumerCount)
            {
                processInline = true;
                cancellationToken.ThrowIfCancellationRequested();
                _processingToken.ThrowIfCancellationRequested();
            }
            else
            {
                // The consumers stop when the processing is canceled, so stop waiting as well.
                using var linkedSource = cancellationToken.CanBeCanceled ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _processingToken) : null;
                await _channel.Writer.WriteAsync(item, linkedSource?.Token ?? _processingToken).ConfigureAwait(false);
            }
        }
        catch
        {
            // The caller is processing an item of its own, so the count cannot reach 0 here.
            Interlocked.Decrement(ref _pendingItems);
            throw;
        }
        finally
        {
            Interlocked.Decrement(ref _waitingEnqueuers);
        }

        if (processInline)
        {
            // Processing an item inline nests the call, so avoid overflowing the stack on deep chains.
            if (!RuntimeHelpers.TryEnsureSufficientExecutionStack())
            {
                await Task.Yield();
            }

            await _processItem(this, item).ConfigureAwait(false);
        }
    }

    // Enqueues an item from a producer that is not a consumer, such as the enumeration of the initial
    // items. It can wait safely: the consumers keep dequeuing the items meanwhile.
    internal ValueTask EnqueueFromProducerAsync(T item)
    {
        if (TryEnqueue(item))
            return ValueTask.CompletedTask;

        if (!_isBounded || _isCompleted)
            return ValueTask.FromException(new InvalidOperationException("Item cannot be enqueued"));

        return WriteAsync(item);

        async ValueTask WriteAsync(T item)
        {
            Interlocked.Increment(ref _pendingItems);
            try
            {
                await _channel.Writer.WriteAsync(item, _processingToken).ConfigureAwait(false);
            }
            catch
            {
                // The producer holds a reservation, so the count cannot reach 0 here.
                Interlocked.Decrement(ref _pendingItems);
                throw;
            }
        }
    }

    // Accounts for a producer that can enqueue items while not processing an item of its own. The
    // enumeration of the initial items is such a producer: without the reservation, the count could
    // reach 0 between two initial items and complete the channel while more items are still coming.
    internal void ReserveProducer() => Interlocked.Increment(ref _pendingItems);

    internal void ReleaseProducer() => OnItemProcessed();

    internal void OnItemProcessed()
    {
        if (Interlocked.Decrement(ref _pendingItems) == 0)
        {
            _isCompleted = true;
            _channel.Writer.Complete();
        }
    }
}
