using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

partial class Assert
{
    public static void Equal(Half expected, Half actual, Half tolerance, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (expected.Equals(actual) || MathF.Abs((float)expected - (float)actual) <= (float)tolerance)
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new EqualWithToleranceAssertionError<Half>(expected, actual, tolerance, message, actualExpression, expectedExpression)));
    }

    public static void Equal(float expected, float actual, float tolerance, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (expected.Equals(actual) || MathF.Abs(expected - actual) <= tolerance)
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new EqualWithToleranceAssertionError<float>(expected, actual, tolerance, message, actualExpression, expectedExpression)));
    }

    public static void Equal(double expected, double actual, double tolerance, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (expected.Equals(actual) || Math.Abs(expected - actual) <= tolerance)
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new EqualWithToleranceAssertionError<double>(expected, actual, tolerance, message, actualExpression, expectedExpression)));
    }

    public static void Equal(decimal expected, decimal actual, decimal tolerance, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (expected == actual || Math.Abs(expected - actual) <= tolerance)
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new EqualWithToleranceAssertionError<decimal>(expected, actual, tolerance, message, actualExpression, expectedExpression)));
    }

    public static void Equal<T>(T expected, T actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (TryEqualEnumerables(expected, actual, message, actualExpression, expectedExpression))
            return;

        if (TryEqualMemory(expected, actual, message, actualExpression, expectedExpression))
            return;

        if (!ValuesEqual(expected, actual))
        {
            throw new AssertionException(AssertionFormatter.Default.Format(new EqualAssertionError<T, T>(expected, actual, message, actualExpression, expectedExpression)));
        }
    }

    [OverloadResolutionPriority(-1)]
    public static void Equal<TExpected, TActual>(TExpected expected, TActual actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (TryEqualEnumerables(expected, actual, message, actualExpression, expectedExpression))
            return;

        if (TryEqualMemory(expected, actual, message, actualExpression, expectedExpression))
            return;

        if (!ValuesEqual(expected, actual))
        {
            throw new AssertionException(AssertionFormatter.Default.Format(new EqualAssertionError<TExpected, TActual>(expected, actual, message, actualExpression, expectedExpression)));
        }
    }

    public static void Equal<T>(ReadOnlySpan<T> expected, ReadOnlySpan<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        EqualSpans<T, T>(expected, actual, message, actualExpression, expectedExpression);
    }

    [OverloadResolutionPriority(-1)]
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
            throw new AssertionException(AssertionFormatter.Default.Format(new ReadOnlySpanLengthAssertionError<TExpected, TActual>(expected, actual, message, actualExpression, expectedExpression)));
        }

        for (var i = 0; i < expected.Length; i++)
        {
            if (!ValuesEqual(expected[i], actual[i]))
            {
                throw new AssertionException(AssertionFormatter.Default.Format(new ReadOnlySpanEqualAssertionError<TExpected, TActual>(expected, actual, i, message, actualExpression, expectedExpression)));
            }
        }
    }

    public static void Equal<T>(IEnumerable<T> expected, IEnumerable<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        EqualCollections<T>(expected, actual, EqualityComparer<T>.Default, message, actualExpression, expectedExpression);
    }

    public static void Equal<T>(IEnumerable<T> expected, IEnumerable<T> actual, IEqualityComparer<T>? comparer, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        EqualCollections<T>(expected, actual, comparer, message, actualExpression, expectedExpression);
    }

    [OverloadResolutionPriority(-1)]
    public static void Equal<TExpected, TActual>(IEnumerable<TExpected> expected, IEnumerable<TActual> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        EqualCollections(expected, actual, comparer: (System.Collections.IEqualityComparer?)null, message, actualExpression, expectedExpression);
    }

    private static void EqualCollections<T>(IEnumerable<T> expected, IEnumerable<T> actual, IEqualityComparer<T>? comparer, string? message, string? actualExpression, string? expectedExpression)
    {
        using var actualSnapshot = new CollectionSnapshot<T>(actual);
        using var expectedSnapshot = new CollectionSnapshot<T>(expected);
        using var actualEnumerator = actualSnapshot.GetEnumerator();
        using var expectedEnumerator = expectedSnapshot.GetEnumerator();
        comparer ??= EqualityComparer<T>.Default;

        var index = 0;
        while (true)
        {
            var actualHasNext = actualEnumerator.MoveNext();
            var expectedHasNext = expectedEnumerator.MoveNext();

            if (!actualHasNext && !expectedHasNext)
                break;

            if (actualHasNext != expectedHasNext)
            {
                throw new AssertionException(AssertionFormatter.Default.Format(new CollectionEqualAssertionError<T, T>(expectedSnapshot, actualSnapshot, index, message, actualExpression, expectedExpression)));
            }

            if (!comparer.Equals(expectedEnumerator.Current, actualEnumerator.Current))
            {
                throw new AssertionException(AssertionFormatter.Default.Format(new CollectionEqualAssertionError<T, T>(expectedSnapshot, actualSnapshot, index, message, actualExpression, expectedExpression)));
            }

            index++;
        }
    }

    private static void EqualCollections<TExpected, TActual>(IEnumerable<TExpected> expected, IEnumerable<TActual> actual, System.Collections.IEqualityComparer? comparer, string? message, string? actualExpression, string? expectedExpression)
    {
        using var actualSnapshot = new CollectionSnapshot<TActual>(actual);
        using var expectedSnapshot = new CollectionSnapshot<TExpected>(expected);
        using var actualEnumerator = actualSnapshot.GetEnumerator();
        using var expectedEnumerator = expectedSnapshot.GetEnumerator();

        var index = 0;
        while (true)
        {
            var actualHasNext = actualEnumerator.MoveNext();
            var expectedHasNext = expectedEnumerator.MoveNext();

            if (!actualHasNext && !expectedHasNext)
                break;

            if (actualHasNext != expectedHasNext)
            {
                throw new AssertionException(AssertionFormatter.Default.Format(new CollectionEqualAssertionError<TExpected, TActual>(expectedSnapshot, actualSnapshot, index, message, actualExpression, expectedExpression)));
            }

            if (!ValuesEqual(expectedEnumerator.Current, actualEnumerator.Current, comparer))
            {
                throw new AssertionException(AssertionFormatter.Default.Format(new CollectionEqualAssertionError<TExpected, TActual>(expectedSnapshot, actualSnapshot, index, message, actualExpression, expectedExpression)));
            }

            index++;
        }
    }

    public static async Task Equal<T>(IAsyncEnumerable<T> expected, IAsyncEnumerable<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        await EqualAsyncCollections<T>(expected, actual, EqualityComparer<T>.Default, message, actualExpression, expectedExpression).ConfigureAwait(false);
    }

    public static async Task Equal<T>(IAsyncEnumerable<T> expected, IAsyncEnumerable<T> actual, IEqualityComparer<T>? comparer, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        await EqualAsyncCollections<T>(expected, actual, comparer, message, actualExpression, expectedExpression).ConfigureAwait(false);
    }

    [OverloadResolutionPriority(-1)]
    public static async Task Equal<TExpected, TActual>(IAsyncEnumerable<TExpected> expected, IAsyncEnumerable<TActual> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        await EqualAsyncCollections(expected, actual, comparer: (System.Collections.IEqualityComparer?)null, message, actualExpression, expectedExpression).ConfigureAwait(false);
    }

    private static async Task EqualAsyncCollections<T>(IAsyncEnumerable<T> expected, IAsyncEnumerable<T> actual, IEqualityComparer<T>? comparer, string? message, string? actualExpression, string? expectedExpression)
    {
        await using var actualSnapshot = new AsyncCollectionSnapshot<T>(actual);
        await using var expectedSnapshot = new AsyncCollectionSnapshot<T>(expected);
        await using var actualEnumerator = actualSnapshot.GetAsyncEnumerator();
        await using var expectedEnumerator = expectedSnapshot.GetAsyncEnumerator();
        comparer ??= EqualityComparer<T>.Default;

        var index = 0;
        while (true)
        {
            var actualHasNext = await actualEnumerator.MoveNextAsync().ConfigureAwait(false);
            var expectedHasNext = await expectedEnumerator.MoveNextAsync().ConfigureAwait(false);

            if (!actualHasNext && !expectedHasNext)
                break;

            if (actualHasNext != expectedHasNext)
            {
                throw new AssertionException(await AssertionFormatter.Default.FormatAsync(new AsyncCollectionEqualAssertionError<T, T>(expectedSnapshot, actualSnapshot, index, message, actualExpression, expectedExpression)).ConfigureAwait(false));
            }

            if (!comparer.Equals(expectedEnumerator.Current, actualEnumerator.Current))
            {
                throw new AssertionException(await AssertionFormatter.Default.FormatAsync(new AsyncCollectionEqualAssertionError<T, T>(expectedSnapshot, actualSnapshot, index, message, actualExpression, expectedExpression)).ConfigureAwait(false));
            }

            index++;
        }
    }

    private static async Task EqualAsyncCollections<TExpected, TActual>(IAsyncEnumerable<TExpected> expected, IAsyncEnumerable<TActual> actual, System.Collections.IEqualityComparer? comparer, string? message, string? actualExpression, string? expectedExpression)
    {
        await using var actualSnapshot = new AsyncCollectionSnapshot<TActual>(actual);
        await using var expectedSnapshot = new AsyncCollectionSnapshot<TExpected>(expected);
        await using var actualEnumerator = actualSnapshot.GetAsyncEnumerator();
        await using var expectedEnumerator = expectedSnapshot.GetAsyncEnumerator();

        var index = 0;
        while (true)
        {
            var actualHasNext = await actualEnumerator.MoveNextAsync().ConfigureAwait(false);
            var expectedHasNext = await expectedEnumerator.MoveNextAsync().ConfigureAwait(false);

            if (!actualHasNext && !expectedHasNext)
                break;

            if (actualHasNext != expectedHasNext)
            {
                throw new AssertionException(await AssertionFormatter.Default.FormatAsync(new AsyncCollectionEqualAssertionError<TExpected, TActual>(expectedSnapshot, actualSnapshot, index, message, actualExpression, expectedExpression)).ConfigureAwait(false));
            }

            if (!ValuesEqual(expectedEnumerator.Current, actualEnumerator.Current, comparer))
            {
                throw new AssertionException(await AssertionFormatter.Default.FormatAsync(new AsyncCollectionEqualAssertionError<TExpected, TActual>(expectedSnapshot, actualSnapshot, index, message, actualExpression, expectedExpression)).ConfigureAwait(false));
            }

            index++;
        }
    }

    public static void Equal(System.Collections.IEnumerable expected, System.Collections.IEnumerable actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        Equal(expected, actual, comparer: null, message, actualExpression, expectedExpression);
    }

    public static void Equal(System.Collections.IEnumerable expected, System.Collections.IEnumerable actual, System.Collections.IEqualityComparer? comparer, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        using var actualSnapshot = new CollectionSnapshot<object?>(EnumerateObjects(actual));
        using var expectedSnapshot = new CollectionSnapshot<object?>(EnumerateObjects(expected));
        using var actualEnumerator = actualSnapshot.GetEnumerator();
        using var expectedEnumerator = expectedSnapshot.GetEnumerator();

        var index = 0;
        while (true)
        {
            var actualHasNext = actualEnumerator.MoveNext();
            var expectedHasNext = expectedEnumerator.MoveNext();

            if (!actualHasNext && !expectedHasNext)
                break;

            if (actualHasNext != expectedHasNext)
            {
                throw new AssertionException(AssertionFormatter.Default.Format(new CollectionEqualAssertionError<object?, object?>(expectedSnapshot, actualSnapshot, index, message, actualExpression, expectedExpression)));
            }

            if (!ValuesEqual(expectedEnumerator.Current, actualEnumerator.Current, comparer))
            {
                throw new AssertionException(AssertionFormatter.Default.Format(new CollectionEqualAssertionError<object?, object?>(expectedSnapshot, actualSnapshot, index, message, actualExpression, expectedExpression)));
            }

            index++;
        }
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
}
