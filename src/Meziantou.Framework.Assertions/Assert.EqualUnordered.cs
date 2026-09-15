using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    public static void EqualUnordered<T>(IEnumerable<T> expected, [NotNull] IEnumerable<T>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<IEnumerable<T>>(nameof(EqualUnordered), "Expected expression", "Expected", expected, actualExpression, expectedExpression, message)));
        }

        EqualUnorderedCollections<T>(expected, actual, EqualityComparer<T>.Default, message, actualExpression, expectedExpression);
    }

    public static void EqualUnordered<T>(IEnumerable<T> expected, [NotNull] IEnumerable<T>? actual, IEqualityComparer<T>? comparer, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<IEnumerable<T>>(nameof(EqualUnordered), "Expected expression", "Expected", expected, actualExpression, expectedExpression, message)));
        }

        if (comparer is null)
        {
            EqualUnorderedCollections<T>(expected, actual, EqualityComparer<T>.Default, message, actualExpression, expectedExpression);
            return;
        }

        EqualUnorderedCollections(expected, actual, comparer, message, actualExpression, expectedExpression);
    }

    [OverloadResolutionPriority(-1)]
    public static void EqualUnordered<TExpected, TActual>(IEnumerable<TExpected> expected, [NotNull] IEnumerable<TActual>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<IEnumerable<TExpected>>(nameof(EqualUnordered), "Expected expression", "Expected", expected, actualExpression, expectedExpression, message)));
        }

        EqualUnorderedCollections(expected, actual, comparer: (System.Collections.IEqualityComparer?)null, message, actualExpression, expectedExpression);
    }

    private static void EqualUnorderedCollections<T>(IEnumerable<T> expected, IEnumerable<T> actual, IEqualityComparer<T>? comparer, string? message, string? actualExpression, string? expectedExpression)
    {
        using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        using var expectedSnapshot = CollectionSnapshot.Create<T>(expected);
        actualSnapshot.EnsureComplete();
        expectedSnapshot.EnsureComplete();
        comparer ??= EqualityComparer<T>.Default;

        if (GetEqualUnorderedDifference(expectedSnapshot.Items, actualSnapshot.Items, comparer) is not { } difference)
            return;

        throw new AssertionException(ErrorFormatter.Format(new CollectionEqualUnorderedAssertionError<T, T>(expectedSnapshot, actualSnapshot, difference.MissingExpectedIndex, difference.UnexpectedActualIndex, message, actualExpression, expectedExpression)));
    }

    private static void EqualUnorderedCollections<TExpected, TActual>(IEnumerable<TExpected> expected, IEnumerable<TActual> actual, System.Collections.IEqualityComparer? comparer, string? message, string? actualExpression, string? expectedExpression)
    {
        using var actualSnapshot = CollectionSnapshot.Create<TActual>(actual);
        using var expectedSnapshot = CollectionSnapshot.Create<TExpected>(expected);
        actualSnapshot.EnsureComplete();
        expectedSnapshot.EnsureComplete();

        if (GetEqualUnorderedValueDifference(expectedSnapshot.Items, actualSnapshot.Items, comparer) is not { } difference)
            return;

        throw new AssertionException(ErrorFormatter.Format(new CollectionEqualUnorderedAssertionError<TExpected, TActual>(expectedSnapshot, actualSnapshot, difference.MissingExpectedIndex, difference.UnexpectedActualIndex, message, actualExpression, expectedExpression)));
    }

    public static async Task EqualUnordered<T>(IAsyncEnumerable<T> expected, [NotNull] IAsyncEnumerable<T>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            await using var expectedSnapshot = CollectionSnapshot.Create<T>(expected);
            await expectedSnapshot.EnsureCompleteAsync().ConfigureAwait(false);
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<IReadOnlyList<T>>(nameof(EqualUnordered), "Expected expression", "Expected", expectedSnapshot.Items, actualExpression, expectedExpression, message)));
        }

        await EqualUnorderedAsyncCollections<T>(expected, actual, EqualityComparer<T>.Default, message, actualExpression, expectedExpression).ConfigureAwait(false);
    }

    public static async Task EqualUnordered<T>(IAsyncEnumerable<T> expected, [NotNull] IAsyncEnumerable<T>? actual, IEqualityComparer<T>? comparer, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            await using var expectedSnapshot = CollectionSnapshot.Create<T>(expected);
            await expectedSnapshot.EnsureCompleteAsync().ConfigureAwait(false);
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<IReadOnlyList<T>>(nameof(EqualUnordered), "Expected expression", "Expected", expectedSnapshot.Items, actualExpression, expectedExpression, message)));
        }

        if (comparer is null)
        {
            await EqualUnorderedAsyncCollections<T>(expected, actual, EqualityComparer<T>.Default, message, actualExpression, expectedExpression).ConfigureAwait(false);
            return;
        }

        await EqualUnorderedAsyncCollections(expected, actual, comparer, message, actualExpression, expectedExpression).ConfigureAwait(false);
    }

    [OverloadResolutionPriority(-1)]
    public static async Task EqualUnordered<TExpected, TActual>(IAsyncEnumerable<TExpected> expected, [NotNull] IAsyncEnumerable<TActual>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            await using var expectedSnapshot = CollectionSnapshot.Create<TExpected>(expected);
            await expectedSnapshot.EnsureCompleteAsync().ConfigureAwait(false);
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<IReadOnlyList<TExpected>>(nameof(EqualUnordered), "Expected expression", "Expected", expectedSnapshot.Items, actualExpression, expectedExpression, message)));
        }

        await EqualUnorderedAsyncCollections(expected, actual, comparer: (System.Collections.IEqualityComparer?)null, message, actualExpression, expectedExpression).ConfigureAwait(false);
    }

    private static async Task EqualUnorderedAsyncCollections<T>(IAsyncEnumerable<T> expected, IAsyncEnumerable<T> actual, IEqualityComparer<T>? comparer, string? message, string? actualExpression, string? expectedExpression)
    {
        await using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        await using var expectedSnapshot = CollectionSnapshot.Create<T>(expected);
        await actualSnapshot.EnsureCompleteAsync().ConfigureAwait(false);
        await expectedSnapshot.EnsureCompleteAsync().ConfigureAwait(false);
        comparer ??= EqualityComparer<T>.Default;

        if (GetEqualUnorderedDifference(expectedSnapshot.Items, actualSnapshot.Items, comparer) is not { } difference)
            return;

        throw new AssertionException(await ErrorFormatter.FormatAsync(new AsyncCollectionEqualUnorderedAssertionError<T, T>(expectedSnapshot, actualSnapshot, difference.MissingExpectedIndex, difference.UnexpectedActualIndex, message, actualExpression, expectedExpression)).ConfigureAwait(false));
    }

    private static async Task EqualUnorderedAsyncCollections<TExpected, TActual>(IAsyncEnumerable<TExpected> expected, IAsyncEnumerable<TActual> actual, System.Collections.IEqualityComparer? comparer, string? message, string? actualExpression, string? expectedExpression)
    {
        await using var actualSnapshot = CollectionSnapshot.Create<TActual>(actual);
        await using var expectedSnapshot = CollectionSnapshot.Create<TExpected>(expected);
        await actualSnapshot.EnsureCompleteAsync().ConfigureAwait(false);
        await expectedSnapshot.EnsureCompleteAsync().ConfigureAwait(false);

        if (GetEqualUnorderedValueDifference(expectedSnapshot.Items, actualSnapshot.Items, comparer) is not { } difference)
            return;

        throw new AssertionException(await ErrorFormatter.FormatAsync(new AsyncCollectionEqualUnorderedAssertionError<TExpected, TActual>(expectedSnapshot, actualSnapshot, difference.MissingExpectedIndex, difference.UnexpectedActualIndex, message, actualExpression, expectedExpression)).ConfigureAwait(false));
    }

    public static void EqualUnordered(System.Collections.IEnumerable expected, [NotNull] System.Collections.IEnumerable? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<System.Collections.IEnumerable>(nameof(EqualUnordered), "Expected expression", "Expected", expected, actualExpression, expectedExpression, message)));
        }

        EqualUnordered(expected, actual, comparer: null, message, actualExpression, expectedExpression);
    }

    public static void EqualUnordered(System.Collections.IEnumerable expected, [NotNull] System.Collections.IEnumerable? actual, System.Collections.IEqualityComparer? comparer, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<System.Collections.IEnumerable>(nameof(EqualUnordered), "Expected expression", "Expected", expected, actualExpression, expectedExpression, message)));
        }

        EqualUnorderedCollections(EnumerateObjects(expected), EnumerateObjects(actual), comparer, message, actualExpression, expectedExpression);
    }

    /// <summary>
    /// Locates a difference between two collections compared as multisets, or <see langword="null"/> when they hold
    /// the same items regardless of order.
    /// </summary>
    /// <remarks>
    /// <c>EqualUnordered</c> and <c>NotEqualUnordered</c> both decide through <see cref="AreEqualUnordered{T}"/> so they
    /// stay exact complements. Only the failure path pays for the quadratic scan, which locates the indexes shown in the message.
    /// </remarks>
    private static (int? MissingExpectedIndex, int? UnexpectedActualIndex)? GetEqualUnorderedDifference<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual, IEqualityComparer<T> comparer)
    {
        if (AreEqualUnordered(expected, actual, comparer))
            return null;

        return GetEqualUnorderedMismatch(expected, actual, comparer);
    }

    private static (int? MissingExpectedIndex, int? UnexpectedActualIndex)? GetEqualUnorderedValueDifference<TExpected, TActual>(IReadOnlyList<TExpected> expected, IReadOnlyList<TActual> actual, System.Collections.IEqualityComparer? comparer)
    {
        if (AreValuesEqualUnordered(expected, actual, comparer))
            return null;

        return GetEqualUnorderedMismatch(expected, actual, comparer);
    }

    private static bool AreEqualUnordered<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual, IEqualityComparer<T> comparer)
    {
        return object.ReferenceEquals(comparer, EqualityComparer<T>.Default)
            ? AreEqualUnordered<T, T, DefaultUnorderedItemComparer<T>>(expected, actual, default)
            : AreEqualUnordered<T, T, CustomUnorderedItemComparer<T>>(expected, actual, new(comparer));
    }

    private static bool AreValuesEqualUnordered<TExpected, TActual>(IReadOnlyList<TExpected> expected, IReadOnlyList<TActual> actual, System.Collections.IEqualityComparer? comparer)
    {
        return AreEqualUnordered<TExpected, TActual, ValueUnorderedItemComparer<TExpected, TActual>>(expected, actual, new(comparer));
    }

    /// <summary>
    /// Determines whether every expected item can be paired with a distinct actual item it is equal to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The comparer does not have to be an equivalence relation: a tolerance comparer, for instance, is not transitive, so
    /// two items equal to a third are not necessarily equal to each other, and its hash code may not even agree with its
    /// Equals. The answer is therefore a perfect matching in the bipartite graph whose edges are the pairs Equals accepts.
    /// </para>
    /// <para>
    /// Hash codes only propose pairs: items are paired with an equal item from the same hash bucket, and every pair is
    /// confirmed with Equals, so a complete pairing is a proof of equality whatever the comparer. For the usual comparers
    /// this pairs everything in O(n). Otherwise the pairing is completed with augmenting paths (Kuhn's algorithm), which
    /// only relies on Equals. An expected item from which no augmenting path starts stays unpaired in every maximum
    /// pairing, so the first such item ends the search.
    /// </para>
    /// </remarks>
    private static bool AreEqualUnordered<TExpected, TActual, TComparer>(IReadOnlyList<TExpected> expected, IReadOnlyList<TActual> actual, TComparer comparer)
        where TComparer : struct, IUnorderedItemComparer<TExpected, TActual>
    {
        var count = expected.Count;
        if (count != actual.Count)
            return false;

        if (count == 0)
            return true;

        // For an unpaired expected item, links holds the next unpaired expected item of the same hash bucket, or -1.
        // For a paired one, it holds -2 - actualIndex.
        var links = new int[count];
        var pairedCount = 0;
        if (comparer.UsesHashCodes)
        {
            var bucketHeads = new Dictionary<int, int>(count);
            for (var expectedIndex = count - 1; expectedIndex >= 0; expectedIndex--)
            {
                ref var head = ref CollectionsMarshal.GetValueRefOrAddDefault(bucketHeads, comparer.GetExpectedHashCode(expected[expectedIndex]), out var exists);
                links[expectedIndex] = exists ? head : -1;
                head = expectedIndex;
            }

            for (var actualIndex = 0; actualIndex < count; actualIndex++)
            {
                var actualItem = actual[actualIndex];
                ref var head = ref CollectionsMarshal.GetValueRefOrNullRef(bucketHeads, comparer.GetActualHashCode(actualItem));
                if (Unsafe.IsNullRef(ref head))
                    continue;

                var previous = -1;
                for (var expectedIndex = head; expectedIndex >= 0; expectedIndex = links[expectedIndex])
                {
                    if (!comparer.AreEqual(expected[expectedIndex], actualItem))
                    {
                        previous = expectedIndex;
                        continue;
                    }

                    if (previous < 0)
                    {
                        head = links[expectedIndex];
                    }
                    else
                    {
                        links[previous] = links[expectedIndex];
                    }

                    links[expectedIndex] = -2 - actualIndex;
                    pairedCount++;
                    break;
                }
            }

            if (pairedCount == count)
                return true;
        }
        else
        {
            links.AsSpan().Fill(-1);
        }

        // One allocation holds the state of the augmenting-path search:
        // - expectedIndexByActual: expectedIndex + 1 for a paired actual item, 0 for an unpaired one;
        // - nextUnvisited: a disjoint-set forest over the actual indexes plus a sentinel at count, whose root is the
        //   first actual item the current search has not visited yet, so a search never rescans visited items;
        // - pathExpectedIndexes and pathCursors: the expected items on the current path, and for each one the next
        //   actual index to examine; the index before the cursor is the actual item leading to the next step.
        var buffer = new int[(4 * count) + 2];
        var expectedIndexByActual = buffer.AsSpan(0, count);
        var nextUnvisited = buffer.AsSpan(count, count + 1);
        var pathExpectedIndexes = buffer.AsSpan((2 * count) + 1, count);
        var pathCursors = buffer.AsSpan((3 * count) + 1, count + 1);
        for (var expectedIndex = 0; expectedIndex < count; expectedIndex++)
        {
            if (links[expectedIndex] <= -2)
            {
                expectedIndexByActual[-2 - links[expectedIndex]] = expectedIndex + 1;
            }
        }

        for (var expectedIndex = 0; expectedIndex < count; expectedIndex++)
        {
            // Augmenting from an item pairs it without unpairing any other, so every item is examined at most once.
            if (links[expectedIndex] <= -2)
                continue;

            if (!TryAugmentUnorderedPairing(expected, actual, comparer, expectedIndex, expectedIndexByActual, nextUnvisited, pathExpectedIndexes, pathCursors))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Pairs <paramref name="rootExpectedIndex"/> by flipping an augmenting path, found with an iterative depth-first
    /// search so a long path cannot overflow the stack.
    /// </summary>
    private static bool TryAugmentUnorderedPairing<TExpected, TActual, TComparer>(IReadOnlyList<TExpected> expected, IReadOnlyList<TActual> actual, TComparer comparer, int rootExpectedIndex, Span<int> expectedIndexByActual, Span<int> nextUnvisited, Span<int> pathExpectedIndexes, Span<int> pathCursors)
        where TComparer : struct, IUnorderedItemComparer<TExpected, TActual>
    {
        var count = expectedIndexByActual.Length;
        for (var index = 0; index <= count; index++)
        {
            nextUnvisited[index] = index;
        }

        // A path visits distinct actual items and ends at an unpaired one, so it holds at most count expected items.
        var depth = 0;
        pathExpectedIndexes[0] = rootExpectedIndex;
        pathCursors[0] = 0;
        while (depth >= 0)
        {
            var expectedItem = expected[pathExpectedIndexes[depth]];
            var extended = false;
            for (var actualIndex = FindUnvisited(nextUnvisited, pathCursors[depth]); actualIndex < count; actualIndex = FindUnvisited(nextUnvisited, actualIndex + 1))
            {
                if (!comparer.AreEqual(expectedItem, actual[actualIndex]))
                    continue;

                nextUnvisited[actualIndex] = actualIndex + 1;
                pathCursors[depth] = actualIndex + 1;
                if (expectedIndexByActual[actualIndex] == 0)
                {
                    // Flip the path: each expected item on it takes the actual item that leads to the next step.
                    for (var step = depth; step >= 0; step--)
                    {
                        expectedIndexByActual[pathCursors[step] - 1] = pathExpectedIndexes[step] + 1;
                    }

                    return true;
                }

                depth++;
                pathExpectedIndexes[depth] = expectedIndexByActual[actualIndex] - 1;
                pathCursors[depth] = 0;
                extended = true;
                break;
            }

            if (!extended)
            {
                depth--;
            }
        }

        return false;

        static int FindUnvisited(Span<int> nextUnvisited, int index)
        {
            while (nextUnvisited[index] != index)
            {
                var grandparent = nextUnvisited[nextUnvisited[index]];
                nextUnvisited[index] = grandparent;
                index = grandparent;
            }

            return index;
        }
    }

    private interface IUnorderedItemComparer<in TExpected, in TActual>
    {
        /// <summary>Gets a value indicating whether hash codes are worth using to propose pairs of equal items.</summary>
        bool UsesHashCodes { get; }

        int GetExpectedHashCode(TExpected item);

        int GetActualHashCode(TActual item);

        bool AreEqual(TExpected expected, TActual actual);
    }

    private readonly struct DefaultUnorderedItemComparer<T> : IUnorderedItemComparer<T, T>
    {
        public bool UsesHashCodes => true;

        public int GetExpectedHashCode(T item) => item is null ? 0 : EqualityComparer<T>.Default.GetHashCode(item);

        public int GetActualHashCode(T item) => item is null ? 0 : EqualityComparer<T>.Default.GetHashCode(item);

        public bool AreEqual(T expected, T actual) => EqualityComparer<T>.Default.Equals(expected, actual);
    }

    private readonly struct CustomUnorderedItemComparer<T>(IEqualityComparer<T> comparer) : IUnorderedItemComparer<T, T>
    {
        public bool UsesHashCodes => true;

        public int GetExpectedHashCode(T item) => item is null ? 0 : comparer.GetHashCode(item);

        public int GetActualHashCode(T item) => item is null ? 0 : comparer.GetHashCode(item);

        public bool AreEqual(T expected, T actual) => comparer.Equals(expected, actual);
    }

    private readonly struct ValueUnorderedItemComparer<TExpected, TActual>(System.Collections.IEqualityComparer? comparer) : IUnorderedItemComparer<TExpected, TActual>
    {
        // Without a comparer, items are compared as values: 1 equals 1L and nested collections are compared by content,
        // which no hash code reflects. The items' own hash codes still pair up equal items of the same type, the common
        // case, and the augmenting paths find the other pairs. The hash code of a supplied comparer is not used, as a
        // comparer written only for Equals may not implement it.
        public bool UsesHashCodes => comparer is null;

        public int GetExpectedHashCode(TExpected item) => item is null ? 0 : item.GetHashCode();

        public int GetActualHashCode(TActual item) => item is null ? 0 : item.GetHashCode();

        public bool AreEqual(TExpected expected, TActual actual) => ValuesEqual(expected, actual, comparer);
    }

    private static (int? MissingExpectedIndex, int? UnexpectedActualIndex) GetEqualUnorderedMismatch<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual, IEqualityComparer<T> comparer)
    {
        var matchedActualIndexes = new bool[actual.Count];
        int? missingExpectedIndex = null;

        for (var expectedIndex = 0; expectedIndex < expected.Count; expectedIndex++)
        {
            var found = false;
            for (var actualIndex = 0; actualIndex < actual.Count; actualIndex++)
            {
                if (matchedActualIndexes[actualIndex])
                    continue;

                if (!comparer.Equals(expected[expectedIndex], actual[actualIndex]))
                    continue;

                matchedActualIndexes[actualIndex] = true;
                found = true;
                break;
            }

            if (!found && missingExpectedIndex is null)
            {
                missingExpectedIndex = expectedIndex;
            }
        }

        int? unexpectedActualIndex = null;
        for (var actualIndex = 0; actualIndex < matchedActualIndexes.Length; actualIndex++)
        {
            if (!matchedActualIndexes[actualIndex])
            {
                unexpectedActualIndex = actualIndex;
                break;
            }
        }

        return (missingExpectedIndex, unexpectedActualIndex);
    }

    private static (int? MissingExpectedIndex, int? UnexpectedActualIndex) GetEqualUnorderedMismatch<TExpected, TActual>(IReadOnlyList<TExpected> expected, IReadOnlyList<TActual> actual, System.Collections.IEqualityComparer? comparer)
    {
        var matchedActualIndexes = new bool[actual.Count];
        int? missingExpectedIndex = null;

        for (var expectedIndex = 0; expectedIndex < expected.Count; expectedIndex++)
        {
            var found = false;
            for (var actualIndex = 0; actualIndex < actual.Count; actualIndex++)
            {
                if (matchedActualIndexes[actualIndex])
                    continue;

                if (!ValuesEqual(expected[expectedIndex], actual[actualIndex], comparer))
                    continue;

                matchedActualIndexes[actualIndex] = true;
                found = true;
                break;
            }

            if (!found && missingExpectedIndex is null)
            {
                missingExpectedIndex = expectedIndex;
            }
        }

        int? unexpectedActualIndex = null;
        for (var actualIndex = 0; actualIndex < matchedActualIndexes.Length; actualIndex++)
        {
            if (!matchedActualIndexes[actualIndex])
            {
                unexpectedActualIndex = actualIndex;
                break;
            }
        }

        return (missingExpectedIndex, unexpectedActualIndex);
    }
}
