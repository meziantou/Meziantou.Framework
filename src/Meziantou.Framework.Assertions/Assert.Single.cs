using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

#pragma warning disable CA1720 // Assertion method name intentionally matches the established Assert.Single API name.
partial class Assert
{
    /// <summary>
    /// Asserts that a span contains a single item and returns it.
    /// </summary>
    /// <param name="actual">The span to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    public static T Single<T>(ReadOnlySpan<T> actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual.Length == 1)
            return actual[0];

        throw new AssertionException(AssertionFormatter.Default.Format(new ReadOnlySpanSingleAssertionError<T>(actual, actualExpression)));
    }

    /// <summary>
    /// Asserts that a character span contains a single character and returns it.
    /// </summary>
    /// <param name="actual">The span to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    public static char Single(ReadOnlySpan<char> actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual.Length == 1)
            return actual[0];

        throw new AssertionException(AssertionFormatter.Default.Format(new StringSingleAssertionError(actual, actualExpression)));
    }

    /// <summary>
    /// Asserts that a string contains a single character and returns it.
    /// </summary>
    /// <param name="actual">The string to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    public static char Single(string actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual.Length == 1)
            return actual[0];

        throw new AssertionException(AssertionFormatter.Default.Format(new StringSingleAssertionError(actual, actualExpression)));
    }

    /// <summary>
    /// Asserts that an enumerable contains a single item and returns it.
    /// </summary>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    public static T Single<T>(IEnumerable<T> actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        using var actualSnapshot = new CollectionSnapshot<T>(actual);
        using var actualEnumerator = actualSnapshot.GetEnumerator();

        if (!actualEnumerator.MoveNext())
        {
            throw new AssertionException(AssertionFormatter.Default.Format(new CollectionSingleAssertionError<T>(actualSnapshot, actualExpression)));
        }

        var result = actualEnumerator.Current;
        if (!actualEnumerator.MoveNext())
            return result;

        throw new AssertionException(AssertionFormatter.Default.Format(new CollectionSingleAssertionError<T>(actualSnapshot, actualExpression)));
    }

    /// <summary>
    /// Asserts that an enumerable contains a single item matching the predicate and returns it.
    /// </summary>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="predicate">The predicate used to select matching items.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="predicateExpression">The expression that produced the predicate.</param>
    public static T Single<T>(IEnumerable<T> actual, Func<T, bool> predicate, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(predicate))] string? predicateExpression = null)
    {
        using var matchingSnapshot = new CollectionSnapshot<T>(EnumerateMatchingItems(actual, predicate));
        using var matchingEnumerator = matchingSnapshot.GetEnumerator();

        if (!matchingEnumerator.MoveNext())
        {
            throw new AssertionException(AssertionFormatter.Default.Format(new CollectionSinglePredicateAssertionError<T>(matchingSnapshot, actualExpression, predicateExpression)));
        }

        var result = matchingEnumerator.Current;
        if (!matchingEnumerator.MoveNext())
            return result;

        throw new AssertionException(AssertionFormatter.Default.Format(new CollectionSinglePredicateAssertionError<T>(matchingSnapshot, actualExpression, predicateExpression)));
    }

    /// <summary>
    /// Asserts that a non-generic enumerable contains a single item and returns it.
    /// </summary>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    public static object? Single(System.Collections.IEnumerable actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        using var actualSnapshot = new CollectionSnapshot<object?>(EnumerateObjects(actual));
        using var actualEnumerator = actualSnapshot.GetEnumerator();

        if (!actualEnumerator.MoveNext())
        {
            throw new AssertionException(AssertionFormatter.Default.Format(new CollectionSingleAssertionError<object?>(actualSnapshot, actualExpression)));
        }

        var result = actualEnumerator.Current;
        if (!actualEnumerator.MoveNext())
            return result;

        throw new AssertionException(AssertionFormatter.Default.Format(new CollectionSingleAssertionError<object?>(actualSnapshot, actualExpression)));
    }

    /// <summary>
    /// Asserts that an asynchronous sequence contains a single item and returns it.
    /// </summary>
    /// <param name="actual">The sequence to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    public static async Task<T> Single<T>(IAsyncEnumerable<T> actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        await using var actualSnapshot = new AsyncCollectionSnapshot<T>(actual);
        await using var actualEnumerator = actualSnapshot.GetAsyncEnumerator();

        if (!await actualEnumerator.MoveNextAsync().ConfigureAwait(false))
        {
            throw new AssertionException(await AssertionFormatter.Default.FormatAsync(new AsyncCollectionSingleAssertionError<T>(actualSnapshot, actualExpression)).ConfigureAwait(false));
        }

        var result = actualEnumerator.Current;
        if (!await actualEnumerator.MoveNextAsync().ConfigureAwait(false))
            return result;

        throw new AssertionException(await AssertionFormatter.Default.FormatAsync(new AsyncCollectionSingleAssertionError<T>(actualSnapshot, actualExpression)).ConfigureAwait(false));
    }

    private static IEnumerable<T> EnumerateMatchingItems<T>(IEnumerable<T> actual, Func<T, bool> predicate)
    {
        foreach (var item in actual)
        {
            if (predicate(item))
                yield return item;
        }
    }
}
#pragma warning restore CA1720
