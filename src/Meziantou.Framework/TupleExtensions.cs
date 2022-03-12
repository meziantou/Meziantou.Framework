using System.Runtime.CompilerServices;

namespace Meziantou.Framework;

public static class TupleExtensions
{
    public static object?[] ToArray<T>(this T tuple)
        where T : notnull, ITuple
    {
        if (tuple.Length > 0)
        {
            var result = new object?[tuple.Length];
            for (var i = 0; i < result.Length; i++)
            {
                result[i] = tuple[i];
            }

            return result;
        }
        else
        {
            return Array.Empty<object?>();
        }
    }
}
