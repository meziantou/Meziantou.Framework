using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    public static void Equal(Half expected, Half actual, Half tolerance, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (expected.Equals(actual) || MathF.Abs((float)expected - (float)actual) <= (float)tolerance)
            return;

        throw new AssertionException(ErrorFormatter.Format(new EqualWithToleranceAssertionError<Half>(expected, actual, tolerance, message, actualExpression, expectedExpression)));
    }

    public static void Equal(float expected, float actual, float tolerance, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (expected.Equals(actual) || MathF.Abs(expected - actual) <= tolerance)
            return;

        throw new AssertionException(ErrorFormatter.Format(new EqualWithToleranceAssertionError<float>(expected, actual, tolerance, message, actualExpression, expectedExpression)));
    }

    public static void Equal(double expected, double actual, double tolerance, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (expected.Equals(actual) || Math.Abs(expected - actual) <= tolerance)
            return;

        throw new AssertionException(ErrorFormatter.Format(new EqualWithToleranceAssertionError<double>(expected, actual, tolerance, message, actualExpression, expectedExpression)));
    }

    public static void Equal(decimal expected, decimal actual, decimal tolerance, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (IsWithinTolerance(expected, actual, tolerance))
            return;

        throw new AssertionException(ErrorFormatter.Format(new EqualWithToleranceAssertionError<decimal>(expected, actual, tolerance, message, actualExpression, expectedExpression)));
    }

    private static bool IsWithinTolerance(decimal expected, decimal actual, decimal tolerance)
    {
        if (expected == actual)
            return true;

        // Values of opposite signs are |expected| + |actual| apart, which can exceed the range of decimal, so the
        // subtraction would throw. Subtracting each magnitude from the tolerance instead cannot overflow.
        if (decimal.IsNegative(expected) != decimal.IsNegative(actual))
        {
            var expectedMagnitude = decimal.Abs(expected);
            return expectedMagnitude <= tolerance && decimal.Abs(actual) <= tolerance - expectedMagnitude;
        }

        return decimal.Abs(expected - actual) <= tolerance;
    }

    public static void Equal<T>(T expected, T actual, T tolerance, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
        where T : IFloatingPoint<T>
    {
        if (expected.Equals(actual) || T.Abs(expected - actual) <= tolerance)
            return;

        throw new AssertionException(ErrorFormatter.Format(new EqualWithToleranceAssertionError<T>(expected, actual, tolerance, message, actualExpression, expectedExpression)));
    }

    [OverloadResolutionPriority(-2)]
    public static void Equal(object? expected, [NotNullIfNotNull(nameof(expected))] object? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (TryEqualEnumerables(expected, actual, message, actualExpression, expectedExpression))
            return;

        if (TryEqualMemory(expected, actual, message, actualExpression, expectedExpression))
            return;

        if (!ValuesEqual(expected, actual))
        {
            throw new AssertionException(ErrorFormatter.Format(new EqualAssertionError<object?, object?>(expected, actual, GetStringFirstDifferenceIndex(expected, actual, StringComparison.Ordinal), message, actualExpression, expectedExpression)));
        }
    }

    [OverloadResolutionPriority(-1)]
    public static void Equal<T>(T expected, [NotNullIfNotNull(nameof(expected))] T? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (TryEqualEnumerables(expected, actual, message, actualExpression, expectedExpression))
            return;

        if (TryEqualMemory(expected, actual, message, actualExpression, expectedExpression))
            return;

        if (!ValuesEqual(expected, actual))
        {
            throw new AssertionException(ErrorFormatter.Format(new EqualAssertionError<T, T>(expected, actual, GetStringFirstDifferenceIndex(expected, actual, StringComparison.Ordinal), message, actualExpression, expectedExpression)));
        }
    }

    public static void Equal(string? expected, [NotNullIfNotNull(nameof(expected))] string? actual, bool ignoreCase = false, bool ignoreLineEndingDifferences = false, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        var comparison = GetStringComparison(ignoreCase);
        var expectedValue = ignoreLineEndingDifferences && expected is not null ? NormalizeLineEndings(expected) : expected;
        var actualValue = ignoreLineEndingDifferences && actual is not null ? NormalizeLineEndings(actual) : actual;

        if (string.Equals(expectedValue, actualValue, comparison))
            return;

        throw new AssertionException(ErrorFormatter.Format(new EqualAssertionError<string?, string?>(expectedValue, actualValue, GetStringFirstDifferenceIndex(expectedValue, actualValue, comparison), message, actualExpression, expectedExpression)));
    }

    // Arrays would otherwise bind to the ReadOnlySpan<T> overload, and the implicit conversion turns a null array into
    // an empty span, so a null array would compare equal to an empty one. The priority keeps a call that mixes an array
    // with a collection expression, which prefers ReadOnlySpan<T>, from becoming ambiguous.
    [OverloadResolutionPriority(1)]
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void Equal<T>(T[]? expected, [NotNullIfNotNull(nameof(expected))] T[]? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        // Inlining keeps the success path as cheap as calling the ReadOnlySpan<T> overload directly.
        if (expected is not null && actual is not null)
        {
            Equal(new ReadOnlySpan<T>(expected), new ReadOnlySpan<T>(actual), message, actualExpression, expectedExpression);
        }
        else if (expected is not null || actual is not null)
        {
            ThrowArraysNotEqual(expected, actual, message, actualExpression, expectedExpression);
        }
    }

    [DoesNotReturn]
    private static void ThrowArraysNotEqual<T>(T[]? expected, T[]? actual, string? message, string? actualExpression, string? expectedExpression)
    {
        throw new AssertionException(ErrorFormatter.Format(new EqualAssertionError<T[]?, T[]?>(expected, actual, null, message, actualExpression, expectedExpression)));
    }

    public static void Equal<T>(ReadOnlySpan<T> expected, ReadOnlySpan<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        // Comparing the spans as raw bytes lets SequenceEqual use vectorized instructions. It can only conclude
        // that the spans are equal; every other outcome falls through to the element-by-element comparison,
        // which reports which index differs.
        if (expected.Length == actual.Length && BitwiseEquatable<T>.IsSupported && BitwiseSequenceEqual(expected, actual))
            return;

        EqualSpans<T, T>(expected, actual, message, actualExpression, expectedExpression);
    }

    public static void Equal(ReadOnlySpan<char> expected, ReadOnlySpan<char> actual, bool ignoreCase = false, bool ignoreLineEndingDifferences = false, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        var comparison = GetStringComparison(ignoreCase);
        if (ignoreLineEndingDifferences)
        {
            Equal(NormalizeLineEndings(expected), NormalizeLineEndings(actual), ignoreCase, ignoreLineEndingDifferences: false, message, actualExpression, expectedExpression);
            return;
        }

        if (actual.Equals(expected, comparison))
            return;

        throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanEqualAssertionError<char, char>(expected, actual, GetFirstDifferenceIndex(expected, actual, comparison), message, actualExpression, expectedExpression)));
    }

    [OverloadResolutionPriority(-2)]
    public static void Equal<TExpected, TActual>(ReadOnlySpan<TExpected> expected, ReadOnlySpan<TActual> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        EqualSpans(expected, actual, message, actualExpression, expectedExpression);
    }

    public static void Equal<TExpected, TActual>(ReadOnlyMemory<TExpected> expected, ReadOnlyMemory<TActual> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        EqualSpans(expected.Span, actual.Span, message, actualExpression, expectedExpression);
    }

    private static void EqualSpans<TExpected, TActual>(ReadOnlySpan<TExpected> expected, ReadOnlySpan<TActual> actual, string? message, string? actualExpression, string? expectedExpression)
    {
        if (expected.Length != actual.Length)
        {
            throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanLengthAssertionError<TExpected, TActual>(expected, actual, Math.Min(expected.Length, actual.Length), message, actualExpression, expectedExpression)));
        }

        for (var i = 0; i < expected.Length; i++)
        {
            if (!ValuesEqual(expected[i], actual[i]))
            {
                throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanEqualAssertionError<TExpected, TActual>(expected, actual, i, message, actualExpression, expectedExpression)));
            }
        }
    }

    private static bool IsDefaultComparer<T>(IEqualityComparer<T>? comparer)
    {
        return comparer is null || object.ReferenceEquals(comparer, EqualityComparer<T>.Default);
    }

    private static bool TryGetSpan<T>(IEnumerable<T> source, out ReadOnlySpan<T> span)
    {
        switch (source)
        {
            case T[] array:
                span = array;
                return true;

            case List<T> list:
                span = CollectionsMarshal.AsSpan(list);
                return true;

            default:
                span = default;
                return false;
        }
    }

    /// <summary>Compares two spans the way <see cref="EqualityComparer{T}.Default"/> would, element by element.</summary>
    private static bool DefaultComparerSpansEqual<T>(ReadOnlySpan<T> expected, ReadOnlySpan<T> actual)
    {
        if (expected.Length != actual.Length)
            return false;

        if (BitwiseEquatable<T>.IsSupported)
            return BitwiseSequenceEqual(expected, actual);

        for (var i = 0; i < expected.Length; i++)
        {
            if (!EqualityComparer<T>.Default.Equals(expected[i], actual[i]))
                return false;
        }

        return true;
    }

    private static bool BitwiseSequenceEqual<T>(ReadOnlySpan<T> expected, ReadOnlySpan<T> actual)
    {
        if (expected.IsEmpty)
            return true;

        var elementSize = Unsafe.SizeOf<T>();
        if (expected.Length > int.MaxValue / elementSize)
            return false;

        var byteCount = expected.Length * elementSize;
        // Both spans have the same length, checked by every caller, and BitwiseEquatable<T> only admits primitive
        // types, which have no padding, so the byte views cover exactly the memory of the elements.
        var expectedBytes = unsafe(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(expected)), byteCount));
        var actualBytes = unsafe(MemoryMarshal.CreateReadOnlySpan(ref Unsafe.As<T, byte>(ref MemoryMarshal.GetReference(actual)), byteCount));

        return expectedBytes.SequenceEqual(actualBytes);
    }

    private static class BitwiseEquatable<T>
    {
        /// <summary>
        /// Gets a value indicating whether two values of <typeparamref name="T"/> are equal exactly when their bits are equal.
        /// Floating-point types are excluded because <c>+0.0</c> and <c>-0.0</c> are equal but have different bits.
        /// </summary>
        public static readonly bool IsSupported =
            typeof(T) == typeof(byte)
            || typeof(T) == typeof(sbyte)
            || typeof(T) == typeof(short)
            || typeof(T) == typeof(ushort)
            || typeof(T) == typeof(char)
            || typeof(T) == typeof(int)
            || typeof(T) == typeof(uint)
            || typeof(T) == typeof(long)
            || typeof(T) == typeof(ulong)
            || typeof(T) == typeof(nint)
            || typeof(T) == typeof(nuint)
            || typeof(T) == typeof(bool)
            || typeof(T).IsEnum;
    }

    public static void Equal<T>(IEnumerable<T> expected, [NotNullIfNotNull(nameof(expected))] IEnumerable<T>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new EqualAssertionError<IEnumerable<T>, IEnumerable<T>?>(expected, actual, null, message, actualExpression, expectedExpression)));
        }

        EqualCollections<T>(expected, actual, comparer: null, message, actualExpression, expectedExpression);
    }

    public static void Equal<T>(IEnumerable<T> expected, [NotNullIfNotNull(nameof(expected))] IEnumerable<T>? actual, IEqualityComparer<T>? comparer, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new EqualAssertionError<IEnumerable<T>, IEnumerable<T>?>(expected, actual, null, message, actualExpression, expectedExpression)));
        }

        EqualCollections<T>(expected, actual, comparer, message, actualExpression, expectedExpression);
    }

    [OverloadResolutionPriority(-2)]
    public static void Equal<TExpected, TActual>(IEnumerable<TExpected> expected, [NotNullIfNotNull(nameof(expected))] IEnumerable<TActual>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new EqualAssertionError<IEnumerable<TExpected>, IEnumerable<TActual>?>(expected, actual, null, message, actualExpression, expectedExpression)));
        }

        EqualCollections(expected, actual, comparer: (System.Collections.IEqualityComparer?)null, message, actualExpression, expectedExpression);
    }

    private static void EqualCollections<T>(IEnumerable<T> expected, IEnumerable<T> actual, IEqualityComparer<T>? comparer, string? message, string? actualExpression, string? expectedExpression)
    {
        // Arrays and lists are the common case. Comparing them in place skips the snapshots, which only exist to
        // describe the failure, and lets the comparison be vectorized. Items equal according to the default comparer
        // are also equal when no comparer is supplied, so only a positive result is trusted; anything else falls
        // through to the general path.
        if (IsDefaultComparer(comparer) && TryGetSpan(expected, out var expectedSpan) && TryGetSpan(actual, out var actualSpan) && DefaultComparerSpansEqual(expectedSpan, actualSpan))
            return;

        using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        using var expectedSnapshot = CollectionSnapshot.Create<T>(expected);

        var index = IndexOfFirstDifference(expectedSnapshot, actualSnapshot, comparer);
        if (index < 0)
            return;

        throw new AssertionException(ErrorFormatter.Format(new CollectionEqualAssertionError<T, T>(expectedSnapshot, actualSnapshot, index, message, actualExpression, expectedExpression)));
    }

    private static void EqualCollections<TExpected, TActual>(IEnumerable<TExpected> expected, IEnumerable<TActual> actual, System.Collections.IEqualityComparer? comparer, string? message, string? actualExpression, string? expectedExpression)
    {
        using var actualSnapshot = CollectionSnapshot.Create<TActual>(actual);
        using var expectedSnapshot = CollectionSnapshot.Create<TExpected>(expected);

        var index = IndexOfFirstValueDifference(expectedSnapshot, actualSnapshot, comparer);
        if (index < 0)
            return;

        throw new AssertionException(ErrorFormatter.Format(new CollectionEqualAssertionError<TExpected, TActual>(expectedSnapshot, actualSnapshot, index, message, actualExpression, expectedExpression)));
    }

    /// <summary>
    /// Returns the index of the first position where the collections differ, or -1 when they hold equal items in the same order.
    /// </summary>
    /// <remarks>
    /// Without a comparer, items are compared the way <c>Assert.Equal</c> compares two values, so nested collections are
    /// compared by content. <c>Assert.Equal</c> and <c>Assert.NotEqual</c> both decide through this method so they stay
    /// exact complements.
    /// </remarks>
    private static int IndexOfFirstDifference<T>(CollectionSnapshot<T> expected, CollectionSnapshot<T> actual, IEqualityComparer<T>? comparer)
    {
        if (comparer is null)
            return IndexOfFirstValueDifference(expected, actual, comparer: null);

        for (var index = 0; ; index++)
        {
            var actualHasNext = actual.TryGetItem(index, out var actualItem);
            var expectedHasNext = expected.TryGetItem(index, out var expectedItem);
            if (!actualHasNext && !expectedHasNext)
                return -1;

            if (actualHasNext != expectedHasNext || !comparer.Equals(expectedItem, actualItem))
                return index;
        }
    }

    private static int IndexOfFirstValueDifference<TExpected, TActual>(CollectionSnapshot<TExpected> expected, CollectionSnapshot<TActual> actual, System.Collections.IEqualityComparer? comparer)
    {
        for (var index = 0; ; index++)
        {
            var actualHasNext = actual.TryGetItem(index, out var actualItem);
            var expectedHasNext = expected.TryGetItem(index, out var expectedItem);
            if (!actualHasNext && !expectedHasNext)
                return -1;

            if (actualHasNext != expectedHasNext || !ValuesEqual(expectedItem, actualItem, comparer))
                return index;
        }
    }

    private static async Task<int> IndexOfFirstDifferenceAsync<T>(AsyncCollectionSnapshot<T> expected, AsyncCollectionSnapshot<T> actual, IEqualityComparer<T>? comparer)
    {
        if (comparer is null)
            return await IndexOfFirstValueDifferenceAsync(expected, actual, comparer: null).ConfigureAwait(false);

        for (var index = 0; ; index++)
        {
            var (actualHasNext, actualItem) = await actual.TryGetItem(index).ConfigureAwait(false);
            var (expectedHasNext, expectedItem) = await expected.TryGetItem(index).ConfigureAwait(false);
            if (!actualHasNext && !expectedHasNext)
                return -1;

            if (actualHasNext != expectedHasNext || !comparer.Equals(expectedItem, actualItem))
                return index;
        }
    }

    private static async Task<int> IndexOfFirstValueDifferenceAsync<TExpected, TActual>(AsyncCollectionSnapshot<TExpected> expected, AsyncCollectionSnapshot<TActual> actual, System.Collections.IEqualityComparer? comparer)
    {
        for (var index = 0; ; index++)
        {
            var (actualHasNext, actualItem) = await actual.TryGetItem(index).ConfigureAwait(false);
            var (expectedHasNext, expectedItem) = await expected.TryGetItem(index).ConfigureAwait(false);
            if (!actualHasNext && !expectedHasNext)
                return -1;

            if (actualHasNext != expectedHasNext || !ValuesEqual(expectedItem, actualItem, comparer))
                return index;
        }
    }

    public static async Task Equal<T>(IAsyncEnumerable<T> expected, [NotNullIfNotNull(nameof(expected))] IAsyncEnumerable<T>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            await using var expectedSnapshot = CollectionSnapshot.Create<T>(expected);
            await expectedSnapshot.EnsureCompleteAsync().ConfigureAwait(false);
            throw new AssertionException(ErrorFormatter.Format(new EqualAssertionError<IReadOnlyList<T>, IAsyncEnumerable<T>?>(expectedSnapshot.Items, actual, null, message, actualExpression, expectedExpression)));
        }

        await EqualAsyncCollections<T>(expected, actual, comparer: null, message, actualExpression, expectedExpression).ConfigureAwait(false);
    }

    public static async Task Equal<T>(IAsyncEnumerable<T> expected, [NotNullIfNotNull(nameof(expected))] IAsyncEnumerable<T>? actual, IEqualityComparer<T>? comparer, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            await using var expectedSnapshot = CollectionSnapshot.Create<T>(expected);
            await expectedSnapshot.EnsureCompleteAsync().ConfigureAwait(false);
            throw new AssertionException(ErrorFormatter.Format(new EqualAssertionError<IReadOnlyList<T>, IAsyncEnumerable<T>?>(expectedSnapshot.Items, actual, null, message, actualExpression, expectedExpression)));
        }

        await EqualAsyncCollections<T>(expected, actual, comparer, message, actualExpression, expectedExpression).ConfigureAwait(false);
    }

    [OverloadResolutionPriority(-2)]
    public static async Task Equal<TExpected, TActual>(IAsyncEnumerable<TExpected> expected, [NotNullIfNotNull(nameof(expected))] IAsyncEnumerable<TActual>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            await using var expectedSnapshot = CollectionSnapshot.Create<TExpected>(expected);
            await expectedSnapshot.EnsureCompleteAsync().ConfigureAwait(false);
            throw new AssertionException(ErrorFormatter.Format(new EqualAssertionError<IReadOnlyList<TExpected>, IAsyncEnumerable<TActual>?>(expectedSnapshot.Items, actual, null, message, actualExpression, expectedExpression)));
        }

        await EqualAsyncCollections(expected, actual, comparer: (System.Collections.IEqualityComparer?)null, message, actualExpression, expectedExpression).ConfigureAwait(false);
    }

    public static async Task Equal<T>(IEnumerable<T> expected, [NotNull] IAsyncEnumerable<T>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new EqualAssertionError<IEnumerable<T>, IAsyncEnumerable<T>?>(expected, actual, null, message, actualExpression, expectedExpression)));
        }

        var actualList = new List<T>();
        await foreach (var item in actual.ConfigureAwait(false))
        {
            actualList.Add(item);
        }

        Equal(expected, actualList, message, actualExpression, expectedExpression);
    }

    public static async Task Equal<T>(IAsyncEnumerable<T> expected, [NotNullIfNotNull(nameof(expected))] IEnumerable<T>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            await using var expectedSnapshot = CollectionSnapshot.Create<T>(expected);
            await expectedSnapshot.EnsureCompleteAsync().ConfigureAwait(false);
            throw new AssertionException(ErrorFormatter.Format(new EqualAssertionError<IReadOnlyList<T>, IEnumerable<T>?>(expectedSnapshot.Items, actual, null, message, actualExpression, expectedExpression)));
        }

        var expectedList = new List<T>();
        await foreach (var item in expected.ConfigureAwait(false))
        {
            expectedList.Add(item);
        }

        Equal(expectedList, actual, message, actualExpression, expectedExpression);
    }

    private static async Task EqualAsyncCollections<T>(IAsyncEnumerable<T> expected, IAsyncEnumerable<T> actual, IEqualityComparer<T>? comparer, string? message, string? actualExpression, string? expectedExpression)
    {
        await using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        await using var expectedSnapshot = CollectionSnapshot.Create<T>(expected);

        var index = await IndexOfFirstDifferenceAsync(expectedSnapshot, actualSnapshot, comparer).ConfigureAwait(false);
        if (index < 0)
            return;

        throw new AssertionException(await ErrorFormatter.FormatAsync(new AsyncCollectionEqualAssertionError<T, T>(expectedSnapshot, actualSnapshot, index, message, actualExpression, expectedExpression)).ConfigureAwait(false));
    }

    private static async Task EqualAsyncCollections<TExpected, TActual>(IAsyncEnumerable<TExpected> expected, IAsyncEnumerable<TActual> actual, System.Collections.IEqualityComparer? comparer, string? message, string? actualExpression, string? expectedExpression)
    {
        await using var actualSnapshot = CollectionSnapshot.Create<TActual>(actual);
        await using var expectedSnapshot = CollectionSnapshot.Create<TExpected>(expected);

        var index = await IndexOfFirstValueDifferenceAsync(expectedSnapshot, actualSnapshot, comparer).ConfigureAwait(false);
        if (index < 0)
            return;

        throw new AssertionException(await ErrorFormatter.FormatAsync(new AsyncCollectionEqualAssertionError<TExpected, TActual>(expectedSnapshot, actualSnapshot, index, message, actualExpression, expectedExpression)).ConfigureAwait(false));
    }

    public static void Equal(System.Collections.IEnumerable expected, [NotNullIfNotNull(nameof(expected))] System.Collections.IEnumerable? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new EqualAssertionError<System.Collections.IEnumerable, System.Collections.IEnumerable?>(expected, actual, null, message, actualExpression, expectedExpression)));
        }

        Equal(expected, actual, comparer: null, message, actualExpression, expectedExpression);
    }

    public static void Equal(System.Collections.IEnumerable expected, [NotNullIfNotNull(nameof(expected))] System.Collections.IEnumerable? actual, System.Collections.IEqualityComparer? comparer, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new EqualAssertionError<System.Collections.IEnumerable, System.Collections.IEnumerable?>(expected, actual, null, message, actualExpression, expectedExpression)));
        }

        using var actualSnapshot = CollectionSnapshot.Create(actual);
        using var expectedSnapshot = CollectionSnapshot.Create(expected);

        var index = IndexOfFirstValueDifference(expectedSnapshot, actualSnapshot, comparer);
        if (index < 0)
            return;

        throw new AssertionException(ErrorFormatter.Format(new CollectionEqualAssertionError<object?, object?>(expectedSnapshot, actualSnapshot, index, message, actualExpression, expectedExpression)));
    }

    private static bool TryEqualEnumerables<TExpected, TActual>(TExpected expected, TActual actual, string? message, string? actualExpression, string? expectedExpression)
    {
        if (expected is string || actual is string)
            return false;

        if (expected is System.Collections.IEnumerable expectedEnumerable && actual is System.Collections.IEnumerable actualEnumerable)
        {
            Equal(expectedEnumerable, actualEnumerable, message, actualExpression, expectedExpression);
            return true;
        }

        return false;
    }

    private static string NormalizeLineEndings(string value)
    {
        // The span overload always copies. Most strings contain no '\r' at all, so return the instance unchanged.
        if (!value.AsSpan().Contains('\r'))
            return value;

        return NormalizeLineEndings(value.AsSpan());
    }

    private static string NormalizeLineEndings(ReadOnlySpan<char> value)
    {
        if (!value.Contains('\r'))
            return value.ToString();

        var result = new System.Text.StringBuilder(value.Length);
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '\r')
            {
                result.Append('\n');
                if (i + 1 < value.Length && value[i + 1] == '\n')
                {
                    i++;
                }
            }
            else
            {
                result.Append(value[i]);
            }
        }

        return result.ToString();
    }

    private static StringComparison GetStringComparison(bool ignoreCase)
    {
        return ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }

    private static int? GetStringFirstDifferenceIndex(object? expected, object? actual, StringComparison comparison)
    {
        if (expected is not string expectedString || actual is not string actualString)
            return null;

        return GetStringFirstDifferenceIndex(expectedString, actualString, comparison);
    }

    private static int? GetStringFirstDifferenceIndex(string? expected, string? actual, StringComparison comparison)
    {
        if (expected is null || actual is null)
            return null;

        var minLength = Math.Min(expected.Length, actual.Length);

        // An ordinal comparison is character by character, so the common prefix can be found with a single
        // vectorized scan instead of one span comparison per character.
        if (comparison is StringComparison.Ordinal)
        {
            var commonLength = expected.AsSpan().CommonPrefixLength(actual.AsSpan());
            if (commonLength < minLength)
                return commonLength;
        }
        else
        {
            for (var i = 0; i < minLength; i++)
            {
                if (!actual.AsSpan(i, 1).Equals(expected.AsSpan(i, 1), comparison))
                    return i;
            }
        }

        if (expected.Length == actual.Length)
            return null;

        return minLength;
    }
}
