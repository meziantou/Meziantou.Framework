using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    public static void NotProperSubset<T>(IEnumerable<T> expected, IEnumerable<T> actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        // The check consumes the sequences, so the message is built from snapshots rather than enumerating them again
        using var expectedSnapshot = CollectionSnapshot.Create<T>(expected);
        using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        if (!IsProperSubset(expected, expectedSnapshot, actualSnapshot, comparer))
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeSetAssertionError(ErrorFormatter.GetFormattedItems(expectedSnapshot), ErrorFormatter.GetFormattedItems(actualSnapshot), isSuperset: false, actualExpression, expectedExpression, message)));
    }

    public static void NotProperSubset(System.Collections.IEnumerable expected, System.Collections.IEnumerable actual, System.Collections.IEqualityComparer? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        NotProperSubset(EnumerateObjects(expected), EnumerateObjects(actual), new ObjectEqualityComparer(comparer), message, actualExpression, expectedExpression);
    }

    public static void NotProperSuperset<T>(IEnumerable<T> expected, IEnumerable<T> actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        // The check consumes the sequences, so the message is built from snapshots rather than enumerating them again
        using var expectedSnapshot = CollectionSnapshot.Create<T>(expected);
        using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        if (!IsProperSuperset(expected, expectedSnapshot, actualSnapshot, comparer))
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeSetAssertionError(ErrorFormatter.GetFormattedItems(expectedSnapshot), ErrorFormatter.GetFormattedItems(actualSnapshot), isSuperset: true, actualExpression, expectedExpression, message)));
    }

    public static void NotProperSuperset(System.Collections.IEnumerable expected, System.Collections.IEnumerable actual, System.Collections.IEqualityComparer? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        NotProperSuperset(EnumerateObjects(expected), EnumerateObjects(actual), new ObjectEqualityComparer(comparer), message, actualExpression, expectedExpression);
    }
}
