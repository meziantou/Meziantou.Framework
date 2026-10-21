using System.Runtime.CompilerServices;

namespace Meziantou.Framework;

/// <summary>Provides extension methods for tuple types.</summary>
public static class TupleExtensions
{
    /// <summary>Converts a tuple to an array of objects.</summary>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Tests/TupleExtensionsTests.cs" ref:region="TupleExtensions_ToArray" ref:dedent="true">
    /// var tuple = (1, "hello", true);
    /// object?[] array = tuple.ToArray();
    /// Assert.Equal([1, "hello", true], array);
    /// </code>
    /// </example>
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
            return [];
        }
    }
}
