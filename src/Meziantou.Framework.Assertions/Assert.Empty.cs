using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    /// <summary>Asserts that an array is not null and is empty.</summary>
    /// <param name="actual">The array to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <remarks>Arrays would otherwise bind to the <see cref="ReadOnlySpan{T}"/> overload, which turns a null array into an empty span.</remarks>
    [OverloadResolutionPriority(1)]
    public static void Empty<T>([NotNull] T[]? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual is null)
        {
            ThrowNullCollection(nameof(Empty), actualExpression, message);
        }

        Empty(new ReadOnlySpan<T>(actual), message, actualExpression);
    }

    /// <summary>Asserts that a span is empty.</summary>
    /// <param name="actual">The span to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    public static void Empty<T>(ReadOnlySpan<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual.IsEmpty)
            return;

        throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanEmptyAssertionError<T>(actual, actualExpression, message)));
    }

    /// <summary>Asserts that a string is not null and is empty.</summary>
    /// <param name="actual">The string to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    public static void Empty([NotNull] string? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual is null)
        {
            ThrowNullCollection(nameof(Empty), actualExpression, message);
        }

        if (actual.Length == 0)
            return;

        throw new AssertionException(ErrorFormatter.Format(new StringEmptyAssertionError(actual, actualExpression, message)));
    }

    /// <summary>Asserts that an enumerable is not null and is empty.</summary>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    public static void Empty<T>([NotNull] IEnumerable<T>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual is null)
        {
            ThrowNullCollection(nameof(Empty), actualExpression, message);
        }

        if (TryGetKnownCount(actual, out var knownCount) && knownCount == 0)
            return;

        using var actualSnapshot = CollectionSnapshot.Create<T>(actual);

        if (!actualSnapshot.TryGetItem(0, out _))
            return;

        throw new AssertionException(ErrorFormatter.Format(new CollectionEmptyAssertionError<T>(actualSnapshot, actualExpression, message)));
    }

    /// <summary>Asserts that a non-generic enumerable is not null and is empty.</summary>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    public static void Empty([NotNull] System.Collections.IEnumerable? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual is null)
        {
            ThrowNullCollection(nameof(Empty), actualExpression, message);
        }

        if (actual is System.Collections.ICollection { Count: 0 })
            return;

        using var actualSnapshot = CollectionSnapshot.Create(actual);

        if (!actualSnapshot.TryGetItem(0, out _))
            return;

        throw new AssertionException(ErrorFormatter.Format(new CollectionEmptyAssertionError<object?>(actualSnapshot, actualExpression, message)));
    }

    /// <summary>Asserts that an asynchronous sequence is not null and is empty.</summary>
    /// <param name="actual">The sequence to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    public static async Task Empty<T>([NotNull] IAsyncEnumerable<T>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual is null)
        {
            ThrowNullCollection(nameof(Empty), actualExpression, message);
        }

        await using var actualSnapshot = CollectionSnapshot.Create<T>(actual);

        if (await actualSnapshot.TryGetItem(0).ConfigureAwait(false) is (false, _))
            return;

        throw new AssertionException(await ErrorFormatter.FormatAsync(new AsyncCollectionEmptyAssertionError<T>(actualSnapshot, actualExpression, message)).ConfigureAwait(false));
    }
}
