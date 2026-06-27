using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    public static void NotEqual(Half expected, Half actual, Half tolerance, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (expected.Equals(actual) || MathF.Abs((float)expected - (float)actual) <= (float)tolerance)
        {
            throw new AssertionException(AssertionFormatter.Default.Format(new NegativeEqualWithToleranceAssertionError<Half>(expected, actual, tolerance, message, actualExpression, expectedExpression)));
        }
    }

    public static void NotEqual(float expected, float actual, float tolerance, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (expected.Equals(actual) || MathF.Abs(expected - actual) <= tolerance)
        {
            throw new AssertionException(AssertionFormatter.Default.Format(new NegativeEqualWithToleranceAssertionError<float>(expected, actual, tolerance, message, actualExpression, expectedExpression)));
        }
    }

    public static void NotEqual(double expected, double actual, double tolerance, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (expected.Equals(actual) || Math.Abs(expected - actual) <= tolerance)
        {
            throw new AssertionException(AssertionFormatter.Default.Format(new NegativeEqualWithToleranceAssertionError<double>(expected, actual, tolerance, message, actualExpression, expectedExpression)));
        }
    }

    public static void NotEqual(decimal expected, decimal actual, decimal tolerance, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (expected == actual || Math.Abs(expected - actual) <= tolerance)
        {
            throw new AssertionException(AssertionFormatter.Default.Format(new NegativeEqualWithToleranceAssertionError<decimal>(expected, actual, tolerance, message, actualExpression, expectedExpression)));
        }
    }

    public static void NotEqual<T>(T expected, T actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (ValuesEqual(expected, actual))
        {
            throw new AssertionException(AssertionFormatter.Default.Format(new NotEqualAssertionError<T, T>("Not expected", expected, actual, actualExpression, expectedExpression, message)));
        }
    }

    [OverloadResolutionPriority(-1)]
    public static void NotEqual<TExpected, TActual>(TExpected expected, TActual actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (ValuesEqual(expected, actual))
        {
            throw new AssertionException(AssertionFormatter.Default.Format(new NotEqualAssertionError<TExpected, TActual>("Not expected", expected, actual, actualExpression, expectedExpression, message)));
        }
    }

    public static void NotEqual<T>(ReadOnlySpan<T> expected, ReadOnlySpan<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (expected.SequenceEqual(actual))
        {
            throw new AssertionException(AssertionFormatter.Default.Format(new NegativeReadOnlySpanValueAssertionError<T, T>(nameof(NotEqual), "Not expected", expected, actual, actualExpression, expectedExpression, message)));
        }
    }

    [OverloadResolutionPriority(-1)]
    public static void NotEqual<TExpected, TActual>(ReadOnlySpan<TExpected> expected, ReadOnlySpan<TActual> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (!SpansEqual(expected, actual))
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new NegativeReadOnlySpanValueAssertionError<TExpected, TActual>(nameof(NotEqual), "Not expected", expected, actual, actualExpression, expectedExpression, message)));
    }

    public static void NotEqual<TExpected, TActual>(ReadOnlyMemory<TExpected> expected, ReadOnlyMemory<TActual> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (!SpansEqual(expected.Span, actual.Span))
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new NotEqualAssertionError<ReadOnlyMemory<TExpected>, ReadOnlyMemory<TActual>>("Not expected", expected, actual, actualExpression, expectedExpression, message)));
    }

    public static void NotEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (!CollectionsEqual(expected, actual, (IEqualityComparer<T>)EqualityComparer<T>.Default))
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new NotEqualAssertionError<IEnumerable<T>, IEnumerable<T>>("Not expected", expected, actual, actualExpression, expectedExpression, message)));
    }

    public static void NotEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, IEqualityComparer<T>? comparer, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (!CollectionsEqual(expected, actual, comparer))
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new NotEqualAssertionError<IEnumerable<T>, IEnumerable<T>>("Not expected", expected, actual, actualExpression, expectedExpression, message)));
    }

    [OverloadResolutionPriority(-1)]
    public static void NotEqual<TExpected, TActual>(IEnumerable<TExpected> expected, IEnumerable<TActual> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (!CollectionsEqual(expected, actual, (System.Collections.IEqualityComparer?)null))
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new NotEqualAssertionError<IEnumerable<TExpected>, IEnumerable<TActual>>("Not expected", expected, actual, actualExpression, expectedExpression, message)));
    }

    public static async Task NotEqual<T>(IAsyncEnumerable<T> expected, IAsyncEnumerable<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        await using var actualSnapshot = new AsyncCollectionSnapshot<T>(actual);
        await using var expectedSnapshot = new AsyncCollectionSnapshot<T>(expected);
        if (!await AsyncCollectionsEqual(expectedSnapshot, actualSnapshot, (IEqualityComparer<T>)EqualityComparer<T>.Default).ConfigureAwait(false))
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new NotEqualAssertionError<IReadOnlyList<T>, IReadOnlyList<T>>("Not expected", expectedSnapshot.Items, actualSnapshot.Items, actualExpression, expectedExpression, message)));
    }

    public static async Task NotEqual<T>(IAsyncEnumerable<T> expected, IAsyncEnumerable<T> actual, IEqualityComparer<T>? comparer, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        await using var actualSnapshot = new AsyncCollectionSnapshot<T>(actual);
        await using var expectedSnapshot = new AsyncCollectionSnapshot<T>(expected);
        if (!await AsyncCollectionsEqual(expectedSnapshot, actualSnapshot, comparer).ConfigureAwait(false))
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new NotEqualAssertionError<IReadOnlyList<T>, IReadOnlyList<T>>("Not expected", expectedSnapshot.Items, actualSnapshot.Items, actualExpression, expectedExpression, message)));
    }

    [OverloadResolutionPriority(-1)]
    public static async Task NotEqual<TExpected, TActual>(IAsyncEnumerable<TExpected> expected, IAsyncEnumerable<TActual> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        await using var actualSnapshot = new AsyncCollectionSnapshot<TActual>(actual);
        await using var expectedSnapshot = new AsyncCollectionSnapshot<TExpected>(expected);
        if (!await AsyncCollectionsEqual(expectedSnapshot, actualSnapshot, (System.Collections.IEqualityComparer?)null).ConfigureAwait(false))
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new NotEqualAssertionError<IReadOnlyList<TExpected>, IReadOnlyList<TActual>>("Not expected", expectedSnapshot.Items, actualSnapshot.Items, actualExpression, expectedExpression, message)));
    }

    public static void NotEqual(System.Collections.IEnumerable expected, System.Collections.IEnumerable actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (!CollectionsEqual(EnumerateObjects(expected), EnumerateObjects(actual), (System.Collections.IEqualityComparer?)null))
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new NotEqualAssertionError<System.Collections.IEnumerable, System.Collections.IEnumerable>("Not expected", expected, actual, actualExpression, expectedExpression, message)));
    }

    public static void NotEqual(System.Collections.IEnumerable expected, System.Collections.IEnumerable actual, System.Collections.IEqualityComparer? comparer, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (!CollectionsEqual(EnumerateObjects(expected), EnumerateObjects(actual), comparer))
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new NotEqualAssertionError<System.Collections.IEnumerable, System.Collections.IEnumerable>("Not expected", expected, actual, actualExpression, expectedExpression, message)));
    }

    private static bool SpansEqual<TExpected, TActual>(ReadOnlySpan<TExpected> expected, ReadOnlySpan<TActual> actual)
    {
        if (expected.Length != actual.Length)
            return false;

        for (var i = 0; i < expected.Length; i++)
        {
            if (!ValuesEqual(expected[i], actual[i]))
                return false;
        }

        return true;
    }

    private static bool CollectionsEqual<T>(IEnumerable<T> expected, IEnumerable<T> actual, IEqualityComparer<T>? comparer)
    {
        using var actualSnapshot = new CollectionSnapshot<T>(actual);
        using var expectedSnapshot = new CollectionSnapshot<T>(expected);
        using var actualEnumerator = actualSnapshot.GetEnumerator();
        using var expectedEnumerator = expectedSnapshot.GetEnumerator();
        comparer ??= EqualityComparer<T>.Default;

        while (true)
        {
            var actualHasNext = actualEnumerator.MoveNext();
            var expectedHasNext = expectedEnumerator.MoveNext();
            if (!actualHasNext && !expectedHasNext)
                return true;

            if (actualHasNext != expectedHasNext)
                return false;

            if (!comparer.Equals(expectedEnumerator.Current, actualEnumerator.Current))
                return false;
        }
    }

    private static bool CollectionsEqual<TExpected, TActual>(IEnumerable<TExpected> expected, IEnumerable<TActual> actual, System.Collections.IEqualityComparer? comparer)
    {
        using var actualSnapshot = new CollectionSnapshot<TActual>(actual);
        using var expectedSnapshot = new CollectionSnapshot<TExpected>(expected);
        using var actualEnumerator = actualSnapshot.GetEnumerator();
        using var expectedEnumerator = expectedSnapshot.GetEnumerator();

        while (true)
        {
            var actualHasNext = actualEnumerator.MoveNext();
            var expectedHasNext = expectedEnumerator.MoveNext();
            if (!actualHasNext && !expectedHasNext)
                return true;

            if (actualHasNext != expectedHasNext)
                return false;

            if (!ValuesEqual(expectedEnumerator.Current, actualEnumerator.Current, comparer))
                return false;
        }
    }

    private static async Task<bool> AsyncCollectionsEqual<T>(AsyncCollectionSnapshot<T> expectedSnapshot, AsyncCollectionSnapshot<T> actualSnapshot, IEqualityComparer<T>? comparer)
    {
        await using var actualEnumerator = actualSnapshot.GetAsyncEnumerator();
        await using var expectedEnumerator = expectedSnapshot.GetAsyncEnumerator();
        comparer ??= EqualityComparer<T>.Default;

        while (true)
        {
            var actualHasNext = await actualEnumerator.MoveNextAsync().ConfigureAwait(false);
            var expectedHasNext = await expectedEnumerator.MoveNextAsync().ConfigureAwait(false);
            if (!actualHasNext && !expectedHasNext)
                return true;

            if (actualHasNext != expectedHasNext)
                return false;

            if (!comparer.Equals(expectedEnumerator.Current, actualEnumerator.Current))
                return false;
        }
    }

    private static async Task<bool> AsyncCollectionsEqual<TExpected, TActual>(AsyncCollectionSnapshot<TExpected> expectedSnapshot, AsyncCollectionSnapshot<TActual> actualSnapshot, System.Collections.IEqualityComparer? comparer)
    {
        await using var actualEnumerator = actualSnapshot.GetAsyncEnumerator();
        await using var expectedEnumerator = expectedSnapshot.GetAsyncEnumerator();

        while (true)
        {
            var actualHasNext = await actualEnumerator.MoveNextAsync().ConfigureAwait(false);
            var expectedHasNext = await expectedEnumerator.MoveNextAsync().ConfigureAwait(false);
            if (!actualHasNext && !expectedHasNext)
                return true;

            if (actualHasNext != expectedHasNext)
                return false;

            if (!ValuesEqual(expectedEnumerator.Current, actualEnumerator.Current, comparer))
                return false;
        }
    }
}
