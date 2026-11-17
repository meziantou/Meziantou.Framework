namespace Meziantou.Framework.WebPlatform.Infra;

/// <summary>
/// Temporary lists reused by the hot paths of the current thread (e.g. the CSS rule parser, whose calls are nested, so it
/// needs several lists at once): growing a new list for each call allocates its arrays again. A list that is not returned
/// (e.g. when an exception is thrown) is collected.
/// </summary>
internal static class ListPool<T>
{
    // The lists that grow larger are not kept, so the pool does not hold a large buffer
    private const int MaxCapacity = 16 * 1024;

    private const int MaxCount = 32;

    [ThreadStatic]
    private static Stack<List<T>>? s_lists;

    public static List<T> Rent() => s_lists is { Count: > 0 } lists ? lists.Pop() : [];

    /// <summary>Clears the list and returns it to the pool: the caller must not use it anymore.</summary>
    public static void Return(List<T> list)
    {
        if (list.Capacity > MaxCapacity)
            return;

        list.Clear();
        var lists = s_lists ??= new Stack<List<T>>();
        if (lists.Count < MaxCount)
        {
            lists.Push(list);
        }
    }
}
