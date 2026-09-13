using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    /// <summary>Asserts that a collection is a proper subset of another collection.</summary>
    /// <param name="expected">The collection expected to be a proper subset of <paramref name="actual"/>.</param>
    /// <param name="actual">The collection expected to contain every unique item in <paramref name="expected"/> and at least one additional unique item.</param>
    /// <param name="comparer">The comparer used to compare values. When <see langword="null"/> and <paramref name="expected"/> is a set, the set compares values itself, like <see cref="ISet{T}.IsProperSubsetOf(IEnumerable{T})"/>; otherwise <see cref="EqualityComparer{T}.Default"/> is used.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void ProperSubset<T>(IEnumerable<T> expected, IEnumerable<T> actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        using var expectedSnapshot = CollectionSnapshot.Create<T>(expected);
        using var actualSnapshot = CollectionSnapshot.Create<T>(actual);

        if (IsProperSubset(expected, expectedSnapshot, actualSnapshot, comparer))
            return;

        // A set does not read the snapshots, or may stop reading them early, so they must be completed for the message
        expectedSnapshot.EnsureComplete();
        actualSnapshot.EnsureComplete();
        throw new AssertionException(ErrorFormatter.Format(new CollectionSetAssertionError<T>(expectedSnapshot, actualSnapshot, isSuperset: false, actualExpression, expectedExpression, message)));
    }

    /// <summary>Asserts that a non-generic collection is a proper subset of another non-generic collection.</summary>
    /// <param name="expected">The collection expected to be a proper subset of <paramref name="actual"/>.</param>
    /// <param name="actual">The collection expected to contain every unique item in <paramref name="expected"/> and at least one additional unique item.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void ProperSubset(System.Collections.IEnumerable expected, System.Collections.IEnumerable actual, System.Collections.IEqualityComparer? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        ProperSubset(EnumerateObjects(expected), EnumerateObjects(actual), new ObjectEqualityComparer(comparer), message, actualExpression, expectedExpression);
    }

    /// <summary>Asserts that a collection is a proper superset of another collection.</summary>
    /// <param name="expected">The collection expected to be a proper superset of <paramref name="actual"/>.</param>
    /// <param name="actual">The collection expected to be contained in <paramref name="expected"/> with at least one fewer unique item.</param>
    /// <param name="comparer">The comparer used to compare values. When <see langword="null"/> and <paramref name="expected"/> is a set, the set compares values itself, like <see cref="ISet{T}.IsProperSupersetOf(IEnumerable{T})"/>; otherwise <see cref="EqualityComparer{T}.Default"/> is used.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void ProperSuperset<T>(IEnumerable<T> expected, IEnumerable<T> actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        using var expectedSnapshot = CollectionSnapshot.Create<T>(expected);
        using var actualSnapshot = CollectionSnapshot.Create<T>(actual);

        if (IsProperSuperset(expected, expectedSnapshot, actualSnapshot, comparer))
            return;

        // A set does not read the snapshots, or may stop reading them early, so they must be completed for the message
        expectedSnapshot.EnsureComplete();
        actualSnapshot.EnsureComplete();
        throw new AssertionException(ErrorFormatter.Format(new CollectionSetAssertionError<T>(expectedSnapshot, actualSnapshot, isSuperset: true, actualExpression, expectedExpression, message)));
    }

    /// <summary>Asserts that a non-generic collection is a proper superset of another non-generic collection.</summary>
    /// <param name="expected">The collection expected to be a proper superset of <paramref name="actual"/>.</param>
    /// <param name="actual">The collection expected to be contained in <paramref name="expected"/> with at least one fewer unique item.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected value.</param>
    public static void ProperSuperset(System.Collections.IEnumerable expected, System.Collections.IEnumerable actual, System.Collections.IEqualityComparer? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        ProperSuperset(EnumerateObjects(expected), EnumerateObjects(actual), new ObjectEqualityComparer(comparer), message, actualExpression, expectedExpression);
    }

    private static bool IsProperSubset<T>(IEnumerable<T> expected, IEnumerable<T> expectedItems, IEnumerable<T> actualItems, IEqualityComparer<T>? comparer)
    {
        // A set uses its own comparer, so the assertion agrees with ISet<T>.IsProperSubsetOf
        if (comparer is null && expected is ISet<T> set)
            return set.IsProperSubsetOf(actualItems);

        comparer ??= EqualityComparer<T>.Default;
        var expectedSet = CreateSet(expectedItems, comparer);
        var actualSet = CreateSet(actualItems, comparer);
        return expectedSet.Count < actualSet.Count && expectedSet.IsSubsetOf(actualSet);
    }

    private static bool IsProperSuperset<T>(IEnumerable<T> expected, IEnumerable<T> expectedItems, IEnumerable<T> actualItems, IEqualityComparer<T>? comparer)
    {
        // A set uses its own comparer, so the assertion agrees with ISet<T>.IsProperSupersetOf
        if (comparer is null && expected is ISet<T> set)
            return set.IsProperSupersetOf(actualItems);

        comparer ??= EqualityComparer<T>.Default;
        var expectedSet = CreateSet(expectedItems, comparer);
        var actualSet = CreateSet(actualItems, comparer);
        return expectedSet.Count > actualSet.Count && expectedSet.IsSupersetOf(actualSet);
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
