using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    /// <summary>Asserts that <paramref name="actual"/> is a proper subset of <paramref name="expectedSuperset"/>, like <c>actual.IsProperSubsetOf(expectedSuperset)</c>.</summary>
    /// <param name="expectedSuperset">The collection expected to contain every unique item in <paramref name="actual"/> and at least one additional unique item.</param>
    /// <param name="actual">The collection expected to be a proper subset of <paramref name="expectedSuperset"/>.</param>
    /// <param name="comparer">The comparer used to compare values. When <see langword="null"/> and <paramref name="actual"/> is a set, the set compares values itself, like <see cref="ISet{T}.IsProperSubsetOf(IEnumerable{T})"/>; otherwise <see cref="EqualityComparer{T}.Default"/> is used.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected superset.</param>
    public static void ProperSubset<T>(IEnumerable<T> expectedSuperset, [NotNull] IEnumerable<T>? actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expectedSuperset))] string? expectedExpression = null)
    {
        AssertSetRelation(SetRelation.ProperSubset, expectedSuperset, actual, comparer, message, actualExpression, expectedExpression);
    }

    /// <summary>Asserts that <paramref name="actual"/> is a proper subset of <paramref name="expectedSuperset"/>.</summary>
    /// <param name="expectedSuperset">The collection expected to contain every unique item in <paramref name="actual"/> and at least one additional unique item.</param>
    /// <param name="actual">The collection expected to be a proper subset of <paramref name="expectedSuperset"/>.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected superset.</param>
    public static void ProperSubset(System.Collections.IEnumerable expectedSuperset, [NotNull] System.Collections.IEnumerable? actual, System.Collections.IEqualityComparer? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expectedSuperset))] string? expectedExpression = null)
    {
        AssertSetRelation(SetRelation.ProperSubset, expectedSuperset, actual, comparer, message, actualExpression, expectedExpression);
    }

    /// <summary>Asserts that <paramref name="actual"/> is a subset of <paramref name="expectedSuperset"/>, like <c>actual.IsSubsetOf(expectedSuperset)</c>.</summary>
    /// <param name="expectedSuperset">The collection expected to contain every unique item in <paramref name="actual"/>.</param>
    /// <param name="actual">The collection expected to be a subset of <paramref name="expectedSuperset"/>.</param>
    /// <param name="comparer">The comparer used to compare values. When <see langword="null"/> and <paramref name="actual"/> is a set, the set compares values itself, like <see cref="ISet{T}.IsSubsetOf(IEnumerable{T})"/>; otherwise <see cref="EqualityComparer{T}.Default"/> is used.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected superset.</param>
    public static void Subset<T>(IEnumerable<T> expectedSuperset, [NotNull] IEnumerable<T>? actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expectedSuperset))] string? expectedExpression = null)
    {
        AssertSetRelation(SetRelation.Subset, expectedSuperset, actual, comparer, message, actualExpression, expectedExpression);
    }

    /// <summary>Asserts that <paramref name="actual"/> is a subset of <paramref name="expectedSuperset"/>.</summary>
    /// <param name="expectedSuperset">The collection expected to contain every unique item in <paramref name="actual"/>.</param>
    /// <param name="actual">The collection expected to be a subset of <paramref name="expectedSuperset"/>.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected superset.</param>
    public static void Subset(System.Collections.IEnumerable expectedSuperset, [NotNull] System.Collections.IEnumerable? actual, System.Collections.IEqualityComparer? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expectedSuperset))] string? expectedExpression = null)
    {
        AssertSetRelation(SetRelation.Subset, expectedSuperset, actual, comparer, message, actualExpression, expectedExpression);
    }

    /// <summary>Asserts that <paramref name="actual"/> is a proper superset of <paramref name="expectedSubset"/>, like <c>actual.IsProperSupersetOf(expectedSubset)</c>.</summary>
    /// <param name="expectedSubset">The collection whose unique items are all expected in <paramref name="actual"/>, with at least one fewer unique item.</param>
    /// <param name="actual">The collection expected to be a proper superset of <paramref name="expectedSubset"/>.</param>
    /// <param name="comparer">The comparer used to compare values. When <see langword="null"/> and <paramref name="actual"/> is a set, the set compares values itself, like <see cref="ISet{T}.IsProperSupersetOf(IEnumerable{T})"/>; otherwise <see cref="EqualityComparer{T}.Default"/> is used.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected subset.</param>
    public static void ProperSuperset<T>(IEnumerable<T> expectedSubset, [NotNull] IEnumerable<T>? actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expectedSubset))] string? expectedExpression = null)
    {
        AssertSetRelation(SetRelation.ProperSuperset, expectedSubset, actual, comparer, message, actualExpression, expectedExpression);
    }

    /// <summary>Asserts that <paramref name="actual"/> is a proper superset of <paramref name="expectedSubset"/>.</summary>
    /// <param name="expectedSubset">The collection whose unique items are all expected in <paramref name="actual"/>, with at least one fewer unique item.</param>
    /// <param name="actual">The collection expected to be a proper superset of <paramref name="expectedSubset"/>.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected subset.</param>
    public static void ProperSuperset(System.Collections.IEnumerable expectedSubset, [NotNull] System.Collections.IEnumerable? actual, System.Collections.IEqualityComparer? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expectedSubset))] string? expectedExpression = null)
    {
        AssertSetRelation(SetRelation.ProperSuperset, expectedSubset, actual, comparer, message, actualExpression, expectedExpression);
    }

    /// <summary>Asserts that <paramref name="actual"/> is a superset of <paramref name="expectedSubset"/>, like <c>actual.IsSupersetOf(expectedSubset)</c>.</summary>
    /// <param name="expectedSubset">The collection whose unique items are all expected in <paramref name="actual"/>.</param>
    /// <param name="actual">The collection expected to be a superset of <paramref name="expectedSubset"/>.</param>
    /// <param name="comparer">The comparer used to compare values. When <see langword="null"/> and <paramref name="actual"/> is a set, the set compares values itself, like <see cref="ISet{T}.IsSupersetOf(IEnumerable{T})"/>; otherwise <see cref="EqualityComparer{T}.Default"/> is used.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected subset.</param>
    public static void Superset<T>(IEnumerable<T> expectedSubset, [NotNull] IEnumerable<T>? actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expectedSubset))] string? expectedExpression = null)
    {
        AssertSetRelation(SetRelation.Superset, expectedSubset, actual, comparer, message, actualExpression, expectedExpression);
    }

    /// <summary>Asserts that <paramref name="actual"/> is a superset of <paramref name="expectedSubset"/>.</summary>
    /// <param name="expectedSubset">The collection whose unique items are all expected in <paramref name="actual"/>.</param>
    /// <param name="actual">The collection expected to be a superset of <paramref name="expectedSubset"/>.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected subset.</param>
    public static void Superset(System.Collections.IEnumerable expectedSubset, [NotNull] System.Collections.IEnumerable? actual, System.Collections.IEqualityComparer? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expectedSubset))] string? expectedExpression = null)
    {
        AssertSetRelation(SetRelation.Superset, expectedSubset, actual, comparer, message, actualExpression, expectedExpression);
    }

    private static void AssertSetRelation(SetRelation relation, System.Collections.IEnumerable expected, [NotNull] System.Collections.IEnumerable? actual, System.Collections.IEqualityComparer? comparer, string? message, string? actualExpression, string? expectedExpression)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (actual is null)
            throw CreateSetNullActualException(relation, expected, message, actualExpression, expectedExpression);

        AssertSetRelation(relation, EnumerateObjects(expected), EnumerateObjects(actual), new ObjectEqualityComparer(comparer), message, actualExpression, expectedExpression);
    }

    private static void AssertSetRelation<T>(SetRelation relation, IEnumerable<T> expected, [NotNull] IEnumerable<T>? actual, IEqualityComparer<T>? comparer, string? message, string? actualExpression, string? expectedExpression)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (actual is null)
            throw CreateSetNullActualException(relation, expected, message, actualExpression, expectedExpression);

        using var expectedSnapshot = CollectionSnapshot.Create<T>(expected);
        using var actualSnapshot = CollectionSnapshot.Create<T>(actual);

        if (HasSetRelation(relation, actual, expectedSnapshot, actualSnapshot, comparer))
            return;

        // A set does not read the snapshots, or may stop reading them early, so they must be completed for the message
        expectedSnapshot.EnsureComplete();
        actualSnapshot.EnsureComplete();
        throw new AssertionException(ErrorFormatter.Format(new CollectionSetAssertionError<T>(GetSetAssertionName(relation), GetExpectedSetRole(relation), expectedSnapshot, actualSnapshot, actualExpression, expectedExpression, message)));
    }

    private static AssertionException CreateSetNullActualException<TExpected>(SetRelation relation, TExpected expected, string? message, string? actualExpression, string? expectedExpression)
    {
        var role = GetExpectedSetRole(relation);
        return new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<TExpected>(GetSetAssertionName(relation), $"Expected {role} expression", $"Expected {role}", expected, actualExpression, expectedExpression, message)));
    }

    private static string GetSetAssertionName(SetRelation relation)
    {
        return relation switch
        {
            SetRelation.Subset => nameof(Subset),
            SetRelation.ProperSubset => nameof(ProperSubset),
            SetRelation.Superset => nameof(Superset),
            _ => nameof(ProperSuperset),
        };
    }

    /// <summary>Gets the role of the expected collection: the superset for a subset assertion, and the subset for a superset assertion.</summary>
    private static string GetExpectedSetRole(SetRelation relation)
    {
        return relation is SetRelation.Subset or SetRelation.ProperSubset ? "superset" : "subset";
    }

    /// <summary>Determines whether <paramref name="actualItems"/> has <paramref name="relation"/> to <paramref name="expectedItems"/>.</summary>
    private static bool HasSetRelation<T>(SetRelation relation, IEnumerable<T> actual, IEnumerable<T> expectedItems, IEnumerable<T> actualItems, IEqualityComparer<T>? comparer)
    {
        // A set uses its own comparer, so the assertion agrees with the ISet<T> method it mirrors, such as actual.IsProperSubsetOf(expected)
        if (comparer is null && actual is ISet<T> set)
        {
            return relation switch
            {
                SetRelation.Subset => set.IsSubsetOf(expectedItems),
                SetRelation.ProperSubset => set.IsProperSubsetOf(expectedItems),
                SetRelation.Superset => set.IsSupersetOf(expectedItems),
                _ => set.IsProperSupersetOf(expectedItems),
            };
        }

        comparer ??= EqualityComparer<T>.Default;
        var expectedSet = CreateSet(expectedItems, comparer);
        var actualSet = CreateSet(actualItems, comparer);
        return relation switch
        {
            SetRelation.Subset => actualSet.IsSubsetOf(expectedSet),
            SetRelation.ProperSubset => actualSet.IsProperSubsetOf(expectedSet),
            SetRelation.Superset => actualSet.IsSupersetOf(expectedSet),
            _ => actualSet.IsProperSupersetOf(expectedSet),
        };
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

    /// <summary>The relation named after the assertion that checks it.</summary>
    private enum SetRelation
    {
        Subset,
        ProperSubset,
        Superset,
        ProperSuperset,
    }

    private sealed class ObjectEqualityComparer(System.Collections.IEqualityComparer? comparer) : IEqualityComparer<object?>
    {
        public new bool Equals(object? x, object? y)
        {
            return comparer is null ? object.Equals(x, y) : comparer.Equals(x, y);
        }

        public int GetHashCode(object? obj)
        {
            if (obj is null)
                return 0;

            return comparer?.GetHashCode(obj) ?? EqualityComparer<object>.Default.GetHashCode(obj);
        }
    }
}
