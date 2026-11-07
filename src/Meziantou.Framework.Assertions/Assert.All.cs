using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    /// <summary>Asserts that an array is not null and all its items satisfy the specified assertion.</summary>
    /// <param name="actual">The array to inspect.</param>
    /// <param name="assertion">The assertion to run for each item.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="assertionExpression">The expression that produced the assertion.</param>
    /// <remarks>Arrays would otherwise bind to the <see cref="ReadOnlySpan{T}"/> overload, which turns a null array into an empty span.</remarks>
    [OverloadResolutionPriority(1)]
    public static void All<T>([NotNull] T[]? actual, Action<T> assertion, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(assertion))] string? assertionExpression = null)
    {
        if (actual is null)
        {
            ThrowNullCollection(nameof(All), actualExpression, message);
        }

        All(new ReadOnlySpan<T>(actual), assertion, message, actualExpression, assertionExpression);
    }

    /// <summary>Asserts that an array is not null and all its items satisfy the specified assertion.</summary>
    /// <param name="actual">The array to inspect.</param>
    /// <param name="assertion">The assertion to run for each item and index.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="assertionExpression">The expression that produced the assertion.</param>
    /// <remarks>Arrays would otherwise bind to the <see cref="ReadOnlySpan{T}"/> overload, which turns a null array into an empty span.</remarks>
    [OverloadResolutionPriority(1)]
    public static void All<T>([NotNull] T[]? actual, Action<T, int> assertion, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(assertion))] string? assertionExpression = null)
    {
        if (actual is null)
        {
            ThrowNullCollection(nameof(All), actualExpression, message);
        }

        All(new ReadOnlySpan<T>(actual), assertion, message, actualExpression, assertionExpression);
    }

    /// <summary>Asserts that an array is not null and all its items satisfy the specified predicate.</summary>
    /// <param name="actual">The array to inspect.</param>
    /// <param name="predicate">The predicate that every item must satisfy.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="predicateExpression">The expression that produced the predicate.</param>
    /// <remarks>
    /// Without this overload, the priority of the <see cref="Action{T}"/> array overload would bind a lambda that returns
    /// a value, such as <c>x => set.Add(x)</c>, to the assertion overload and ignore its result.
    /// </remarks>
    [OverloadResolutionPriority(1)]
    public static void All<T>([NotNull] T[]? actual, Func<T, bool> predicate, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(predicate))] string? predicateExpression = null)
    {
        All((IEnumerable<T>?)actual, predicate, message, actualExpression, predicateExpression);
    }

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
            catch (Exception exception) when (!IsXunitSkipException(exception))
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
            catch (Exception exception) when (!IsXunitSkipException(exception))
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
    public static void All<T>([NotNull] IEnumerable<T>? actual, Func<T, bool> predicate, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(predicate))] string? predicateExpression = null)
    {
        if (actual is null)
            throw new AssertionException(ErrorFormatter.Format(new PredicateNullActualAssertionError(nameof(All), actualExpression, predicateExpression, message)));

        using var actualSnapshot = CollectionSnapshot.CreateSinglePass<T>(actual);

        for (var index = 0; actualSnapshot.TryGetItem(index, out var item); index++)
        {
            if (!predicate(item))
            {
                actualSnapshot.StopDiscardingItems();
                throw new AssertionException(ErrorFormatter.Format(new CollectionAllPredicateAssertionError<T>(actualSnapshot, index, actualExpression, predicateExpression, message)));
            }
        }
    }

    /// <summary>Asserts that all items in an enumerable satisfy the specified assertion.</summary>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="assertion">The assertion to run for each item.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="assertionExpression">The expression that produced the assertion.</param>
    public static void All<T>([NotNull] IEnumerable<T>? actual, Action<T> assertion, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(assertion))] string? assertionExpression = null)
    {
        if (actual is null)
        {
            ThrowNullCollection(nameof(All), actualExpression, message);
        }

        using var actualSnapshot = CollectionSnapshot.CreateSinglePass<T>(actual);

        for (var index = 0; actualSnapshot.TryGetItem(index, out var item); index++)
        {
            try
            {
                assertion(item);
            }
            catch (Exception exception) when (!IsXunitSkipException(exception))
            {
                actualSnapshot.StopDiscardingItems();
                throw new AssertionException(ErrorFormatter.Format(new CollectionAllAssertionError<T>(actualSnapshot, index, exception, actualExpression, assertionExpression, message)), exception);
            }
        }
    }

    /// <summary>Asserts that all items in an enumerable satisfy the specified assertion.</summary>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="assertion">The assertion to run for each item and index.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="assertionExpression">The expression that produced the assertion.</param>
    public static void All<T>([NotNull] IEnumerable<T>? actual, Action<T, int> assertion, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(assertion))] string? assertionExpression = null)
    {
        if (actual is null)
        {
            ThrowNullCollection(nameof(All), actualExpression, message);
        }

        using var actualSnapshot = CollectionSnapshot.CreateSinglePass<T>(actual);

        for (var index = 0; actualSnapshot.TryGetItem(index, out var item); index++)
        {
            try
            {
                assertion(item, index);
            }
            catch (Exception exception) when (!IsXunitSkipException(exception))
            {
                actualSnapshot.StopDiscardingItems();
                throw new AssertionException(ErrorFormatter.Format(new CollectionAllAssertionError<T>(actualSnapshot, index, exception, actualExpression, assertionExpression, message)), exception);
            }
        }
    }

    /// <summary>Asserts that all items in a non-generic enumerable satisfy the specified assertion.</summary>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="assertion">The assertion to run for each item.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="assertionExpression">The expression that produced the assertion.</param>
    public static void All([NotNull] System.Collections.IEnumerable? actual, Action<object?> assertion, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(assertion))] string? assertionExpression = null)
    {
        if (actual is null)
        {
            ThrowNullCollection(nameof(All), actualExpression, message);
        }

        All(actual, (item, _) => assertion(item), message, actualExpression, assertionExpression);
    }

    /// <summary>Asserts that all items in a non-generic enumerable satisfy the specified assertion.</summary>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="assertion">The assertion to run for each item and index.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="assertionExpression">The expression that produced the assertion.</param>
    public static void All([NotNull] System.Collections.IEnumerable? actual, Action<object?, int> assertion, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(assertion))] string? assertionExpression = null)
    {
        if (actual is null)
        {
            ThrowNullCollection(nameof(All), actualExpression, message);
        }

        All(EnumerateObjects(actual), assertion, message, actualExpression, assertionExpression);
    }

    /// <summary>Asserts that all items in an asynchronous sequence satisfy the specified assertion.</summary>
    /// <param name="actual">The sequence to inspect.</param>
    /// <param name="assertion">The assertion to run for each item.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="assertionExpression">The expression that produced the assertion.</param>
    public static async Task All<T>([NotNull] IAsyncEnumerable<T>? actual, Action<T> assertion, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(assertion))] string? assertionExpression = null)
    {
        if (actual is null)
        {
            ThrowNullCollection(nameof(All), actualExpression, message);
        }

        await All(actual, (item, _) => assertion(item), message, actualExpression, assertionExpression).ConfigureAwait(false);
    }

    /// <summary>Asserts that all items in an asynchronous sequence satisfy the specified assertion.</summary>
    /// <param name="actual">The sequence to inspect.</param>
    /// <param name="assertion">The assertion to run for each item and index.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="assertionExpression">The expression that produced the assertion.</param>
    public static async Task All<T>([NotNull] IAsyncEnumerable<T>? actual, Action<T, int> assertion, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(assertion))] string? assertionExpression = null)
    {
        if (actual is null)
        {
            ThrowNullCollection(nameof(All), actualExpression, message);
        }

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
    public static async Task All<T>([NotNull] IAsyncEnumerable<T>? actual, Func<T, Task> assertion, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(assertion))] string? assertionExpression = null)
    {
        if (actual is null)
        {
            ThrowNullCollection(nameof(All), actualExpression, message);
        }

        await All(actual, (item, _) => assertion(item), message, actualExpression, assertionExpression).ConfigureAwait(false);
    }

    /// <summary>Asserts that all items in an asynchronous sequence satisfy the specified asynchronous assertion.</summary>
    /// <param name="actual">The sequence to inspect.</param>
    /// <param name="assertion">The assertion to run for each item and index.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <param name="assertionExpression">The expression that produced the assertion.</param>
    public static async Task All<T>([NotNull] IAsyncEnumerable<T>? actual, Func<T, int, Task> assertion, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(assertion))] string? assertionExpression = null)
    {
        if (actual is null)
        {
            ThrowNullCollection(nameof(All), actualExpression, message);
        }

        await using var actualSnapshot = CollectionSnapshot.CreateSinglePass<T>(actual);

        for (var index = 0; await actualSnapshot.TryGetItem(index).ConfigureAwait(false) is (true, var item); index++)
        {
            try
            {
                await assertion(item, index).ConfigureAwait(false);
            }
            catch (Exception exception) when (!IsXunitSkipException(exception))
            {
                actualSnapshot.StopDiscardingItems();
                throw new AssertionException(await ErrorFormatter.FormatAsync(new AsyncCollectionAllAssertionError<T>(actualSnapshot, index, exception, actualExpression, assertionExpression, message)).ConfigureAwait(false), exception);
            }
        }
    }
}
