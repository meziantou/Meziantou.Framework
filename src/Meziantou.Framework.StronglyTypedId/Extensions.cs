namespace Meziantou.Framework.StronglyTypedId;

internal static class Extensions
{
    public static IEnumerable<T> WhereNotNull<T>(this IEnumerable<T?> enumerable)
        where T : class
    {
        foreach (var item in enumerable)
        {
            if (item != null)
                yield return item;
        }
    }
}
