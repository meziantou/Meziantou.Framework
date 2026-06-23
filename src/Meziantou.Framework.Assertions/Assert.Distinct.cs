using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

partial class Assert
{
    /// <summary>
    /// Asserts that a span does not contain duplicate items.
    /// </summary>
    /// <param name="actual">The span to inspect.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    public static void Distinct<T>(ReadOnlySpan<T> actual, IEqualityComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;

        for (var duplicateIndex = 1; duplicateIndex < actual.Length; duplicateIndex++)
        {
            var firstIndex = IndexOf(actual[..duplicateIndex], actual[duplicateIndex], comparer);
            if (firstIndex >= 0)
            {
                throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanDistinctAssertionError<T>(actual, duplicateIndex, firstIndex, actualExpression)));
            }
        }
    }

    /// <summary>
    /// Asserts that a string does not contain duplicate characters.
    /// </summary>
    /// <param name="actual">The string to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    public static void Distinct(string actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        Distinct(actual.AsSpan(), comparer: null, actualExpression);
    }

    /// <summary>
    /// Asserts that an enumerable does not contain duplicate items.
    /// </summary>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    public static void Distinct<T>(IEnumerable<T> actual, IEqualityComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;
        using var actualSnapshot = new CollectionSnapshot<T>(actual);

        foreach (var item in actualSnapshot)
        {
            var duplicateIndex = actualSnapshot.Items.Count - 1;
            var firstIndex = IndexOf(actualSnapshot.Items, duplicateIndex, item, comparer);
            if (firstIndex >= 0)
            {
                throw new AssertionException(ErrorFormatter.Format(new CollectionDistinctAssertionError<T>(actualSnapshot, duplicateIndex, firstIndex, actualExpression)));
            }
        }
    }

    /// <summary>
    /// Asserts that a non-generic enumerable does not contain duplicate items.
    /// </summary>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    public static void Distinct(System.Collections.IEnumerable actual, System.Collections.IEqualityComparer? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        using var actualSnapshot = new CollectionSnapshot<object?>(EnumerateObjects(actual));

        foreach (var item in actualSnapshot)
        {
            var duplicateIndex = actualSnapshot.Items.Count - 1;
            var firstIndex = IndexOf(actualSnapshot.Items, duplicateIndex, item, comparer);
            if (firstIndex >= 0)
            {
                throw new AssertionException(ErrorFormatter.Format(new CollectionDistinctAssertionError<object?>(actualSnapshot, duplicateIndex, firstIndex, actualExpression)));
            }
        }
    }

    /// <summary>
    /// Asserts that an asynchronous sequence does not contain duplicate items.
    /// </summary>
    /// <param name="actual">The sequence to inspect.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    public static async Task Distinct<T>(IAsyncEnumerable<T> actual, IEqualityComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;
        await using var actualSnapshot = new AsyncCollectionSnapshot<T>(actual);

        await foreach (var item in ((IAsyncEnumerable<T>)actualSnapshot).ConfigureAwait(false))
        {
            var duplicateIndex = actualSnapshot.Items.Count - 1;
            var firstIndex = IndexOf(actualSnapshot.Items, duplicateIndex, item, comparer);
            if (firstIndex >= 0)
            {
                throw new AssertionException(await ErrorFormatter.FormatAsync(new AsyncCollectionDistinctAssertionError<T>(actualSnapshot, duplicateIndex, firstIndex, actualExpression)).ConfigureAwait(false));
            }
        }
    }

    private static int IndexOf<T>(ReadOnlySpan<T> items, T item, IEqualityComparer<T> comparer)
    {
        for (var i = 0; i < items.Length; i++)
        {
            if (comparer.Equals(items[i], item))
                return i;
        }

        return -1;
    }

    private static int IndexOf<T>(IReadOnlyList<T> items, int count, T item, IEqualityComparer<T> comparer)
    {
        for (var i = 0; i < count; i++)
        {
            if (comparer.Equals(items[i], item))
                return i;
        }

        return -1;
    }

    private static int IndexOf(IReadOnlyList<object?> items, int count, object? item, System.Collections.IEqualityComparer? comparer)
    {
        for (var i = 0; i < count; i++)
        {
            if (Equals(items[i], item, comparer))
                return i;
        }

        return -1;
    }
}
