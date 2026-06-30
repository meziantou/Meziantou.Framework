using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    /// <summary>Asserts that a span is empty.</summary>
    /// <param name="actual">The span to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    public static void Empty<T>(ReadOnlySpan<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual.IsEmpty)
            return;

        throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanEmptyAssertionError<T>(actual, actualExpression, message)));
    }

    /// <summary>Asserts that a string is empty.</summary>
    /// <param name="actual">The string to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    public static void Empty(string actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual.Length == 0)
            return;

        throw new AssertionException(ErrorFormatter.Format(new StringEmptyAssertionError(actual, actualExpression, message)));
    }

    /// <summary>Asserts that an enumerable is empty.</summary>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    public static void Empty<T>(IEnumerable<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        using var actualSnapshot = CollectionSnapshot.Create<T>(actual);

        if (!actualSnapshot.TryGetItem(0, out _))
            return;

        throw new AssertionException(ErrorFormatter.Format(new CollectionEmptyAssertionError<T>(actualSnapshot, actualExpression, message)));
    }

    /// <summary>Asserts that a non-generic enumerable is empty.</summary>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    public static void Empty(System.Collections.IEnumerable actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        using var actualSnapshot = CollectionSnapshot.Create(actual);

        if (!actualSnapshot.TryGetItem(0, out _))
            return;

        throw new AssertionException(ErrorFormatter.Format(new CollectionEmptyAssertionError<object?>(actualSnapshot, actualExpression, message)));
    }

    /// <summary>Asserts that an asynchronous sequence is empty.</summary>
    /// <param name="actual">The sequence to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    public static async Task Empty<T>(IAsyncEnumerable<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        await using var actualSnapshot = CollectionSnapshot.Create<T>(actual);

        if (await actualSnapshot.TryGetItem(0).ConfigureAwait(false) is (false, _))
            return;

        throw new AssertionException(await ErrorFormatter.FormatAsync(new AsyncCollectionEmptyAssertionError<T>(actualSnapshot, actualExpression, message)).ConfigureAwait(false));
    }
}
