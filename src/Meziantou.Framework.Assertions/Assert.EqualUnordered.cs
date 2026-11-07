using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    /// <summary>
    /// Bounds the comparisons spent completing the pairing of collections already known to differ. That work only chooses
    /// the indexes shown in the failure message, so it must not make a failing assertion look like a hung test.
    /// </summary>
    private const long UnorderedMismatchComparisonBudget = 1_000_000;

    /// <summary>Asserts that two sequences contain the same items, regardless of their order.</summary>
    /// <remarks>
    /// Items are compared like <c>Assert.Equal</c> compares two values, so nested collections are compared by content and
    /// numbers of different types by value. Two <see langword="null"/> sequences are equal.
    /// </remarks>
    public static void EqualUnordered<T>(IEnumerable<T>? expected, [NotNullIfNotNull(nameof(expected))] IEnumerable<T>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        EqualUnordered(expected, actual, comparer: null, message, actualExpression, expectedExpression);
    }

    /// <summary>Asserts that two sequences contain the same items, regardless of their order.</summary>
    /// <param name="comparer">The comparer used to compare items. When <see langword="null"/>, items are compared like <c>Assert.Equal</c> compares two values.</param>
    public static void EqualUnordered<T>(IEnumerable<T>? expected, [NotNullIfNotNull(nameof(expected))] IEnumerable<T>? actual, IEqualityComparer<T>? comparer, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (expected is null || actual is null)
        {
            EqualUnorderedNull(expected, actual, message, actualExpression, expectedExpression);
            return;
        }

        using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        using var expectedSnapshot = CollectionSnapshot.Create<T>(expected);
        actualSnapshot.EnsureComplete();
        expectedSnapshot.EnsureComplete();

        if (GetEqualUnorderedDifference(expectedSnapshot.Items, actualSnapshot.Items, comparer) is not { } difference)
            return;

        throw new AssertionException(ErrorFormatter.Format(new CollectionEqualUnorderedAssertionError<T, T>(expectedSnapshot, actualSnapshot, difference.MissingExpectedIndex, difference.UnexpectedActualIndex, message, actualExpression, expectedExpression)));
    }

    [OverloadResolutionPriority(-1)]
    public static void EqualUnordered<TExpected, TActual>(IEnumerable<TExpected>? expected, [NotNullIfNotNull(nameof(expected))] IEnumerable<TActual>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (expected is null || actual is null)
        {
            EqualUnorderedNull(expected, actual, message, actualExpression, expectedExpression);
            return;
        }

        EqualUnorderedCollections(expected, actual, comparer: null, message, actualExpression, expectedExpression);
    }

    /// <summary>Succeeds when both collections are <see langword="null"/>, and fails when only one of them is.</summary>
    private static void EqualUnorderedNull<TExpected, TActual>(TExpected? expected, TActual? actual, string? message, string? actualExpression, string? expectedExpression)
        where TExpected : class, System.Collections.IEnumerable
        where TActual : class, System.Collections.IEnumerable
    {
        if (expected is null && actual is null)
            return;

        if (actual is null)
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<TExpected?>(nameof(EqualUnordered), "Expected expression", "Expected", expected, actualExpression, expectedExpression, message)));

        throw new AssertionException(ErrorFormatter.Format(new NullExpectedAssertionError<TActual>(nameof(EqualUnordered), actual, actualExpression, expectedExpression, message)));
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

    /// <inheritdoc cref="EqualUnordered{T}(IEnumerable{T}, IEnumerable{T}, string, string, string)"/>
    public static async Task EqualUnordered<T>(IAsyncEnumerable<T>? expected, [NotNullIfNotNull(nameof(expected))] IAsyncEnumerable<T>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        await EqualUnordered(expected, actual, comparer: null, message, actualExpression, expectedExpression).ConfigureAwait(false);
    }

    /// <inheritdoc cref="EqualUnordered{T}(IEnumerable{T}, IEnumerable{T}, IEqualityComparer{T}, string, string, string)"/>
    public static async Task EqualUnordered<T>(IAsyncEnumerable<T>? expected, [NotNullIfNotNull(nameof(expected))] IAsyncEnumerable<T>? actual, IEqualityComparer<T>? comparer, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (expected is null || actual is null)
        {
            await EqualUnorderedNullAsync(expected, actual, message, actualExpression, expectedExpression).ConfigureAwait(false);
            return;
        }

        await using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        await using var expectedSnapshot = CollectionSnapshot.Create<T>(expected);
        await actualSnapshot.EnsureCompleteAsync().ConfigureAwait(false);
        await expectedSnapshot.EnsureCompleteAsync().ConfigureAwait(false);

        if (GetEqualUnorderedDifference(expectedSnapshot.Items, actualSnapshot.Items, comparer) is not { } difference)
            return;

        throw new AssertionException(await ErrorFormatter.FormatAsync(new AsyncCollectionEqualUnorderedAssertionError<T, T>(expectedSnapshot, actualSnapshot, difference.MissingExpectedIndex, difference.UnexpectedActualIndex, message, actualExpression, expectedExpression)).ConfigureAwait(false));
    }

    [OverloadResolutionPriority(-1)]
    public static async Task EqualUnordered<TExpected, TActual>(IAsyncEnumerable<TExpected>? expected, [NotNullIfNotNull(nameof(expected))] IAsyncEnumerable<TActual>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (expected is null || actual is null)
        {
            await EqualUnorderedNullAsync(expected, actual, message, actualExpression, expectedExpression).ConfigureAwait(false);
            return;
        }

        await using var actualSnapshot = CollectionSnapshot.Create<TActual>(actual);
        await using var expectedSnapshot = CollectionSnapshot.Create<TExpected>(expected);
        await actualSnapshot.EnsureCompleteAsync().ConfigureAwait(false);
        await expectedSnapshot.EnsureCompleteAsync().ConfigureAwait(false);

        if (GetEqualUnorderedValueDifference(expectedSnapshot.Items, actualSnapshot.Items, comparer: null) is not { } difference)
            return;

        throw new AssertionException(await ErrorFormatter.FormatAsync(new AsyncCollectionEqualUnorderedAssertionError<TExpected, TActual>(expectedSnapshot, actualSnapshot, difference.MissingExpectedIndex, difference.UnexpectedActualIndex, message, actualExpression, expectedExpression)).ConfigureAwait(false));
    }

    private static async Task EqualUnorderedNullAsync<TExpected, TActual>(IAsyncEnumerable<TExpected>? expected, [NotNullIfNotNull(nameof(expected))] IAsyncEnumerable<TActual>? actual, string? message, string? actualExpression, string? expectedExpression)
    {
        if (expected is null && actual is null)
            return;

        if (actual is null)
        {
            await using var expectedSnapshot = CollectionSnapshot.Create<TExpected>(expected!);
            await expectedSnapshot.EnsureCompleteAsync().ConfigureAwait(false);
            throw new AssertionException(ErrorFormatter.Format(new NullActualAssertionError<IReadOnlyList<TExpected>>(nameof(EqualUnordered), "Expected expression", "Expected", expectedSnapshot.Items, actualExpression, expectedExpression, message)));
        }

        await using var actualSnapshot = CollectionSnapshot.Create<TActual>(actual);
        await actualSnapshot.EnsureCompleteAsync().ConfigureAwait(false);
        throw new AssertionException(ErrorFormatter.Format(new NullExpectedAssertionError<IReadOnlyList<TActual>>(nameof(EqualUnordered), actualSnapshot.Items, actualExpression, expectedExpression, message)));
    }

    public static void EqualUnordered(System.Collections.IEnumerable? expected, [NotNullIfNotNull(nameof(expected))] System.Collections.IEnumerable? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        EqualUnordered(expected, actual, comparer: null, message, actualExpression, expectedExpression);
    }

    public static void EqualUnordered(System.Collections.IEnumerable? expected, [NotNullIfNotNull(nameof(expected))] System.Collections.IEnumerable? actual, System.Collections.IEqualityComparer? comparer, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (expected is null || actual is null)
        {
            EqualUnorderedNull(expected, actual, message, actualExpression, expectedExpression);
            return;
        }

        EqualUnorderedCollections(EnumerateObjects(expected), EnumerateObjects(actual), comparer, message, actualExpression, expectedExpression);
    }

    /// <summary>
    /// Locates a difference between two collections compared as multisets, or <see langword="null"/> when they hold
    /// the same items regardless of order.
    /// </summary>
    /// <remarks>
    /// <c>EqualUnordered</c> and <c>NotEqualUnordered</c> both decide through the same pairing so they stay exact
    /// complements. The indexes shown in the message are the first items the pairing leaves unpaired.
    /// </remarks>
    private static (int? MissingExpectedIndex, int? UnexpectedActualIndex)? GetEqualUnorderedDifference<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual, IEqualityComparer<T>? comparer)
    {
        return GetUnorderedItemComparerKind(comparer) switch
        {
            UnorderedItemComparerKind.Default => GetEqualUnorderedDifference<T, T, DefaultUnorderedItemComparer<T>>(expected, actual, default),
            UnorderedItemComparerKind.Value => GetEqualUnorderedDifference<T, T, ValueUnorderedItemComparer<T, T>>(expected, actual, new(comparer: null)),
            _ => GetEqualUnorderedDifference<T, T, CustomUnorderedItemComparer<T>>(expected, actual, new(comparer!)),
        };
    }

    private static (int? MissingExpectedIndex, int? UnexpectedActualIndex)? GetEqualUnorderedValueDifference<TExpected, TActual>(IReadOnlyList<TExpected> expected, IReadOnlyList<TActual> actual, System.Collections.IEqualityComparer? comparer)
    {
        return GetEqualUnorderedDifference<TExpected, TActual, ValueUnorderedItemComparer<TExpected, TActual>>(expected, actual, new(comparer));
    }

    private static bool AreEqualUnordered<T>(IReadOnlyList<T> expected, IReadOnlyList<T> actual, IEqualityComparer<T>? comparer)
    {
        return GetUnorderedItemComparerKind(comparer) switch
        {
            UnorderedItemComparerKind.Default => AreEqualUnordered<T, T, DefaultUnorderedItemComparer<T>>(expected, actual, default),
            UnorderedItemComparerKind.Value => AreEqualUnordered<T, T, ValueUnorderedItemComparer<T, T>>(expected, actual, new(comparer: null)),
            _ => AreEqualUnordered<T, T, CustomUnorderedItemComparer<T>>(expected, actual, new(comparer!)),
        };
    }

    private static bool AreValuesEqualUnordered<TExpected, TActual>(IReadOnlyList<TExpected> expected, IReadOnlyList<TActual> actual, System.Collections.IEqualityComparer? comparer)
    {
        return AreEqualUnordered<TExpected, TActual, ValueUnorderedItemComparer<TExpected, TActual>>(expected, actual, new(comparer));
    }

    private static UnorderedItemComparerKind GetUnorderedItemComparerKind<T>(IEqualityComparer<T>? comparer)
    {
        if (comparer is null)
            return DefaultEqualityMatchesValueEquality<T>.Value ? UnorderedItemComparerKind.Default : UnorderedItemComparerKind.Value;

        return object.ReferenceEquals(comparer, EqualityComparer<T>.Default) ? UnorderedItemComparerKind.Default : UnorderedItemComparerKind.Custom;
    }

    private static bool AreEqualUnordered<TExpected, TActual, TComparer>(IReadOnlyList<TExpected> expected, IReadOnlyList<TActual> actual, TComparer comparer)
        where TComparer : struct, IUnorderedItemComparer<TExpected, TActual>
    {
        if (expected.Count != actual.Count)
            return false;

        if (expected.Count == 0)
            return true;

        var pairing = new UnorderedPairing<TExpected, TActual, TComparer>(expected, actual, comparer);
        pairing.PairByHashCodes();
        return pairing.PairRemaining(stopAtFirstUnpairedItem: true, comparisonBudget: long.MaxValue);
    }

    private static (int? MissingExpectedIndex, int? UnexpectedActualIndex)? GetEqualUnorderedDifference<TExpected, TActual, TComparer>(IReadOnlyList<TExpected> expected, IReadOnlyList<TActual> actual, TComparer comparer)
        where TComparer : struct, IUnorderedItemComparer<TExpected, TActual>
    {
        if (expected.Count == actual.Count && expected.Count == 0)
            return null;

        var pairing = new UnorderedPairing<TExpected, TActual, TComparer>(expected, actual, comparer);
        pairing.PairByHashCodes();
        if (expected.Count == actual.Count && pairing.PairRemaining(stopAtFirstUnpairedItem: true, comparisonBudget: long.MaxValue))
            return null;

        // The collections differ. Pairing the remaining items only avoids reporting an item that another item pairs with,
        // so it is bounded: past the budget, the message shows the first items that are still unpaired.
        pairing.PairRemaining(stopAtFirstUnpairedItem: false, UnorderedMismatchComparisonBudget);
        return (pairing.GetFirstUnpairedExpectedIndex(), pairing.GetFirstUnpairedActualIndex());
    }

    private enum UnorderedItemComparerKind
    {
        Default,
        Value,
        Custom,
    }

    /// <summary>
    /// Indicates whether <see cref="EqualityComparer{T}.Default"/> gives the same answer as the value comparison of
    /// <c>Assert.Equal</c> for every pair of <typeparamref name="T"/> values, so the cheaper comparer can be used.
    /// </summary>
    /// <remarks>
    /// The value comparison only differs when a value is a collection, compared by content, or when two values have
    /// different runtime types, such as boxed numbers or types with implicit conversions. A value type other than
    /// <see cref="Nullable{T}"/> and a sealed class have a single runtime type, so only collections are left. A string is
    /// a collection of characters, but both comparisons compare it ordinally.
    /// </remarks>
    private static class DefaultEqualityMatchesValueEquality<T>
    {
        public static readonly bool Value = typeof(T) == typeof(string)
            || ((typeof(T).IsValueType || typeof(T).IsSealed) && !typeof(System.Collections.IEnumerable).IsAssignableFrom(Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T)));
    }

    /// <summary>
    /// Pairs every expected item with a distinct actual item it is equal to.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The comparer does not have to be an equivalence relation: a tolerance comparer, for instance, is not transitive, so
    /// two items equal to a third are not necessarily equal to each other, and its hash code may not even agree with its
    /// Equals. Equality is therefore a perfect matching in the bipartite graph whose edges are the pairs Equals accepts.
    /// </para>
    /// <para>
    /// Hash codes only propose pairs: items are paired with an equal item from the same hash bucket, and every pair is
    /// confirmed with Equals, so a complete pairing is a proof of equality whatever the comparer. For the usual comparers
    /// this pairs everything in O(n). Otherwise the pairing is completed with augmenting paths (Kuhn's algorithm), which
    /// only relies on Equals. An expected item from which no augmenting path starts stays unpaired in every maximum
    /// pairing, so it never needs to be examined again.
    /// </para>
    /// </remarks>
    private sealed class UnorderedPairing<TExpected, TActual, TComparer>
        where TComparer : struct, IUnorderedItemComparer<TExpected, TActual>
    {
        private readonly IReadOnlyList<TExpected> _expected;
        private readonly IReadOnlyList<TActual> _actual;
        private readonly TComparer _comparer;

        // expectedIndex + 1 for a paired item, 0 for an unpaired one
        private readonly int[] _expectedIndexByActual;
        private readonly bool[] _isExpectedPaired;

        // State of the augmenting-path search, allocated on first use:
        // - nextUnvisited: a disjoint-set forest over the actual indexes plus a sentinel, whose root is the first actual
        //   item the current search has not visited yet, so a search never rescans visited items. An entry whose stamp is
        //   not the current search's is unvisited, so starting a search does not reset the whole forest.
        // - pathExpectedIndexes and pathCursors: the expected items on the current path, and for each one the next
        //   actual index to examine; the index before the cursor is the actual item leading to the next step.
        private int[]? _nextUnvisited;
        private int[]? _visitStamps;
        private int[]? _pathExpectedIndexes;
        private int[]? _pathCursors;
        private int _searchStamp;
        private int _nextRootExpectedIndex;

        public UnorderedPairing(IReadOnlyList<TExpected> expected, IReadOnlyList<TActual> actual, TComparer comparer)
        {
            _expected = expected;
            _actual = actual;
            _comparer = comparer;
            _expectedIndexByActual = new int[actual.Count];
            _isExpectedPaired = new bool[expected.Count];
        }

        public void PairByHashCodes()
        {
            var expectedCount = _expected.Count;
            var actualCount = _actual.Count;
            if (expectedCount == 0 || actualCount == 0)
                return;

            // For an unpaired expected item, links holds the next unpaired expected item of the same hash bucket, or -1
            var links = new int[expectedCount];
            var bucketHeads = new Dictionary<int, int>(expectedCount);
            var isHashing = false;
            try
            {
                for (var expectedIndex = expectedCount - 1; expectedIndex >= 0; expectedIndex--)
                {
                    isHashing = true;
                    var hashCode = _comparer.GetExpectedHashCode(_expected[expectedIndex]);
                    isHashing = false;

                    ref var head = ref CollectionsMarshal.GetValueRefOrAddDefault(bucketHeads, hashCode, out var exists);
                    links[expectedIndex] = exists ? head : -1;
                    head = expectedIndex;
                }

                for (var actualIndex = 0; actualIndex < actualCount; actualIndex++)
                {
                    var actualItem = _actual[actualIndex];
                    isHashing = true;
                    var hashCode = _comparer.GetActualHashCode(actualItem);
                    isHashing = false;

                    ref var head = ref CollectionsMarshal.GetValueRefOrNullRef(bucketHeads, hashCode);
                    if (Unsafe.IsNullRef(ref head))
                        continue;

                    var previous = -1;
                    for (var expectedIndex = head; expectedIndex >= 0; expectedIndex = links[expectedIndex])
                    {
                        if (!_comparer.AreEqual(_expected[expectedIndex], actualItem))
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

                        _isExpectedPaired[expectedIndex] = true;
                        _expectedIndexByActual[actualIndex] = expectedIndex + 1;
                        break;
                    }
                }
            }
            catch (Exception exception) when (isHashing && !IsXunitSkipException(exception))
            {
                // A comparer written only for Equals may not implement GetHashCode, and a value may not support being
                // enumerated to hash its content. Hashing then stops: the pairs found so far were confirmed with Equals,
                // and the augmenting paths find the others. An exception thrown by Equals is not caught.
            }
        }

        /// <summary>Pairs the expected items that are still unpaired, in order.</summary>
        /// <param name="stopAtFirstUnpairedItem">Stops at the first expected item that cannot be paired.</param>
        /// <param name="comparisonBudget">The maximum number of comparisons, after which the remaining items stay unpaired.</param>
        /// <returns><see langword="true"/> when every expected item is paired.</returns>
        public bool PairRemaining(bool stopAtFirstUnpairedItem, long comparisonBudget)
        {
            var allPaired = true;
            for (; _nextRootExpectedIndex < _expected.Count; _nextRootExpectedIndex++)
            {
                // Augmenting from an item pairs it without unpairing any other, so every item is examined at most once.
                if (_isExpectedPaired[_nextRootExpectedIndex])
                    continue;

                if (comparisonBudget <= 0 || !TryAugment(_nextRootExpectedIndex, ref comparisonBudget))
                {
                    allPaired = false;
                    if (stopAtFirstUnpairedItem || comparisonBudget <= 0)
                    {
                        _nextRootExpectedIndex++;
                        return false;
                    }
                }
            }

            return allPaired;
        }

        public int? GetFirstUnpairedExpectedIndex()
        {
            var index = Array.IndexOf(_isExpectedPaired, value: false);
            return index < 0 ? null : index;
        }

        public int? GetFirstUnpairedActualIndex()
        {
            var index = Array.IndexOf(_expectedIndexByActual, value: 0);
            return index < 0 ? null : index;
        }

        /// <summary>
        /// Pairs <paramref name="rootExpectedIndex"/> by flipping an augmenting path, found with an iterative depth-first
        /// search so a long path cannot overflow the stack.
        /// </summary>
        private bool TryAugment(int rootExpectedIndex, ref long comparisonBudget)
        {
            var actualCount = _actual.Count;
            if (actualCount == 0)
                return false;

            // A path visits distinct paired actual items before ending at an unpaired one
            var maxPathLength = Math.Min(_expected.Count, actualCount) + 1;
            var nextUnvisited = _nextUnvisited ??= new int[actualCount + 1];
            var visitStamps = _visitStamps ??= new int[actualCount + 1];
            var pathExpectedIndexes = _pathExpectedIndexes ??= new int[maxPathLength];
            var pathCursors = _pathCursors ??= new int[maxPathLength];
            var stamp = ++_searchStamp;

            var depth = 0;
            pathExpectedIndexes[0] = rootExpectedIndex;
            pathCursors[0] = 0;
            while (depth >= 0)
            {
                var expectedItem = _expected[pathExpectedIndexes[depth]];
                var extended = false;
                for (var actualIndex = FindUnvisited(nextUnvisited, visitStamps, stamp, pathCursors[depth]); actualIndex < actualCount; actualIndex = FindUnvisited(nextUnvisited, visitStamps, stamp, actualIndex + 1))
                {
                    if (comparisonBudget-- <= 0)
                        return false;

                    if (!_comparer.AreEqual(expectedItem, _actual[actualIndex]))
                        continue;

                    nextUnvisited[actualIndex] = actualIndex + 1;
                    pathCursors[depth] = actualIndex + 1;
                    if (_expectedIndexByActual[actualIndex] == 0)
                    {
                        // Flip the path: each expected item on it takes the actual item that leads to the next step.
                        for (var step = depth; step >= 0; step--)
                        {
                            _expectedIndexByActual[pathCursors[step] - 1] = pathExpectedIndexes[step] + 1;
                        }

                        _isExpectedPaired[rootExpectedIndex] = true;
                        return true;
                    }

                    depth++;
                    pathExpectedIndexes[depth] = _expectedIndexByActual[actualIndex] - 1;
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

            static int FindUnvisited(int[] nextUnvisited, int[] visitStamps, int stamp, int index)
            {
                while (true)
                {
                    if (visitStamps[index] != stamp)
                    {
                        // Not touched by the current search, so the item is unvisited
                        visitStamps[index] = stamp;
                        nextUnvisited[index] = index;
                        return index;
                    }

                    var parent = nextUnvisited[index];
                    if (parent == index)
                        return index;

                    var grandparent = visitStamps[parent] == stamp ? nextUnvisited[parent] : parent;
                    nextUnvisited[index] = grandparent;
                    index = grandparent;
                }
            }
        }
    }

    private interface IUnorderedItemComparer<in TExpected, in TActual>
    {
        int GetExpectedHashCode(TExpected item);

        int GetActualHashCode(TActual item);

        bool AreEqual(TExpected expected, TActual actual);
    }

    private readonly struct DefaultUnorderedItemComparer<T> : IUnorderedItemComparer<T, T>
    {
        public int GetExpectedHashCode(T item) => item is null ? 0 : EqualityComparer<T>.Default.GetHashCode(item);

        public int GetActualHashCode(T item) => item is null ? 0 : EqualityComparer<T>.Default.GetHashCode(item);

        public bool AreEqual(T expected, T actual) => EqualityComparer<T>.Default.Equals(expected, actual);
    }

    private readonly struct CustomUnorderedItemComparer<T>(IEqualityComparer<T> comparer) : IUnorderedItemComparer<T, T>
    {
        public int GetExpectedHashCode(T item) => item is null ? 0 : comparer.GetHashCode(item);

        public int GetActualHashCode(T item) => item is null ? 0 : comparer.GetHashCode(item);

        public bool AreEqual(T expected, T actual) => comparer.Equals(expected, actual);
    }

    private readonly struct ValueUnorderedItemComparer<TExpected, TActual>(System.Collections.IEqualityComparer? comparer) : IUnorderedItemComparer<TExpected, TActual>
    {
        public int GetExpectedHashCode(TExpected item) => GetItemHashCode(item);

        public int GetActualHashCode(TActual item) => GetItemHashCode(item);

        public bool AreEqual(TExpected expected, TActual actual) => ValuesEqual(expected, actual, comparer);

        private int GetItemHashCode<T>(T item)
        {
            if (item is null)
                return 0;

            if (comparer is not null)
                return comparer.GetHashCode(item);

            var remainingItems = MaxHashedNestedItems;
            return GetValueHashCode(item, depth: 0, ref remainingItems);
        }
    }

    private const int MaxHashedNestedItems = 1024;
    private const int MaxHashedNestingDepth = 8;

    /// <summary>
    /// Computes a hash code that agrees with the value comparison of <c>Assert.Equal</c> in the common cases, so equal
    /// items land in the same bucket: numbers of different types that are equal as values, and collections with equal
    /// items in the same order.
    /// </summary>
    /// <remarks>
    /// The hash code only proposes pairs, so a disagreement only costs time. Values equal through an implicit conversion
    /// or an unusual Equals, for instance, are still paired by the augmenting paths. Only the first items of nested
    /// collections are hashed, which bounds the work on large, deeply nested or self-referencing collections.
    /// </remarks>
    private static int GetValueHashCode<T>(T value, int depth, ref int remainingItems)
    {
        switch (value)
        {
            case null:
                return 0;

            case string text:
                return StringComparer.Ordinal.GetHashCode(text);

            // Assert.Equal compares numbers of different types as double when one of them is a floating-point number, and
            // as decimal otherwise. Equal decimals are also equal as double, so hashing the double value agrees with both.
            case int number:
                return ((double)number).GetHashCode();

            case long number:
                return ((double)number).GetHashCode();

            case double number:
                return number.GetHashCode();

            case nint pointer:
                return ((double)pointer).GetHashCode();

            case nuint pointer:
                return ((double)pointer).GetHashCode();

            case System.Collections.IEnumerable enumerable:
                var hashCode = new HashCode();
                if (depth < MaxHashedNestingDepth)
                {
                    foreach (var item in enumerable)
                    {
                        if (remainingItems-- <= 0)
                            break;

                        hashCode.Add(GetValueHashCode(item, depth + 1, ref remainingItems));
                    }
                }

                return hashCode.ToHashCode();

            // Every numeric type implements IConvertible, which rules out most other values without reading their type
            case IConvertible convertible when value is not Enum && Type.GetTypeCode(value.GetType()) is >= TypeCode.SByte and <= TypeCode.Decimal:
                return convertible.ToDouble(CultureInfo.InvariantCulture).GetHashCode();
        }

        return EqualityComparer<T>.Default.GetHashCode(value);
    }
}
