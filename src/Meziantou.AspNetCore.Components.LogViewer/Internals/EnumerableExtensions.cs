using System.Collections.Generic;

namespace Meziantou.AspNetCore.Components;

internal static class EnumerableExtensions
{
    public static IEnumerable<(T Item, int index)> WithIndex<T>(this IEnumerable<T> enumerable)
    {
        var index = 0;
        foreach (var item in enumerable)
        {
            yield return (item, index);
            index++;
        }
    }
}
