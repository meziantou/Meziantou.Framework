using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    /// <summary>Asserts that a span contains a single item and returns it.</summary>
    /// <param name="actual">The span to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    [SuppressMessage("Naming", "CA1720:Identifier contains type name")]
    public static T Single<T>(ReadOnlySpan<T> actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual.Length is 1)
            return actual[0];

        throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanSingleAssertionError<T>(actual, actualExpression)));
    }

    /// <summary>Asserts that a character span contains a single character and returns it.</summary>
    /// <param name="actual">The span to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    [SuppressMessage("Naming", "CA1720:Identifier contains type name")]
    public static char Single(ReadOnlySpan<char> actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual.Length is 1)
            return actual[0];

        throw new AssertionException(ErrorFormatter.Format(new StringSingleAssertionError(actual, actualExpression)));
    }

    /// <summary>Asserts that a string contains a single character and returns it.</summary>
    /// <param name="actual">The string to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    [SuppressMessage("Naming", "CA1720:Identifier contains type name")]
    public static char Single(string actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual.Length is 1)
            return actual[0];

        throw new AssertionException(ErrorFormatter.Format(new StringSingleAssertionError(actual, actualExpression)));
    }

    /// <summary>Asserts that an enumerable contains a single item and returns it.</summary>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    [SuppressMessage("Naming", "CA1720:Identifier contains type name")]
    public static T Single<T>(IEnumerable<T> actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        using var actualSnapshot = CollectionSnapshot.Create<T>(actual);

        if (!actualSnapshot.TryGetItem(0, out var result))
        {
            throw new AssertionException(ErrorFormatter.Format(new CollectionSingleAssertionError<T>(actualSnapshot, actualExpression)));
        }

        if (!actualSnapshot.TryGetItem(1, out _))
            return result;

        throw new AssertionException(ErrorFormatter.Format(new CollectionSingleAssertionError<T>(actualSnapshot, actualExpression)));
    }

    /// <summary>Asserts that an enumerable contains a single item matching the predicate and returns it.</summary>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="predicate">The predicate used to select matching items.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="predicateExpression">The expression that produced the predicate.</param>
    [SuppressMessage("Naming", "CA1720:Identifier contains type name")]
    public static T Single<T>(IEnumerable<T> actual, Func<T, bool> predicate, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(predicate))] string? predicateExpression = null)
    {
        using var matchingSnapshot = CollectionSnapshot.Create<T>(EnumerateMatchingItems(actual, predicate));

        if (!matchingSnapshot.TryGetItem(0, out var result))
        {
            throw new AssertionException(ErrorFormatter.Format(new CollectionSinglePredicateAssertionError<T>(matchingSnapshot, actualExpression, predicateExpression)));
        }

        if (!matchingSnapshot.TryGetItem(1, out _))
            return result;

        throw new AssertionException(ErrorFormatter.Format(new CollectionSinglePredicateAssertionError<T>(matchingSnapshot, actualExpression, predicateExpression)));
    }

    /// <summary>Asserts that a non-generic enumerable contains a single item and returns it.</summary>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    [SuppressMessage("Naming", "CA1720:Identifier contains type name")]
    public static object? Single(System.Collections.IEnumerable actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        using var actualSnapshot = CollectionSnapshot.Create(actual);

        if (!actualSnapshot.TryGetItem(0, out var result))
        {
            throw new AssertionException(ErrorFormatter.Format(new CollectionSingleAssertionError<object?>(actualSnapshot, actualExpression)));
        }

        if (!actualSnapshot.TryGetItem(1, out _))
            return result;

        throw new AssertionException(ErrorFormatter.Format(new CollectionSingleAssertionError<object?>(actualSnapshot, actualExpression)));
    }

    /// <summary>Asserts that an asynchronous sequence contains a single item and returns it.</summary>
    /// <param name="actual">The sequence to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    [SuppressMessage("Naming", "CA1720:Identifier contains type name")]
    public static async Task<T> Single<T>(IAsyncEnumerable<T> actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        await using var actualSnapshot = CollectionSnapshot.Create<T>(actual);

        if (await actualSnapshot.TryGetItem(0).ConfigureAwait(false) is not (true, var result))
        {
            throw new AssertionException(await ErrorFormatter.FormatAsync(new AsyncCollectionSingleAssertionError<T>(actualSnapshot, actualExpression)).ConfigureAwait(false));
        }

        if (await actualSnapshot.TryGetItem(1).ConfigureAwait(false) is (false, _))
            return result;

        throw new AssertionException(await ErrorFormatter.FormatAsync(new AsyncCollectionSingleAssertionError<T>(actualSnapshot, actualExpression)).ConfigureAwait(false));
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
