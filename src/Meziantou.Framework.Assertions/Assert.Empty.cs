using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

partial class Assert
{
    /// <summary>
    /// Asserts that a span is empty.
    /// </summary>
    /// <param name="actual">The span to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    public static void Empty<T>(ReadOnlySpan<T> actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual.IsEmpty)
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new ReadOnlySpanEmptyAssertionError<T>(actual, actualExpression)));
    }

    /// <summary>
    /// Asserts that a string is empty.
    /// </summary>
    /// <param name="actual">The string to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    public static void Empty(string actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual.Length == 0)
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new StringEmptyAssertionError(actual, actualExpression)));
    }

    /// <summary>
    /// Asserts that an enumerable is empty.
    /// </summary>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    public static void Empty<T>(IEnumerable<T> actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        using var actualSnapshot = new CollectionSnapshot<T>(actual);
        using var actualEnumerator = actualSnapshot.GetEnumerator();

        if (!actualEnumerator.MoveNext())
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new CollectionEmptyAssertionError<T>(actualSnapshot, actualExpression)));
    }

    /// <summary>
    /// Asserts that a non-generic enumerable is empty.
    /// </summary>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    public static void Empty(System.Collections.IEnumerable actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        using var actualSnapshot = new CollectionSnapshot<object?>(EnumerateObjects(actual));
        using var actualEnumerator = actualSnapshot.GetEnumerator();

        if (!actualEnumerator.MoveNext())
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new CollectionEmptyAssertionError<object?>(actualSnapshot, actualExpression)));
    }

    /// <summary>
    /// Asserts that an asynchronous sequence is empty.
    /// </summary>
    /// <param name="actual">The sequence to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    public static async Task Empty<T>(IAsyncEnumerable<T> actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        await using var actualSnapshot = new AsyncCollectionSnapshot<T>(actual);
        await using var actualEnumerator = actualSnapshot.GetAsyncEnumerator();

        if (!await actualEnumerator.MoveNextAsync().ConfigureAwait(false))
            return;

        throw new AssertionException(await AssertionFormatter.Default.FormatAsync(new AsyncCollectionEmptyAssertionError<T>(actualSnapshot, actualExpression)).ConfigureAwait(false));
    }
}
