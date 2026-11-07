namespace Meziantou.Framework.Threading;

/// <summary>Implemented by the waiters stored in a <see cref="WaiterQueue{T}"/>. The links live on the waiter
/// itself, so removing a waiter does not have to search for it.</summary>
internal interface IWaiterQueueNode<T>
    where T : class, IWaiterQueueNode<T>
{
    T? Previous { get; set; }
    T? Next { get; set; }

    /// <summary>The generation of the queue the waiter was enqueued in, or 0 once it was removed. The waiter belongs
    /// to the queue only while this value matches the queue's current generation. Guarded by the queue owner's lock;
    /// only <see cref="WaiterQueue{T}"/> uses it.</summary>
    int QueueGeneration { get; set; }
}
