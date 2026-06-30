using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    /// <summary>Asserts that all items in a span satisfy the specified assertion.</summary>
    /// <param name="actual">The span to inspect.</param>
    /// <param name="assertion">The assertion to run for each item.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="assertionExpression">The expression that produced the assertion.</param>
    public static void All<T>(ReadOnlySpan<T> actual, Action<T> assertion, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(assertion))] string? assertionExpression = null)
    {
        for (var i = 0; i < actual.Length; i++)
        {
            try
            {
                assertion(actual[i]);
            }
            catch (Exception exception)
            {
                throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanAllAssertionError<T>(actual, i, exception, actualExpression, assertionExpression, message)), exception);
            }
        }
    }

    /// <summary>Asserts that all items in a span satisfy the specified assertion.</summary>
    /// <param name="actual">The span to inspect.</param>
    /// <param name="assertion">The assertion to run for each item and index.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="assertionExpression">The expression that produced the assertion.</param>
    public static void All<T>(ReadOnlySpan<T> actual, Action<T, int> assertion, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(assertion))] string? assertionExpression = null)
    {
        for (var i = 0; i < actual.Length; i++)
        {
            try
            {
                assertion(actual[i], i);
            }
            catch (Exception exception)
            {
                throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanAllAssertionError<T>(actual, i, exception, actualExpression, assertionExpression, message)), exception);
            }
        }
    }

    /// <summary>Asserts that all items in an enumerable satisfy the specified predicate.</summary>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="predicate">The predicate that every item must satisfy.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="predicateExpression">The expression that produced the predicate.</param>
    public static void All<T>(IEnumerable<T> actual, Func<T, bool> predicate, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(predicate))] string? predicateExpression = null)
    {
        using var actualSnapshot = new CollectionSnapshot<T>(actual);

        var index = 0;
        foreach (var item in actualSnapshot)
        {
            if (!predicate(item))
            {
                throw new AssertionException(ErrorFormatter.Format(new CollectionAllPredicateAssertionError<T>(actualSnapshot, index, actualExpression, predicateExpression, message)));
            }

            index++;
        }
    }

    /// <summary>Asserts that all items in an enumerable satisfy the specified assertion.</summary>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="assertion">The assertion to run for each item.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="assertionExpression">The expression that produced the assertion.</param>
    public static void All<T>(IEnumerable<T> actual, Action<T> assertion, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(assertion))] string? assertionExpression = null)
    {
        All(actual, (item, _) => assertion(item), message, actualExpression, assertionExpression);
    }

    /// <summary>Asserts that all items in an enumerable satisfy the specified assertion.</summary>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="assertion">The assertion to run for each item and index.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="assertionExpression">The expression that produced the assertion.</param>
    public static void All<T>(IEnumerable<T> actual, Action<T, int> assertion, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(assertion))] string? assertionExpression = null)
    {
        using var actualSnapshot = CollectionSnapshot.Create<T>(actual);

        for (var index = 0; actualSnapshot.TryGetItem(index, out var item); index++)
        {
            try
            {
                assertion(item, index);
            }
            catch (Exception exception)
            {
                throw new AssertionException(ErrorFormatter.Format(new CollectionAllAssertionError<T>(actualSnapshot, index, exception, actualExpression, assertionExpression, message)), exception);
            }
        }
    }

    /// <summary>Asserts that all items in a non-generic enumerable satisfy the specified assertion.</summary>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="assertion">The assertion to run for each item.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="assertionExpression">The expression that produced the assertion.</param>
    public static void All(System.Collections.IEnumerable actual, Action<object?> assertion, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(assertion))] string? assertionExpression = null)
    {
        All(actual, (item, _) => assertion(item), message, actualExpression, assertionExpression);
    }

    /// <summary>Asserts that all items in a non-generic enumerable satisfy the specified assertion.</summary>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="assertion">The assertion to run for each item and index.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="assertionExpression">The expression that produced the assertion.</param>
    public static void All(System.Collections.IEnumerable actual, Action<object?, int> assertion, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(assertion))] string? assertionExpression = null)
    {
        All(EnumerateObjects(actual), assertion, message, actualExpression, assertionExpression);
    }

    /// <summary>Asserts that all items in an asynchronous sequence satisfy the specified assertion.</summary>
    /// <param name="actual">The sequence to inspect.</param>
    /// <param name="assertion">The assertion to run for each item.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="assertionExpression">The expression that produced the assertion.</param>
    public static async Task All<T>(IAsyncEnumerable<T> actual, Action<T> assertion, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(assertion))] string? assertionExpression = null)
    {
        await All(actual, (item, _) => assertion(item), message, actualExpression, assertionExpression).ConfigureAwait(false);
    }

    /// <summary>Asserts that all items in an asynchronous sequence satisfy the specified assertion.</summary>
    /// <param name="actual">The sequence to inspect.</param>
    /// <param name="assertion">The assertion to run for each item and index.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="assertionExpression">The expression that produced the assertion.</param>
    public static async Task All<T>(IAsyncEnumerable<T> actual, Action<T, int> assertion, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(assertion))] string? assertionExpression = null)
    {
        await All(actual, (item, index) =>
        {
            assertion(item, index);
            return Task.CompletedTask;
        }, message, actualExpression, assertionExpression).ConfigureAwait(false);
    }

    /// <summary>Asserts that all items in an asynchronous sequence satisfy the specified asynchronous assertion.</summary>
    /// <param name="actual">The sequence to inspect.</param>
    /// <param name="assertion">The assertion to run for each item.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="assertionExpression">The expression that produced the assertion.</param>
    public static async Task All<T>(IAsyncEnumerable<T> actual, Func<T, Task> assertion, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(assertion))] string? assertionExpression = null)
    {
        await All(actual, (item, _) => assertion(item), message, actualExpression, assertionExpression).ConfigureAwait(false);
    }

    /// <summary>Asserts that all items in an asynchronous sequence satisfy the specified asynchronous assertion.</summary>
    /// <param name="actual">The sequence to inspect.</param>
    /// <param name="assertion">The assertion to run for each item and index.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="assertionExpression">The expression that produced the assertion.</param>
    public static async Task All<T>(IAsyncEnumerable<T> actual, Func<T, int, Task> assertion, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(assertion))] string? assertionExpression = null)
    {
        await using var actualSnapshot = CollectionSnapshot.Create<T>(actual);

        for (var index = 0; await actualSnapshot.TryGetItem(index).ConfigureAwait(false) is (true, var item); index++)
        {
            try
            {
                await assertion(item, index).ConfigureAwait(false);
            }
            catch (Exception exception)
            {
                throw new AssertionException(await ErrorFormatter.FormatAsync(new AsyncCollectionAllAssertionError<T>(actualSnapshot, index, exception, actualExpression, assertionExpression, message)).ConfigureAwait(false), exception);
            }
        }
    }
}
