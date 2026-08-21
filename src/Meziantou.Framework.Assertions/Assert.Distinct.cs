using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    /// <summary>Asserts that a span does not contain duplicate items.</summary>
    /// <param name="actual">The span to inspect.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    public static void Distinct<T>(ReadOnlySpan<T> actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;

        if (actual.Length <= LinearDuplicateSearchThreshold)
        {
            for (var duplicateIndex = 1; duplicateIndex < actual.Length; duplicateIndex++)
            {
                var firstIndex = IndexOf(actual[..duplicateIndex], actual[duplicateIndex], comparer);
                if (firstIndex >= 0)
                {
                    throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanDistinctAssertionError<T>(actual, duplicateIndex, firstIndex, actualExpression, message)));
                }
            }

            return;
        }

        var firstIndexes = new FirstIndexLookup<T>(comparer, actual.Length);
        for (var duplicateIndex = 0; duplicateIndex < actual.Length; duplicateIndex++)
        {
            var firstIndex = firstIndexes.Add(actual[duplicateIndex], duplicateIndex);
            if (firstIndex >= 0)
            {
                throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanDistinctAssertionError<T>(actual, duplicateIndex, firstIndex, actualExpression, message)));
            }
        }
    }

    /// <summary>Asserts that a string does not contain duplicate characters.</summary>
    /// <param name="actual">The string to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    public static void Distinct(string actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        Distinct(actual.AsSpan(), comparer: null, message: message, actualExpression: actualExpression);
    }

    /// <summary>Asserts that an enumerable does not contain duplicate items.</summary>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    public static void Distinct<T>(IEnumerable<T> actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;
        using var actualSnapshot = CollectionSnapshot.Create<T>(actual);

        FirstIndexLookup<T>? firstIndexes = null;
        for (var duplicateIndex = 0; actualSnapshot.TryGetItem(duplicateIndex, out var item); duplicateIndex++)
        {
            int firstIndex;
            if (duplicateIndex < LinearDuplicateSearchThreshold)
            {
                firstIndex = IndexOf(actualSnapshot.Items, duplicateIndex, item, comparer);
            }
            else
            {
                firstIndexes ??= FirstIndexLookup<T>.Create(actualSnapshot.Items, duplicateIndex, comparer);
                firstIndex = firstIndexes.Add(item, duplicateIndex);
            }

            if (firstIndex >= 0)
            {
                throw new AssertionException(ErrorFormatter.Format(new CollectionDistinctAssertionError<T>(actualSnapshot, duplicateIndex, firstIndex, actualExpression, message)));
            }
        }
    }

    /// <summary>Asserts that a non-generic enumerable does not contain duplicate items.</summary>
    /// <param name="actual">The enumerable to inspect.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    public static void Distinct(System.Collections.IEnumerable actual, System.Collections.IEqualityComparer? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        using var actualSnapshot = CollectionSnapshot.Create(actual);

        for (var duplicateIndex = 0; actualSnapshot.TryGetItem(duplicateIndex, out var item); duplicateIndex++)
        {
            var firstIndex = IndexOf(actualSnapshot.Items, duplicateIndex, item, comparer);
            if (firstIndex >= 0)
            {
                throw new AssertionException(ErrorFormatter.Format(new CollectionDistinctAssertionError<object?>(actualSnapshot, duplicateIndex, firstIndex, actualExpression, message)));
            }
        }
    }

    /// <summary>Asserts that an asynchronous sequence does not contain duplicate items.</summary>
    /// <param name="actual">The sequence to inspect.</param>
    /// <param name="comparer">The comparer used to compare values.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    public static async Task Distinct<T>(IAsyncEnumerable<T> actual, IEqualityComparer<T>? comparer = null, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        comparer ??= EqualityComparer<T>.Default;
        await using var actualSnapshot = CollectionSnapshot.Create<T>(actual);

        FirstIndexLookup<T>? firstIndexes = null;
        for (var duplicateIndex = 0; await actualSnapshot.TryGetItem(duplicateIndex).ConfigureAwait(false) is (true, var item); duplicateIndex++)
        {
            int firstIndex;
            if (duplicateIndex < LinearDuplicateSearchThreshold)
            {
                firstIndex = IndexOf(actualSnapshot.Items, duplicateIndex, item, comparer);
            }
            else
            {
                firstIndexes ??= FirstIndexLookup<T>.Create(actualSnapshot.Items, duplicateIndex, comparer);
                firstIndex = firstIndexes.Add(item, duplicateIndex);
            }

            if (firstIndex >= 0)
            {
                throw new AssertionException(await ErrorFormatter.FormatAsync(new AsyncCollectionDistinctAssertionError<T>(actualSnapshot, duplicateIndex, firstIndex, actualExpression, message)).ConfigureAwait(false));
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

    /// <summary>
    /// Number of leading items compared with a linear scan before switching to a hash lookup. Small collections are
    /// the common case in assertions and the scan allocates nothing for them; the quadratic work stays bounded by
    /// the square of this value. Around this size the hash lookup starts winning despite the dictionary it allocates.
    /// </summary>
    private const int LinearDuplicateSearchThreshold = 64;

    /// <summary>Maps each item to the index where it was first seen.</summary>
    private sealed class FirstIndexLookup<T>
    {
        private readonly Dictionary<NullableKey<T>, int> _indexes;

        public FirstIndexLookup(IEqualityComparer<T> comparer, int capacity)
        {
            _indexes = new Dictionary<NullableKey<T>, int>(capacity, NullableKeyComparer<T>.Create(comparer));
        }

        public static FirstIndexLookup<T> Create(IReadOnlyList<T> items, int count, IEqualityComparer<T> comparer)
        {
            var lookup = new FirstIndexLookup<T>(comparer, count);
            for (var index = 0; index < count; index++)
            {
                lookup.Add(items[index], index);
            }

            return lookup;
        }

        /// <summary>Records <paramref name="item"/> and returns -1, or returns the index where it was first seen.</summary>
        public int Add(T item, int index)
        {
            var key = new NullableKey<T>(item);
            if (_indexes.TryAdd(key, index))
                return -1;

            return _indexes[key];
        }
    }
}
