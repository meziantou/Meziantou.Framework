using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    /// <summary>Asserts that an array is not null, contains a single item and returns it.</summary>
    /// <param name="actual">The array to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <remarks>Arrays would otherwise bind to the <see cref="ReadOnlySpan{T}"/> overload, which turns a null array into an empty span.</remarks>
    [OverloadResolutionPriority(1)]
    [SuppressMessage("Naming", "CA1720:Identifier contains type name")]
    public static T Single<T>([NotNull] T[]? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual is null)
        {
            ThrowNullCollection(nameof(Single), actualExpression, message);
        }

        return Single(new ReadOnlySpan<T>(actual), message, actualExpression);
    }

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
    public static char Single([NotNull] string? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual is null)
        {
            ThrowNullCollection(nameof(Single), actualExpression, message);
        }

        if (actual.Length is 1)
            return actual[0];

        throw new AssertionException(ErrorFormatter.Format(new StringSingleAssertionError(actual, actualExpression, message)));
    }

    /// <summary>Asserts that an enumerable contains a single item and returns it.</summary>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    [SuppressMessage("Naming", "CA1720:Identifier contains type name")]
    public static T Single<T>([NotNull] IEnumerable<T>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual is null)
        {
            ThrowNullCollection(nameof(Single), actualExpression, message);
        }

        // A list with a single item needs no snapshot
        if (actual is IList<T> { Count: 1 } list)
            return list[0];

        if (actual is IReadOnlyList<T> { Count: 1 } readOnlyList)
            return readOnlyList[0];

        using var actualSnapshot = CollectionSnapshot.Create<T>(actual);

        if (!actualSnapshot.TryGetItem(0, out var result))
        {
            throw new AssertionException(ErrorFormatter.Format(new CollectionSingleAssertionError<T>(actualSnapshot, actualExpression, message)));
        }

        if (!actualSnapshot.TryGetItem(1, out _))
            return result;

        throw new AssertionException(ErrorFormatter.Format(new CollectionSingleAssertionError<T>(actualSnapshot, actualExpression, message)));
    }

    /// <summary>Asserts that an enumerable contains a single item matching the predicate and returns it.</summary>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="predicate">The predicate used to select matching items.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="predicateExpression">The expression that produced the predicate.</param>
    [SuppressMessage("Naming", "CA1720:Identifier contains type name")]
    public static T Single<T>([NotNull] IEnumerable<T>? actual, Func<T, bool> predicate, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(predicate))] string? predicateExpression = null)
    {
        if (actual is null)
            throw new AssertionException(ErrorFormatter.Format(new PredicateNullActualAssertionError(nameof(Single), actualExpression, predicateExpression, message)));

        using var matchingSnapshot = CollectionSnapshot.Create<T>(EnumerateMatchingItems(actual, predicate));

        if (!matchingSnapshot.TryGetItem(0, out var result))
        {
            throw new AssertionException(ErrorFormatter.Format(new CollectionSinglePredicateAssertionError<T>(matchingSnapshot, actualExpression, predicateExpression, message)));
        }

        if (!matchingSnapshot.TryGetItem(1, out _))
            return result;

        throw new AssertionException(ErrorFormatter.Format(new CollectionSinglePredicateAssertionError<T>(matchingSnapshot, actualExpression, predicateExpression, message)));
    }

    /// <summary>Asserts that a non-generic enumerable contains a single item and returns it.</summary>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    [SuppressMessage("Naming", "CA1720:Identifier contains type name")]
    public static object? Single([NotNull] System.Collections.IEnumerable? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual is null)
        {
            ThrowNullCollection(nameof(Single), actualExpression, message);
        }

        // The indexer of a multi-dimensional array throws
        if (actual is System.Collections.IList { Count: 1 } list and not Array { Rank: > 1 })
            return list[0];

        using var actualSnapshot = CollectionSnapshot.Create(actual);

        if (!actualSnapshot.TryGetItem(0, out var result))
        {
            throw new AssertionException(ErrorFormatter.Format(new CollectionSingleAssertionError<object?>(actualSnapshot, actualExpression, message)));
        }

        if (!actualSnapshot.TryGetItem(1, out _))
            return result;

        throw new AssertionException(ErrorFormatter.Format(new CollectionSingleAssertionError<object?>(actualSnapshot, actualExpression, message)));
    }

    /// <summary>Asserts that an asynchronous sequence contains a single item and returns it.</summary>
    /// <param name="actual">The sequence to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    [SuppressMessage("Naming", "CA1720:Identifier contains type name")]
    public static async Task<T> Single<T>([NotNull] IAsyncEnumerable<T>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual is null)
        {
            ThrowNullCollection(nameof(Single), actualExpression, message);
        }

        await using var actualSnapshot = CollectionSnapshot.Create<T>(actual);

        if (await actualSnapshot.TryGetItem(0).ConfigureAwait(false) is not (true, var result))
        {
            throw new AssertionException(await ErrorFormatter.FormatAsync(new AsyncCollectionSingleAssertionError<T>(actualSnapshot, actualExpression, message)).ConfigureAwait(false));
        }

        if (await actualSnapshot.TryGetItem(1).ConfigureAwait(false) is (false, _))
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
