namespace Meziantou.Framework.Threading;

/// <summary>Implemented by the waiters stored in a <see cref="WaiterQueue{T}"/>. The links live on the waiter
/// itself, so removing a waiter does not have to search for it.</summary>
internal interface IWaiterQueueNode<T>
    where T : class, IWaiterQueueNode<T>
{
    T? Previous { get; set; }
    T? Next { get; set; }

    /// <summary>Whether the waiter currently belongs to a queue. Guarded by the queue owner's lock.</summary>
    bool IsQueued { get; set; }
}
