using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    /// <summary>Asserts that all items in a span satisfy the specified assertion.</summary>
    /// <param name="actual">The span to inspect.</param>
    /// <param name="assertion">The assertion to run for each item.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="assertionExpression">The expression that produced the assertion.</param>
    public static void All<T>(ReadOnlySpan<T> actual, Action<T> assertion, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(assertion))] string? assertionExpression = null)
    {
        for (var i = 0; i < actual.Length; i++)
        {
            try
            {
                assertion(actual[i]);
            }
            catch (Exception exception)
            {
                throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanAllAssertionError<T>(actual, i, exception, actualExpression, assertionExpression)), exception);
            }
        }
    }

    /// <summary>Asserts that all items in a span satisfy the specified assertion.</summary>
    /// <param name="actual">The span to inspect.</param>
    /// <param name="assertion">The assertion to run for each item and index.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="assertionExpression">The expression that produced the assertion.</param>
    public static void All<T>(ReadOnlySpan<T> actual, Action<T, int> assertion, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(assertion))] string? assertionExpression = null)
    {
        for (var i = 0; i < actual.Length; i++)
        {
            try
            {
                assertion(actual[i], i);
            }
            catch (Exception exception)
            {
                throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanAllAssertionError<T>(actual, i, exception, actualExpression, assertionExpression)), exception);
            }
        }
    }

    /// <summary>Asserts that all items in an enumerable satisfy the specified predicate.</summary>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="predicate">The predicate that every item must satisfy.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="predicateExpression">The expression that produced the predicate.</param>
    public static void All<T>(IEnumerable<T> actual, Func<T, bool> predicate, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(predicate))] string? predicateExpression = null)
    {
        using var actualSnapshot = new CollectionSnapshot<T>(actual);

        var index = 0;
        foreach (var item in actualSnapshot)
        {
            if (!predicate(item))
            {
                throw new AssertionException(ErrorFormatter.Format(new CollectionAllPredicateAssertionError<T>(actualSnapshot, index, actualExpression, predicateExpression)));
            }

            index++;
        }
    }

    /// <summary>Asserts that all items in an enumerable satisfy the specified assertion.</summary>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="assertion">The assertion to run for each item.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="assertionExpression">The expression that produced the assertion.</param>
    public static void All<T>(IEnumerable<T> actual, Action<T> assertion, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(assertion))] string? assertionExpression = null)
    {
        All(actual, (item, _) => assertion(item), actualExpression, assertionExpression);
    }

    /// <summary>Asserts that all items in an enumerable satisfy the specified assertion.</summary>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="assertion">The assertion to run for each item and index.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="assertionExpression">The expression that produced the assertion.</param>
    public static void All<T>(IEnumerable<T> actual, Action<T, int> assertion, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(assertion))] string? assertionExpression = null)
    {
        using var actualSnapshot = new CollectionSnapshot<T>(actual);

        var index = 0;
        foreach (var item in actualSnapshot)
        {
            try
            {
                assertion(item, index);
            }
            catch (Exception exception)
            {
                throw new AssertionException(ErrorFormatter.Format(new CollectionAllAssertionError<T>(actualSnapshot, index, exception, actualExpression, assertionExpression)), exception);
            }

            index++;
        }
    }

    /// <summary>Asserts that all items in a non-generic enumerable satisfy the specified assertion.</summary>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="assertion">The assertion to run for each item.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="assertionExpression">The expression that produced the assertion.</param>
    public static void All(System.Collections.IEnumerable actual, Action<object?> assertion, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(assertion))] string? assertionExpression = null)
    {
        All(actual, (item, _) => assertion(item), actualExpression, assertionExpression);
    }

    /// <summary>Asserts that all items in a non-generic enumerable satisfy the specified assertion.</summary>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="assertion">The assertion to run for each item and index.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="assertionExpression">The expression that produced the assertion.</param>
    public static void All(System.Collections.IEnumerable actual, Action<object?, int> assertion, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(assertion))] string? assertionExpression = null)
    {
        All(EnumerateObjects(actual), assertion, actualExpression, assertionExpression);
    }

    /// <summary>Asserts that all items in an asynchronous sequence satisfy the specified assertion.</summary>
    /// <param name="actual">The sequence to inspect.</param>
    /// <param name="assertion">The assertion to run for each item.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="assertionExpression">The expression that produced the assertion.</param>
    public static Task All<T>(IAsyncEnumerable<T> actual, Action<T> assertion, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(assertion))] string? assertionExpression = null)
    {
        return All(actual, (item, _) => assertion(item), actualExpression, assertionExpression);
    }

    /// <summary>Asserts that all items in an asynchronous sequence satisfy the specified assertion.</summary>
    /// <param name="actual">The sequence to inspect.</param>
    /// <param name="assertion">The assertion to run for each item and index.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="assertionExpression">The expression that produced the assertion.</param>
    public static async Task All<T>(IAsyncEnumerable<T> actual, Action<T, int> assertion, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(assertion))] string? assertionExpression = null)
    {
        await All(actual, (item, index) =>
        {
            assertion(item, index);
            return Task.CompletedTask;
        }, actualExpression, assertionExpression).ConfigureAwait(false);
    }

    /// <summary>Asserts that all items in an asynchronous sequence satisfy the specified asynchronous assertion.</summary>
    /// <param name="actual">The sequence to inspect.</param>
    /// <param name="assertion">The assertion to run for each item.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="assertionExpression">The expression that produced the assertion.</param>
    public static Task All<T>(IAsyncEnumerable<T> actual, Func<T, Task> assertion, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(assertion))] string? assertionExpression = null)
    {
        return All(actual, (item, _) => assertion(item), actualExpression, assertionExpression);
    }

    /// <summary>Asserts that all items in an asynchronous sequence satisfy the specified asynchronous assertion.</summary>
    /// <param name="actual">The sequence to inspect.</param>
    /// <param name="assertion">The assertion to run for each item and index.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="assertionExpression">The expression that produced the assertion.</param>
    public static async Task All<T>(IAsyncEnumerable<T> actual, Func<T, int, Task> assertion, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(assertion))] string? assertionExpression = null)
    {
        await using var actualSnapshot = new AsyncCollectionSnapshot<T>(actual);

        var index = 0;
        await foreach (var item in ((IAsyncEnumerable<T>)actualSnapshot).ConfigureAwait(false))
        {
            try
            {
                await assertion(item, index).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                throw new AssertionException(await ErrorFormatter.FormatAsync(new AsyncCollectionAllAssertionError<T>(actualSnapshot, index, exception, actualExpression, assertionExpression)).ConfigureAwait(false), exception);
            }

            index++;
        }
    }
}
