using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    /// <summary>Asserts that a span contains a single item and returns it.</summary>
    /// <param name="actual">The span to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    [SuppressMessage("Naming", "CA1720:Identifier contains type name")]
    public static T Single<T>(ReadOnlySpan<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual.Length is 1)
            return actual[0];

        throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanSingleAssertionError<T>(actual, actualExpression, message)));
    }

    /// <summary>Asserts that a character span contains a single character and returns it.</summary>
    /// <param name="actual">The span to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    [SuppressMessage("Naming", "CA1720:Identifier contains type name")]
    public static char Single(ReadOnlySpan<char> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual.Length is 1)
            return actual[0];

        throw new AssertionException(ErrorFormatter.Format(new StringSingleAssertionError(actual, actualExpression, message)));
    }

    /// <summary>Asserts that a string contains a single character and returns it.</summary>
    /// <param name="actual">The string to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    [SuppressMessage("Naming", "CA1720:Identifier contains type name")]
    public static char Single(string actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual.Length is 1)
            return actual[0];

        throw new AssertionException(ErrorFormatter.Format(new StringSingleAssertionError(actual, actualExpression, message)));
    }

    /// <summary>Asserts that an enumerable contains a single item and returns it.</summary>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    [SuppressMessage("Naming", "CA1720:Identifier contains type name")]
    public static T Single<T>(IEnumerable<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        using var actualSnapshot = new CollectionSnapshot<T>(actual);
        using var actualEnumerator = actualSnapshot.GetEnumerator();

        if (!actualEnumerator.MoveNext())
        {
            throw new AssertionException(ErrorFormatter.Format(new CollectionSingleAssertionError<T>(actualSnapshot, actualExpression, message)));
        }

        var result = actualEnumerator.Current;
        if (!actualEnumerator.MoveNext())
            return result;

        throw new AssertionException(ErrorFormatter.Format(new CollectionSingleAssertionError<T>(actualSnapshot, actualExpression, message)));
    }

    /// <summary>Asserts that an enumerable contains a single item matching the predicate and returns it.</summary>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="predicate">The predicate used to select matching items.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="predicateExpression">The expression that produced the predicate.</param>
    [SuppressMessage("Naming", "CA1720:Identifier contains type name")]
    public static T Single<T>(IEnumerable<T> actual, Func<T, bool> predicate, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(predicate))] string? predicateExpression = null)
    {
        using var matchingSnapshot = new CollectionSnapshot<T>(EnumerateMatchingItems(actual, predicate));
        using var matchingEnumerator = matchingSnapshot.GetEnumerator();

        if (!matchingEnumerator.MoveNext())
        {
            throw new AssertionException(ErrorFormatter.Format(new CollectionSinglePredicateAssertionError<T>(matchingSnapshot, actualExpression, predicateExpression, message)));
        }

        var result = matchingEnumerator.Current;
        if (!matchingEnumerator.MoveNext())
            return result;

        throw new AssertionException(ErrorFormatter.Format(new CollectionSinglePredicateAssertionError<T>(matchingSnapshot, actualExpression, predicateExpression, message)));
    }

    /// <summary>Asserts that a non-generic enumerable contains a single item and returns it.</summary>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    [SuppressMessage("Naming", "CA1720:Identifier contains type name")]
    public static object? Single(System.Collections.IEnumerable actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        using var actualSnapshot = new CollectionSnapshot<object?>(EnumerateObjects(actual));
        using var actualEnumerator = actualSnapshot.GetEnumerator();

        if (!actualEnumerator.MoveNext())
        {
            throw new AssertionException(ErrorFormatter.Format(new CollectionSingleAssertionError<object?>(actualSnapshot, actualExpression, message)));
        }

        var result = actualEnumerator.Current;
        if (!actualEnumerator.MoveNext())
            return result;

        throw new AssertionException(ErrorFormatter.Format(new CollectionSingleAssertionError<object?>(actualSnapshot, actualExpression, message)));
    }

    /// <summary>Asserts that an asynchronous sequence contains a single item and returns it.</summary>
    /// <param name="actual">The sequence to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    [SuppressMessage("Naming", "CA1720:Identifier contains type name")]
    public static async Task<T> Single<T>(IAsyncEnumerable<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        await using var actualSnapshot = new AsyncCollectionSnapshot<T>(actual);
        await using var actualEnumerator = actualSnapshot.GetAsyncEnumerator();

        if (!await actualEnumerator.MoveNextAsync().ConfigureAwait(false))
        {
            throw new AssertionException(await ErrorFormatter.FormatAsync(new AsyncCollectionSingleAssertionError<T>(actualSnapshot, actualExpression, message)).ConfigureAwait(false));
        }

        var result = actualEnumerator.Current;
        if (!await actualEnumerator.MoveNextAsync().ConfigureAwait(false))
            return result;

        throw new AssertionException(await ErrorFormatter.FormatAsync(new AsyncCollectionSingleAssertionError<T>(actualSnapshot, actualExpression, message)).ConfigureAwait(false));
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
