using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    /// <summary>Asserts that a collection is a proper subset of another collection.</summary>
    /// <param name="expected">The collection expected to be a proper subset of <paramref name="actual"/>.</param>
    /// <param name="actual">The collection expected to contain every unique item in <paramref name="expected"/> and at least one additional unique item.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void ProperSubset<T>(IEnumerable<T> expected, IEnumerable<T> actual, IEqualityComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;
        using var expectedSnapshot = CollectionSnapshot.Create<T>(expected);
        using var actualSnapshot = CollectionSnapshot.Create<T>(actual);

        var expectedSet = CreateSet(expectedSnapshot, comparer);
        var actualSet = CreateSet(actualSnapshot, comparer);

        if (expectedSet.Count < actualSet.Count && expectedSet.IsSubsetOf(actualSet))
            return;

        throw new AssertionException(ErrorFormatter.Format(new CollectionSetAssertionError<T>(expectedSnapshot, actualSnapshot, isSuperset: false, actualExpression, expectedExpression)));
    }

    /// <summary>Asserts that a non-generic collection is a proper subset of another non-generic collection.</summary>
    /// <param name="expected">The collection expected to be a proper subset of <paramref name="actual"/>.</param>
    /// <param name="actual">The collection expected to contain every unique item in <paramref name="expected"/> and at least one additional unique item.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void ProperSubset(System.Collections.IEnumerable expected, System.Collections.IEnumerable actual, System.Collections.IEqualityComparer? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        ProperSubset(EnumerateObjects(expected), EnumerateObjects(actual), new ObjectEqualityComparer(comparer), actualExpression, expectedExpression);
    }

    /// <summary>Asserts that a collection is a proper superset of another collection.</summary>
    /// <param name="expected">The collection expected to be a proper superset of <paramref name="actual"/>.</param>
    /// <param name="actual">The collection expected to be contained in <paramref name="expected"/> with at least one fewer unique item.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void ProperSuperset<T>(IEnumerable<T> expected, IEnumerable<T> actual, IEqualityComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;
        using var expectedSnapshot = CollectionSnapshot.Create<T>(expected);
        using var actualSnapshot = CollectionSnapshot.Create<T>(actual);

        var expectedSet = CreateSet(expectedSnapshot, comparer);
        var actualSet = CreateSet(actualSnapshot, comparer);

        if (expectedSet.Count > actualSet.Count && expectedSet.IsSupersetOf(actualSet))
            return;

        throw new AssertionException(ErrorFormatter.Format(new CollectionSetAssertionError<T>(expectedSnapshot, actualSnapshot, isSuperset: true, actualExpression, expectedExpression)));
    }

    /// <summary>Asserts that a non-generic collection is a proper superset of another non-generic collection.</summary>
    /// <param name="expected">The collection expected to be a proper superset of <paramref name="actual"/>.</param>
    /// <param name="actual">The collection expected to be contained in <paramref name="expected"/> with at least one fewer unique item.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void ProperSuperset(System.Collections.IEnumerable expected, System.Collections.IEnumerable actual, System.Collections.IEqualityComparer? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        ProperSuperset(EnumerateObjects(expected), EnumerateObjects(actual), new ObjectEqualityComparer(comparer), actualExpression, expectedExpression);
    }

    private static HashSet<T> CreateSet<T>(IEnumerable<T> items, IEqualityComparer<T> comparer)
    {
        var set = new HashSet<T>(comparer);
        foreach (var item in items)
        {
            set.Add(item);
        }

        return set;
    }

    private sealed class ObjectEqualityComparer(System.Collections.IEqualityComparer? comparer) : IEqualityComparer<object?>
    {
        public new bool Equals(object? x, object? y)
        {
            return Assert.Equals(x, y, comparer);
        }

        public int GetHashCode(object? obj)
        {
            if (obj is null)
                return 0;

            return comparer?.GetHashCode(obj) ?? EqualityComparer<object>.Default.GetHashCode(obj);
        }
    }
}
