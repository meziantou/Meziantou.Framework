using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    public static void EqualUnordered<T>(IEnumerable<T> expected, IEnumerable<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        EqualUnorderedCollections<T>(expected, actual, EqualityComparer<T>.Default, message, actualExpression, expectedExpression);
    }

    public static void EqualUnordered<T>(IEnumerable<T> expected, IEnumerable<T> actual, IEqualityComparer<T>? comparer, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        EqualUnorderedCollections(expected, actual, comparer, message, actualExpression, expectedExpression);
    }

    [OverloadResolutionPriority(-1)]
    public static void EqualUnordered<TExpected, TActual>(IEnumerable<TExpected> expected, IEnumerable<TActual> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        EqualUnorderedCollections(expected, actual, comparer: (System.Collections.IEqualityComparer?)null, message, actualExpression, expectedExpression);
    }

    private static void EqualUnorderedCollections<T>(IEnumerable<T> expected, IEnumerable<T> actual, IEqualityComparer<T>? comparer, string? message, string? actualExpression, string? expectedExpression)
    {
        using var actualSnapshot = new CollectionSnapshot<T>(actual);
        using var expectedSnapshot = new CollectionSnapshot<T>(expected);
        EnsureComplete(actualSnapshot);
        EnsureComplete(expectedSnapshot);
        comparer ??= EqualityComparer<T>.Default;

        var (missingExpectedIndex, unexpectedActualIndex) = GetEqualUnorderedMismatch(expectedSnapshot.Items, actualSnapshot.Items, comparer.Equals);
        if (missingExpectedIndex is not null || unexpectedActualIndex is not null)
        {
            throw new AssertionException(AssertionFormatter.Default.Format(new CollectionEqualUnorderedAssertionError<T, T>(expectedSnapshot, actualSnapshot, missingExpectedIndex, unexpectedActualIndex, message, actualExpression, expectedExpression)));
        }
    }

    private static void EqualUnorderedCollections<TExpected, TActual>(IEnumerable<TExpected> expected, IEnumerable<TActual> actual, System.Collections.IEqualityComparer? comparer, string? message, string? actualExpression, string? expectedExpression)
    {
        using var actualSnapshot = new CollectionSnapshot<TActual>(actual);
        using var expectedSnapshot = new CollectionSnapshot<TExpected>(expected);
        EnsureComplete(actualSnapshot);
        EnsureComplete(expectedSnapshot);

        var (missingExpectedIndex, unexpectedActualIndex) = GetEqualUnorderedMismatch(expectedSnapshot.Items, actualSnapshot.Items, (expectedItem, actualItem) => ValuesEqual(expectedItem, actualItem, comparer));
        if (missingExpectedIndex is not null || unexpectedActualIndex is not null)
        {
            throw new AssertionException(AssertionFormatter.Default.Format(new CollectionEqualUnorderedAssertionError<TExpected, TActual>(expectedSnapshot, actualSnapshot, missingExpectedIndex, unexpectedActualIndex, message, actualExpression, expectedExpression)));
        }
    }

    public static async Task EqualUnordered<T>(IAsyncEnumerable<T> expected, IAsyncEnumerable<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        await EqualUnorderedAsyncCollections<T>(expected, actual, EqualityComparer<T>.Default, message, actualExpression, expectedExpression).ConfigureAwait(false);
    }

    public static async Task EqualUnordered<T>(IAsyncEnumerable<T> expected, IAsyncEnumerable<T> actual, IEqualityComparer<T>? comparer, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        await EqualUnorderedAsyncCollections(expected, actual, comparer, message, actualExpression, expectedExpression).ConfigureAwait(false);
    }

    [OverloadResolutionPriority(-1)]
    public static async Task EqualUnordered<TExpected, TActual>(IAsyncEnumerable<TExpected> expected, IAsyncEnumerable<TActual> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        await EqualUnorderedAsyncCollections(expected, actual, comparer: (System.Collections.IEqualityComparer?)null, message, actualExpression, expectedExpression).ConfigureAwait(false);
    }

    private static async Task EqualUnorderedAsyncCollections<T>(IAsyncEnumerable<T> expected, IAsyncEnumerable<T> actual, IEqualityComparer<T>? comparer, string? message, string? actualExpression, string? expectedExpression)
    {
        await using var actualSnapshot = new AsyncCollectionSnapshot<T>(actual);
        await using var expectedSnapshot = new AsyncCollectionSnapshot<T>(expected);
        await EnsureCompleteAsync(actualSnapshot).ConfigureAwait(false);
        await EnsureCompleteAsync(expectedSnapshot).ConfigureAwait(false);
        comparer ??= EqualityComparer<T>.Default;

        var (missingExpectedIndex, unexpectedActualIndex) = GetEqualUnorderedMismatch(expectedSnapshot.Items, actualSnapshot.Items, comparer.Equals);
        if (missingExpectedIndex is not null || unexpectedActualIndex is not null)
        {
            throw new AssertionException(await AssertionFormatter.Default.FormatAsync(new AsyncCollectionEqualUnorderedAssertionError<T, T>(expectedSnapshot, actualSnapshot, missingExpectedIndex, unexpectedActualIndex, message, actualExpression, expectedExpression)).ConfigureAwait(false));
        }
    }

    private static async Task EqualUnorderedAsyncCollections<TExpected, TActual>(IAsyncEnumerable<TExpected> expected, IAsyncEnumerable<TActual> actual, System.Collections.IEqualityComparer? comparer, string? message, string? actualExpression, string? expectedExpression)
    {
        await using var actualSnapshot = new AsyncCollectionSnapshot<TActual>(actual);
        await using var expectedSnapshot = new AsyncCollectionSnapshot<TExpected>(expected);
        await EnsureCompleteAsync(actualSnapshot).ConfigureAwait(false);
        await EnsureCompleteAsync(expectedSnapshot).ConfigureAwait(false);

        var (missingExpectedIndex, unexpectedActualIndex) = GetEqualUnorderedMismatch(expectedSnapshot.Items, actualSnapshot.Items, (expectedItem, actualItem) => ValuesEqual(expectedItem, actualItem, comparer));
        if (missingExpectedIndex is not null || unexpectedActualIndex is not null)
        {
            throw new AssertionException(await AssertionFormatter.Default.FormatAsync(new AsyncCollectionEqualUnorderedAssertionError<TExpected, TActual>(expectedSnapshot, actualSnapshot, missingExpectedIndex, unexpectedActualIndex, message, actualExpression, expectedExpression)).ConfigureAwait(false));
        }
    }

    public static void EqualUnordered(System.Collections.IEnumerable expected, System.Collections.IEnumerable actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        EqualUnordered(expected, actual, comparer: null, message, actualExpression, expectedExpression);
    }

    public static void EqualUnordered(System.Collections.IEnumerable expected, System.Collections.IEnumerable actual, System.Collections.IEqualityComparer? comparer, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        EqualUnorderedCollections(EnumerateObjects(expected), EnumerateObjects(actual), comparer, message, actualExpression, expectedExpression);
    }

    private static (int? MissingExpectedIndex, int? UnexpectedActualIndex) GetEqualUnorderedMismatch<TExpected, TActual>(IReadOnlyList<TExpected> expected, IReadOnlyList<TActual> actual, Func<TExpected, TActual, bool> equals)
    {
        var matchedActualIndexes = new bool[actual.Count];
        int? missingExpectedIndex = null;

        for (var expectedIndex = 0; expectedIndex < expected.Count; expectedIndex++)
        {
            var found = false;
            for (var actualIndex = 0; actualIndex < actual.Count; actualIndex++)
            {
                if (matchedActualIndexes[actualIndex])
                    continue;

                if (!equals(expected[expectedIndex], actual[actualIndex]))
                    continue;

                matchedActualIndexes[actualIndex] = true;
                found = true;
                break;
            }

            if (!found && missingExpectedIndex is null)
            {
                missingExpectedIndex = expectedIndex;
            }
        }

        int? unexpectedActualIndex = null;
        for (var actualIndex = 0; actualIndex < matchedActualIndexes.Length; actualIndex++)
        {
            if (!matchedActualIndexes[actualIndex])
            {
                unexpectedActualIndex = actualIndex;
                break;
            }
        }

        return (missingExpectedIndex, unexpectedActualIndex);
    }
}
