using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    public static void NotProperSubset<T>(IEnumerable<T> expected, IEnumerable<T> actual, IEqualityComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;
        var expectedSet = CreateSet(expected, comparer);
        var actualSet = CreateSet(actual, comparer);
        if (!(expectedSet.Count < actualSet.Count && expectedSet.IsSubsetOf(actualSet)))
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new NegativeSetAssertionError(expected, actual, isSuperset: false, actualExpression, expectedExpression)));
    }

    public static void NotProperSubset(System.Collections.IEnumerable expected, System.Collections.IEnumerable actual, System.Collections.IEqualityComparer? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        NotProperSubset(EnumerateObjects(expected), EnumerateObjects(actual), new ObjectEqualityComparer(comparer), actualExpression, expectedExpression);
    }

    public static void NotProperSuperset<T>(IEnumerable<T> expected, IEnumerable<T> actual, IEqualityComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;
        var expectedSet = CreateSet(expected, comparer);
        var actualSet = CreateSet(actual, comparer);
        if (!(expectedSet.Count > actualSet.Count && expectedSet.IsSupersetOf(actualSet)))
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new NegativeSetAssertionError(expected, actual, isSuperset: true, actualExpression, expectedExpression)));
    }

    public static void NotProperSuperset(System.Collections.IEnumerable expected, System.Collections.IEnumerable actual, System.Collections.IEqualityComparer? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        NotProperSuperset(EnumerateObjects(expected), EnumerateObjects(actual), new ObjectEqualityComparer(comparer), actualExpression, expectedExpression);
    }
}
