namespace Meziantou.Framework.JavaScriptEngine;

/// <summary>A waiter of <c>Atomics.wait</c> or <c>Atomics.waitAsync</c> (a Waiter Record).</summary>
internal sealed class AtomicsWaiter
{
    /// <summary>Gets or sets whether a notification removed the waiter from its list ([[Result]] is "ok").</summary>
    public bool Notified { get; set; }

    /// <summary>Gets or sets whether the waiter is in its list.</summary>
    public bool IsInList { get; set; }

    /// <summary>Gets the event of a synchronous waiter (the agent is suspended on it).</summary>
    public ManualResetEventSlim? Event { get; init; }

    /// <summary>Gets the callback of an asynchronous waiter: called (in the critical section, on any thread) with true when notified, false when timed out.</summary>
    public Action<bool>? OnResolved { get; init; }
}

/// <summary>
/// The WaiterList records of all the agents (https://tc39.es/ecma262/#sec-waiterlist-objects): a list of waiters per
/// (Shared Data Block, byte index), shared by all the engines of the process. A single lock is the critical section of
/// all the lists.
/// </summary>
internal static class AtomicsWaiterList
{
    private static readonly Lock CriticalSection = new();
    private static readonly Dictionary<(JsSharedDataBlock Block, int ByteIndex), LinkedList<AtomicsWaiter>> Lists = [];

    /// <summary>EnterCriticalSection(WL) for every waiter list; dispose the result to leave it.</summary>
    public static Lock.Scope Enter() => CriticalSection.EnterScope();

    /// <summary>AddWaiter(WL, waiterRecord); must be called in the critical section.</summary>
    public static void AddWaiter(JsSharedDataBlock block, int byteIndex, AtomicsWaiter waiter)
    {
        if (!Lists.TryGetValue((block, byteIndex), out var list))
        {
            list = new LinkedList<AtomicsWaiter>();
            Lists.Add((block, byteIndex), list);
        }

        list.AddLast(waiter);
        waiter.IsInList = true;
    }

    /// <summary>RemoveWaiter(WL, waiterRecord); must be called in the critical section.</summary>
    public static void RemoveWaiter(JsSharedDataBlock block, int byteIndex, AtomicsWaiter waiter)
    {
        if (!waiter.IsInList)
            return;

        waiter.IsInList = false;
        if (Lists.TryGetValue((block, byteIndex), out var list))
        {
            list.Remove(waiter);
            if (list.Count == 0)
            {
                Lists.Remove((block, byteIndex));
            }
        }
    }

    /// <summary>RemoveWaiters(WL, c) and NotifyWaiter for each removed waiter: returns the number of waiters notified. Must be called in the critical section.</summary>
    public static int NotifyWaiters(JsSharedDataBlock block, int byteIndex, double count)
    {
        if (count <= 0 || !Lists.TryGetValue((block, byteIndex), out var list))
            return 0;

        var notified = 0;
        while (notified < count && list.First is { } node)
        {
            var waiter = node.Value;
            list.RemoveFirst();
            waiter.IsInList = false;
            waiter.Notified = true;
            notified++;
            if (waiter.Event is { } waitEvent)
            {
                waitEvent.Set();
            }
            else
            {
                waiter.OnResolved?.Invoke(true);
            }
        }

        if (list.Count == 0)
        {
            Lists.Remove((block, byteIndex));
        }

        return notified;
    }
}
