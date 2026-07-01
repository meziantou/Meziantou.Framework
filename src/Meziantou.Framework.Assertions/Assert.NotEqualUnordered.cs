using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    public static void NotEqualUnordered<T>(IEnumerable<T> expected, IEnumerable<T>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
            return;

        using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        using var expectedSnapshot = CollectionSnapshot.Create<T>(expected);
        actualSnapshot.EnsureComplete();
        expectedSnapshot.EnsureComplete();
        if (!EqualUnorderedUsingDefaultComparer(expectedSnapshot.Items, actualSnapshot.Items))
            return;

        throw new AssertionException(ErrorFormatter.Format(new NotEqualUnorderedAssertionError<IEnumerable<T>, IEnumerable<T>>("Not expected", expected, actual, actualExpression, expectedExpression, message)));
    }

    public static void NotEqualUnordered<T>(IEnumerable<T> expected, IEnumerable<T>? actual, IEqualityComparer<T>? comparer, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
            return;

        using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        using var expectedSnapshot = CollectionSnapshot.Create<T>(expected);
        actualSnapshot.EnsureComplete();
        expectedSnapshot.EnsureComplete();
        if (comparer is null)
        {
            if (!EqualUnorderedUsingDefaultComparer(expectedSnapshot.Items, actualSnapshot.Items))
                return;
        }
        else
        {
            var (missingExpectedIndex, unexpectedActualIndex) = GetEqualUnorderedMismatch(expectedSnapshot.Items, actualSnapshot.Items, comparer);
            if (missingExpectedIndex is not null || unexpectedActualIndex is not null)
                return;
        }

        throw new AssertionException(ErrorFormatter.Format(new NotEqualUnorderedAssertionError<IEnumerable<T>, IEnumerable<T>>("Not expected", expected, actual, actualExpression, expectedExpression, message)));
    }

    [OverloadResolutionPriority(-1)]
    public static void NotEqualUnordered<TExpected, TActual>(IEnumerable<TExpected> expected, IEnumerable<TActual>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
            return;

        using var actualSnapshot = CollectionSnapshot.Create<TActual>(actual);
        using var expectedSnapshot = CollectionSnapshot.Create<TExpected>(expected);
        actualSnapshot.EnsureComplete();
        expectedSnapshot.EnsureComplete();
        var (missingExpectedIndex, unexpectedActualIndex) = GetEqualUnorderedMismatch(expectedSnapshot.Items, actualSnapshot.Items, (System.Collections.IEqualityComparer?)null);
        if (missingExpectedIndex is not null || unexpectedActualIndex is not null)
            return;

        throw new AssertionException(ErrorFormatter.Format(new NotEqualUnorderedAssertionError<IEnumerable<TExpected>, IEnumerable<TActual>>("Not expected", expected, actual, actualExpression, expectedExpression, message)));
    }

    public static async Task NotEqualUnordered<T>(IAsyncEnumerable<T> expected, IAsyncEnumerable<T>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
            return;

        await using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        await using var expectedSnapshot = CollectionSnapshot.Create<T>(expected);
        await actualSnapshot.EnsureCompleteAsync().ConfigureAwait(false);
        await expectedSnapshot.EnsureCompleteAsync().ConfigureAwait(false);
        if (!EqualUnorderedUsingDefaultComparer(expectedSnapshot.Items, actualSnapshot.Items))
            return;

        throw new AssertionException(ErrorFormatter.Format(new NotEqualUnorderedAssertionError<IReadOnlyList<T>, IReadOnlyList<T>>("Not expected", expectedSnapshot.Items, actualSnapshot.Items, actualExpression, expectedExpression, message)));
    }

    public static async Task NotEqualUnordered<T>(IAsyncEnumerable<T> expected, IAsyncEnumerable<T>? actual, IEqualityComparer<T>? comparer, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
            return;

        await using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        await using var expectedSnapshot = CollectionSnapshot.Create<T>(expected);
        await actualSnapshot.EnsureCompleteAsync().ConfigureAwait(false);
        await expectedSnapshot.EnsureCompleteAsync().ConfigureAwait(false);
        if (comparer is null)
        {
            if (!EqualUnorderedUsingDefaultComparer(expectedSnapshot.Items, actualSnapshot.Items))
                return;
        }
        else
        {
            var (missingExpectedIndex, unexpectedActualIndex) = GetEqualUnorderedMismatch(expectedSnapshot.Items, actualSnapshot.Items, comparer);
            if (missingExpectedIndex is not null || unexpectedActualIndex is not null)
                return;
        }

        throw new AssertionException(ErrorFormatter.Format(new NotEqualUnorderedAssertionError<IReadOnlyList<T>, IReadOnlyList<T>>("Not expected", expectedSnapshot.Items, actualSnapshot.Items, actualExpression, expectedExpression, message)));
    }

    [OverloadResolutionPriority(-1)]
    public static async Task NotEqualUnordered<TExpected, TActual>(IAsyncEnumerable<TExpected> expected, IAsyncEnumerable<TActual>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
            return;

        await using var actualSnapshot = CollectionSnapshot.Create<TActual>(actual);
        await using var expectedSnapshot = CollectionSnapshot.Create<TExpected>(expected);
        await actualSnapshot.EnsureCompleteAsync().ConfigureAwait(false);
        await expectedSnapshot.EnsureCompleteAsync().ConfigureAwait(false);
        var (missingExpectedIndex, unexpectedActualIndex) = GetEqualUnorderedMismatch(expectedSnapshot.Items, actualSnapshot.Items, (System.Collections.IEqualityComparer?)null);
        if (missingExpectedIndex is not null || unexpectedActualIndex is not null)
            return;

        throw new AssertionException(ErrorFormatter.Format(new NotEqualUnorderedAssertionError<IReadOnlyList<TExpected>, IReadOnlyList<TActual>>("Not expected", expectedSnapshot.Items, actualSnapshot.Items, actualExpression, expectedExpression, message)));
    }

    public static void NotEqualUnordered(System.Collections.IEnumerable expected, System.Collections.IEnumerable? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
            return;

        using var expectedSnapshot = CollectionSnapshot.Create(expected);
        using var actualSnapshot = CollectionSnapshot.Create(actual);
        expectedSnapshot.EnsureComplete();
        actualSnapshot.EnsureComplete();
        var (missingExpectedIndex, unexpectedActualIndex) = GetEqualUnorderedMismatch(expectedSnapshot.Items, actualSnapshot.Items, (System.Collections.IEqualityComparer?)null);
        if (missingExpectedIndex is not null || unexpectedActualIndex is not null)
            return;

        throw new AssertionException(ErrorFormatter.Format(new NotEqualUnorderedAssertionError<System.Collections.IEnumerable, System.Collections.IEnumerable>("Not expected", expected, actual, actualExpression, expectedExpression, message)));
    }

    public static void NotEqualUnordered(System.Collections.IEnumerable expected, System.Collections.IEnumerable? actual, System.Collections.IEqualityComparer? comparer, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
            return;

        using var expectedSnapshot = CollectionSnapshot.Create(expected);
        using var actualSnapshot = CollectionSnapshot.Create(actual);
        expectedSnapshot.EnsureComplete();
        actualSnapshot.EnsureComplete();
        var (missingExpectedIndex, unexpectedActualIndex) = GetEqualUnorderedMismatch(expectedSnapshot.Items, actualSnapshot.Items, comparer);
        if (missingExpectedIndex is not null || unexpectedActualIndex is not null)
            return;

        throw new AssertionException(ErrorFormatter.Format(new NotEqualUnorderedAssertionError<System.Collections.IEnumerable, System.Collections.IEnumerable>("Not expected", expected, actual, actualExpression, expectedExpression, message)));
    }
}
