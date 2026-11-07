using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    /// <summary>Asserts that <paramref name="actual"/> is not a proper subset of <paramref name="expectedSuperset"/>. This is the exact complement of <c>ProperSubset</c>, so a <see langword="null"/> <paramref name="actual"/> succeeds.</summary>
    /// <param name="expectedSuperset">The collection <paramref name="actual"/> is compared to.</param>
    /// <param name="actual">The collection expected not to be a proper subset of <paramref name="expectedSuperset"/>.</param>
    /// <param name="comparer">The comparer used to compare values. When <see langword="null"/> and <paramref name="actual"/> is a set, the set compares values itself, like <see cref="ISet{T}.IsProperSubsetOf(IEnumerable{T})"/>; otherwise <see cref="EqualityComparer{T}.Default"/> is used.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected superset.</param>
    public static void NotProperSubset<T>(IEnumerable<T> expectedSuperset, IEnumerable<T>? actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expectedSuperset))] string? expectedExpression = null)
    {
        AssertNotSetRelation(SetRelation.ProperSubset, expectedSuperset, actual, comparer, message, actualExpression, expectedExpression);
    }

    /// <inheritdoc cref="NotProperSubset{T}(IEnumerable{T}, IEnumerable{T}, IEqualityComparer{T}, string, string, string)"/>
    public static void NotProperSubset(System.Collections.IEnumerable expectedSuperset, System.Collections.IEnumerable? actual, System.Collections.IEqualityComparer? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expectedSuperset))] string? expectedExpression = null)
    {
        AssertNotSetRelation(SetRelation.ProperSubset, expectedSuperset, actual, comparer, message, actualExpression, expectedExpression);
    }

    /// <summary>Asserts that <paramref name="actual"/> is not a proper superset of <paramref name="expectedSubset"/>. This is the exact complement of <c>ProperSuperset</c>, so a <see langword="null"/> <paramref name="actual"/> succeeds.</summary>
    /// <param name="expectedSubset">The collection <paramref name="actual"/> is compared to.</param>
    /// <param name="actual">The collection expected not to be a proper superset of <paramref name="expectedSubset"/>.</param>
    /// <param name="comparer">The comparer used to compare values. When <see langword="null"/> and <paramref name="actual"/> is a set, the set compares values itself, like <see cref="ISet{T}.IsProperSupersetOf(IEnumerable{T})"/>; otherwise <see cref="EqualityComparer{T}.Default"/> is used.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="expectedExpression">The expression that produced the expected subset.</param>
    public static void NotProperSuperset<T>(IEnumerable<T> expectedSubset, IEnumerable<T>? actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expectedSubset))] string? expectedExpression = null)
    {
        AssertNotSetRelation(SetRelation.ProperSuperset, expectedSubset, actual, comparer, message, actualExpression, expectedExpression);
    }

    /// <inheritdoc cref="NotProperSuperset{T}(IEnumerable{T}, IEnumerable{T}, IEqualityComparer{T}, string, string, string)"/>
    public static void NotProperSuperset(System.Collections.IEnumerable expectedSubset, System.Collections.IEnumerable? actual, System.Collections.IEqualityComparer? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expectedSubset))] string? expectedExpression = null)
    {
        AssertNotSetRelation(SetRelation.ProperSuperset, expectedSubset, actual, comparer, message, actualExpression, expectedExpression);
    }

    private static void AssertNotSetRelation(SetRelation relation, System.Collections.IEnumerable expected, System.Collections.IEnumerable? actual, System.Collections.IEqualityComparer? comparer, string? message, string? actualExpression, string? expectedExpression)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (actual is null)
            return;

        AssertNotSetRelation(relation, EnumerateObjects(expected), EnumerateObjects(actual), new ObjectEqualityComparer(comparer), message, actualExpression, expectedExpression);
    }

    private static void AssertNotSetRelation<T>(SetRelation relation, IEnumerable<T> expected, IEnumerable<T>? actual, IEqualityComparer<T>? comparer, string? message, string? actualExpression, string? expectedExpression)
    {
        ArgumentNullException.ThrowIfNull(expected);
        if (actual is null)
            return;

        // The check consumes the sequences, so the message is built from snapshots rather than enumerating them again
        using var expectedSnapshot = CollectionSnapshot.Create<T>(expected);
        using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        if (!HasSetRelation(relation, actual, expectedSnapshot, actualSnapshot, comparer))
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeSetAssertionError(relation is SetRelation.ProperSubset ? nameof(NotProperSubset) : nameof(NotProperSuperset), GetExpectedSetRole(relation), ErrorFormatter.GetFormattedItems(expectedSnapshot), ErrorFormatter.GetFormattedItems(actualSnapshot), actualExpression, expectedExpression, message)));
    }
}
