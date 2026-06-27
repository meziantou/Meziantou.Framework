using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    public static void NotEqualUnordered<T>(IEnumerable<T> expected, IEnumerable<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        using var actualSnapshot = new CollectionSnapshot<T>(actual);
        using var expectedSnapshot = new CollectionSnapshot<T>(expected);
        EnsureComplete(actualSnapshot);
        EnsureComplete(expectedSnapshot);
        var (missingExpectedIndex, unexpectedActualIndex) = GetEqualUnorderedMismatch(expectedSnapshot.Items, actualSnapshot.Items, EqualityComparer<T>.Default.Equals);
        if (missingExpectedIndex is not null || unexpectedActualIndex is not null)
            return;

        throw new AssertionException(ErrorFormatter.Format(new NotEqualUnorderedAssertionError<IEnumerable<T>, IEnumerable<T>>("Not expected", expected, actual, actualExpression, expectedExpression, message)));
    }

    public static void NotEqualUnordered<T>(IEnumerable<T> expected, IEnumerable<T> actual, IEqualityComparer<T>? comparer, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        using var actualSnapshot = new CollectionSnapshot<T>(actual);
        using var expectedSnapshot = new CollectionSnapshot<T>(expected);
        EnsureComplete(actualSnapshot);
        EnsureComplete(expectedSnapshot);
        comparer ??= EqualityComparer<T>.Default;
        var (missingExpectedIndex, unexpectedActualIndex) = GetEqualUnorderedMismatch(expectedSnapshot.Items, actualSnapshot.Items, comparer.Equals);
        if (missingExpectedIndex is not null || unexpectedActualIndex is not null)
            return;

        throw new AssertionException(ErrorFormatter.Format(new NotEqualUnorderedAssertionError<IEnumerable<T>, IEnumerable<T>>("Not expected", expected, actual, actualExpression, expectedExpression, message)));
    }

    [OverloadResolutionPriority(-1)]
    public static void NotEqualUnordered<TExpected, TActual>(IEnumerable<TExpected> expected, IEnumerable<TActual> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        using var actualSnapshot = new CollectionSnapshot<TActual>(actual);
        using var expectedSnapshot = new CollectionSnapshot<TExpected>(expected);
        EnsureComplete(actualSnapshot);
        EnsureComplete(expectedSnapshot);
        var (missingExpectedIndex, unexpectedActualIndex) = GetEqualUnorderedMismatch(expectedSnapshot.Items, actualSnapshot.Items, (expectedItem, actualItem) => ValuesEqual(expectedItem, actualItem));
        if (missingExpectedIndex is not null || unexpectedActualIndex is not null)
            return;

        throw new AssertionException(ErrorFormatter.Format(new NotEqualUnorderedAssertionError<IEnumerable<TExpected>, IEnumerable<TActual>>("Not expected", expected, actual, actualExpression, expectedExpression, message)));
    }

    public static async Task NotEqualUnordered<T>(IAsyncEnumerable<T> expected, IAsyncEnumerable<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        await using var actualSnapshot = new AsyncCollectionSnapshot<T>(actual);
        await using var expectedSnapshot = new AsyncCollectionSnapshot<T>(expected);
        await EnsureCompleteAsync(actualSnapshot).ConfigureAwait(false);
        await EnsureCompleteAsync(expectedSnapshot).ConfigureAwait(false);
        var (missingExpectedIndex, unexpectedActualIndex) = GetEqualUnorderedMismatch(expectedSnapshot.Items, actualSnapshot.Items, EqualityComparer<T>.Default.Equals);
        if (missingExpectedIndex is not null || unexpectedActualIndex is not null)
            return;

        throw new AssertionException(ErrorFormatter.Format(new NotEqualUnorderedAssertionError<IReadOnlyList<T>, IReadOnlyList<T>>("Not expected", expectedSnapshot.Items, actualSnapshot.Items, actualExpression, expectedExpression, message)));
    }

    public static async Task NotEqualUnordered<T>(IAsyncEnumerable<T> expected, IAsyncEnumerable<T> actual, IEqualityComparer<T>? comparer, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        await using var actualSnapshot = new AsyncCollectionSnapshot<T>(actual);
        await using var expectedSnapshot = new AsyncCollectionSnapshot<T>(expected);
        await EnsureCompleteAsync(actualSnapshot).ConfigureAwait(false);
        await EnsureCompleteAsync(expectedSnapshot).ConfigureAwait(false);
        comparer ??= EqualityComparer<T>.Default;
        var (missingExpectedIndex, unexpectedActualIndex) = GetEqualUnorderedMismatch(expectedSnapshot.Items, actualSnapshot.Items, comparer.Equals);
        if (missingExpectedIndex is not null || unexpectedActualIndex is not null)
            return;

        throw new AssertionException(ErrorFormatter.Format(new NotEqualUnorderedAssertionError<IReadOnlyList<T>, IReadOnlyList<T>>("Not expected", expectedSnapshot.Items, actualSnapshot.Items, actualExpression, expectedExpression, message)));
    }

    [OverloadResolutionPriority(-1)]
    public static async Task NotEqualUnordered<TExpected, TActual>(IAsyncEnumerable<TExpected> expected, IAsyncEnumerable<TActual> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        await using var actualSnapshot = new AsyncCollectionSnapshot<TActual>(actual);
        await using var expectedSnapshot = new AsyncCollectionSnapshot<TExpected>(expected);
        await EnsureCompleteAsync(actualSnapshot).ConfigureAwait(false);
        await EnsureCompleteAsync(expectedSnapshot).ConfigureAwait(false);
        var (missingExpectedIndex, unexpectedActualIndex) = GetEqualUnorderedMismatch(expectedSnapshot.Items, actualSnapshot.Items, (expectedItem, actualItem) => ValuesEqual(expectedItem, actualItem));
        if (missingExpectedIndex is not null || unexpectedActualIndex is not null)
            return;

        throw new AssertionException(ErrorFormatter.Format(new NotEqualUnorderedAssertionError<IReadOnlyList<TExpected>, IReadOnlyList<TActual>>("Not expected", expectedSnapshot.Items, actualSnapshot.Items, actualExpression, expectedExpression, message)));
    }

    public static void NotEqualUnordered(System.Collections.IEnumerable expected, System.Collections.IEnumerable actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        using var expectedSnapshot = new CollectionSnapshot<object?>(EnumerateObjects(expected));
        using var actualSnapshot = new CollectionSnapshot<object?>(EnumerateObjects(actual));
        EnsureComplete(expectedSnapshot);
        EnsureComplete(actualSnapshot);
        var (missingExpectedIndex, unexpectedActualIndex) = GetEqualUnorderedMismatch(expectedSnapshot.Items, actualSnapshot.Items, (expectedItem, actualItem) => ValuesEqual(expectedItem, actualItem));
        if (missingExpectedIndex is not null || unexpectedActualIndex is not null)
            return;

        throw new AssertionException(ErrorFormatter.Format(new NotEqualUnorderedAssertionError<System.Collections.IEnumerable, System.Collections.IEnumerable>("Not expected", expected, actual, actualExpression, expectedExpression, message)));
    }

    public static void NotEqualUnordered(System.Collections.IEnumerable expected, System.Collections.IEnumerable actual, System.Collections.IEqualityComparer? comparer, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        using var expectedSnapshot = new CollectionSnapshot<object?>(EnumerateObjects(expected));
        using var actualSnapshot = new CollectionSnapshot<object?>(EnumerateObjects(actual));
        EnsureComplete(expectedSnapshot);
        EnsureComplete(actualSnapshot);
        var (missingExpectedIndex, unexpectedActualIndex) = GetEqualUnorderedMismatch(expectedSnapshot.Items, actualSnapshot.Items, (expectedItem, actualItem) => ValuesEqual(expectedItem, actualItem, comparer));
        if (missingExpectedIndex is not null || unexpectedActualIndex is not null)
            return;

        throw new AssertionException(ErrorFormatter.Format(new NotEqualUnorderedAssertionError<System.Collections.IEnumerable, System.Collections.IEnumerable>("Not expected", expected, actual, actualExpression, expectedExpression, message)));
    }
}
