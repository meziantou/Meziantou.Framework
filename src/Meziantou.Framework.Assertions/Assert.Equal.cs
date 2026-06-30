using System.Runtime.CompilerServices;

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
        if (expected == actual || Math.Abs(expected - actual) <= tolerance)
            return;

        throw new AssertionException(ErrorFormatter.Format(new EqualWithToleranceAssertionError<decimal>(expected, actual, tolerance, message, actualExpression, expectedExpression)));
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
            throw new AssertionException(ErrorFormatter.Format(new EqualAssertionError<object?, object?>(expected, actual, message, actualExpression, expectedExpression)));
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
            throw new AssertionException(ErrorFormatter.Format(new EqualAssertionError<T, T>(expected, actual, message, actualExpression, expectedExpression)));
        }
    }

    public static void Equal(string? expected, [NotNullIfNotNull(nameof(expected))] string? actual, bool ignoreCase = false, bool ignoreLineEndingDifferences = false, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        var comparison = GetStringComparison(ignoreCase);
        var expectedValue = ignoreLineEndingDifferences && expected is not null ? NormalizeLineEndings(expected) : expected;
        var actualValue = ignoreLineEndingDifferences && actual is not null ? NormalizeLineEndings(actual) : actual;

        if (string.Equals(expectedValue, actualValue, comparison))
            return;

        throw new AssertionException(ErrorFormatter.Format(new EqualAssertionError<string?, string?>(expectedValue, actualValue, message, actualExpression, expectedExpression)));
    }

    public static void Equal<T>(ReadOnlySpan<T> expected, ReadOnlySpan<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
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
            throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanLengthAssertionError<TExpected, TActual>(expected, actual, message, actualExpression, expectedExpression)));
        }

        for (var i = 0; i < expected.Length; i++)
        {
            if (!ValuesEqual(expected[i], actual[i]))
            {
                throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanEqualAssertionError<TExpected, TActual>(expected, actual, i, message, actualExpression, expectedExpression)));
            }
        }
    }

    public static void Equal<T>(IEnumerable<T> expected, [NotNullIfNotNull(nameof(expected))] IEnumerable<T>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new EqualAssertionError<IEnumerable<T>, IEnumerable<T>?>(expected, actual, message, actualExpression, expectedExpression)));
        }

        EqualCollections<T>(expected, actual, EqualityComparer<T>.Default, message, actualExpression, expectedExpression);
    }

    public static void Equal<T>(IEnumerable<T> expected, [NotNullIfNotNull(nameof(expected))] IEnumerable<T>? actual, IEqualityComparer<T>? comparer, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new EqualAssertionError<IEnumerable<T>, IEnumerable<T>?>(expected, actual, message, actualExpression, expectedExpression)));
        }

        EqualCollections<T>(expected, actual, comparer, message, actualExpression, expectedExpression);
    }

    [OverloadResolutionPriority(-2)]
    public static void Equal<TExpected, TActual>(IEnumerable<TExpected> expected, [NotNullIfNotNull(nameof(expected))] IEnumerable<TActual>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new EqualAssertionError<IEnumerable<TExpected>, IEnumerable<TActual>?>(expected, actual, message, actualExpression, expectedExpression)));
        }

        EqualCollections(expected, actual, comparer: (System.Collections.IEqualityComparer?)null, message, actualExpression, expectedExpression);
    }

    private static void EqualCollections<T>(IEnumerable<T> expected, IEnumerable<T> actual, IEqualityComparer<T>? comparer, string? message, string? actualExpression, string? expectedExpression)
    {
        using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        using var expectedSnapshot = CollectionSnapshot.Create<T>(expected);
        comparer ??= EqualityComparer<T>.Default;

        var index = 0;
        while (true)
        {
            var actualHasNext = actualSnapshot.TryGetItem(index, out var actualItem);
            var expectedHasNext = expectedSnapshot.TryGetItem(index, out var expectedItem);

            if (!actualHasNext && !expectedHasNext)
                break;

            if (actualHasNext != expectedHasNext)
            {
                throw new AssertionException(ErrorFormatter.Format(new CollectionEqualAssertionError<T, T>(expectedSnapshot, actualSnapshot, index, message, actualExpression, expectedExpression)));
            }

            if (!comparer.Equals(expectedItem, actualItem))
            {
                throw new AssertionException(ErrorFormatter.Format(new CollectionEqualAssertionError<T, T>(expectedSnapshot, actualSnapshot, index, message, actualExpression, expectedExpression)));
            }

            index++;
        }
    }

    private static void EqualCollections<TExpected, TActual>(IEnumerable<TExpected> expected, IEnumerable<TActual> actual, System.Collections.IEqualityComparer? comparer, string? message, string? actualExpression, string? expectedExpression)
    {
        using var actualSnapshot = CollectionSnapshot.Create<TActual>(actual);
        using var expectedSnapshot = CollectionSnapshot.Create<TExpected>(expected);

        var index = 0;
        while (true)
        {
            var actualHasNext = actualSnapshot.TryGetItem(index, out var actualItem);
            var expectedHasNext = expectedSnapshot.TryGetItem(index, out var expectedItem);

            if (!actualHasNext && !expectedHasNext)
                break;

            if (actualHasNext != expectedHasNext)
            {
                throw new AssertionException(ErrorFormatter.Format(new CollectionEqualAssertionError<TExpected, TActual>(expectedSnapshot, actualSnapshot, index, message, actualExpression, expectedExpression)));
            }

            if (!ValuesEqual(expectedItem, actualItem, comparer))
            {
                throw new AssertionException(ErrorFormatter.Format(new CollectionEqualAssertionError<TExpected, TActual>(expectedSnapshot, actualSnapshot, index, message, actualExpression, expectedExpression)));
            }

            index++;
        }
    }

    public static async Task Equal<T>(IAsyncEnumerable<T> expected, [NotNullIfNotNull(nameof(expected))] IAsyncEnumerable<T>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            await using var expectedSnapshot = CollectionSnapshot.Create<T>(expected);
            await EnsureCompleteAsync(expectedSnapshot).ConfigureAwait(false);
            throw new AssertionException(ErrorFormatter.Format(new EqualAssertionError<IReadOnlyList<T>, IAsyncEnumerable<T>?>(expectedSnapshot.Items, actual, message, actualExpression, expectedExpression)));
        }

        await EqualAsyncCollections<T>(expected, actual, EqualityComparer<T>.Default, message, actualExpression, expectedExpression).ConfigureAwait(false);
    }

    public static async Task Equal<T>(IAsyncEnumerable<T> expected, [NotNullIfNotNull(nameof(expected))] IAsyncEnumerable<T>? actual, IEqualityComparer<T>? comparer, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            await using var expectedSnapshot = CollectionSnapshot.Create<T>(expected);
            await EnsureCompleteAsync(expectedSnapshot).ConfigureAwait(false);
            throw new AssertionException(ErrorFormatter.Format(new EqualAssertionError<IReadOnlyList<T>, IAsyncEnumerable<T>?>(expectedSnapshot.Items, actual, message, actualExpression, expectedExpression)));
        }

        await EqualAsyncCollections<T>(expected, actual, comparer, message, actualExpression, expectedExpression).ConfigureAwait(false);
    }

    [OverloadResolutionPriority(-2)]
    public static async Task Equal<TExpected, TActual>(IAsyncEnumerable<TExpected> expected, [NotNullIfNotNull(nameof(expected))] IAsyncEnumerable<TActual>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            await using var expectedSnapshot = CollectionSnapshot.Create<TExpected>(expected);
            await EnsureCompleteAsync(expectedSnapshot).ConfigureAwait(false);
            throw new AssertionException(ErrorFormatter.Format(new EqualAssertionError<IReadOnlyList<TExpected>, IAsyncEnumerable<TActual>?>(expectedSnapshot.Items, actual, message, actualExpression, expectedExpression)));
        }

        await EqualAsyncCollections(expected, actual, comparer: (System.Collections.IEqualityComparer?)null, message, actualExpression, expectedExpression).ConfigureAwait(false);
    }

    public static async Task Equal<T>(IEnumerable<T> expected, [NotNull] IAsyncEnumerable<T>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new EqualAssertionError<IEnumerable<T>, IAsyncEnumerable<T>?>(expected, actual, message, actualExpression, expectedExpression)));
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
            await EnsureCompleteAsync(expectedSnapshot).ConfigureAwait(false);
            throw new AssertionException(ErrorFormatter.Format(new EqualAssertionError<IReadOnlyList<T>, IEnumerable<T>?>(expectedSnapshot.Items, actual, message, actualExpression, expectedExpression)));
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
        comparer ??= EqualityComparer<T>.Default;

        var index = 0;
        while (true)
        {
            var (actualHasNext, actualItem) = await actualSnapshot.TryGetItem(index).ConfigureAwait(false);
            var (expectedHasNext, expectedItem) = await expectedSnapshot.TryGetItem(index).ConfigureAwait(false);

            if (!actualHasNext && !expectedHasNext)
                break;

            if (actualHasNext != expectedHasNext)
            {
                throw new AssertionException(await ErrorFormatter.FormatAsync(new AsyncCollectionEqualAssertionError<T, T>(expectedSnapshot, actualSnapshot, index, message, actualExpression, expectedExpression)).ConfigureAwait(false));
            }

            if (!comparer.Equals(expectedItem, actualItem))
            {
                throw new AssertionException(await ErrorFormatter.FormatAsync(new AsyncCollectionEqualAssertionError<T, T>(expectedSnapshot, actualSnapshot, index, message, actualExpression, expectedExpression)).ConfigureAwait(false));
            }

            index++;
        }
    }

    private static async Task EqualAsyncCollections<TExpected, TActual>(IAsyncEnumerable<TExpected> expected, IAsyncEnumerable<TActual> actual, System.Collections.IEqualityComparer? comparer, string? message, string? actualExpression, string? expectedExpression)
    {
        await using var actualSnapshot = CollectionSnapshot.Create<TActual>(actual);
        await using var expectedSnapshot = CollectionSnapshot.Create<TExpected>(expected);

        var index = 0;
        while (true)
        {
            var (actualHasNext, actualItem) = await actualSnapshot.TryGetItem(index).ConfigureAwait(false);
            var (expectedHasNext, expectedItem) = await expectedSnapshot.TryGetItem(index).ConfigureAwait(false);

            if (!actualHasNext && !expectedHasNext)
                break;

            if (actualHasNext != expectedHasNext)
            {
                throw new AssertionException(await ErrorFormatter.FormatAsync(new AsyncCollectionEqualAssertionError<TExpected, TActual>(expectedSnapshot, actualSnapshot, index, message, actualExpression, expectedExpression)).ConfigureAwait(false));
            }

            if (!ValuesEqual(expectedItem, actualItem, comparer))
            {
                throw new AssertionException(await ErrorFormatter.FormatAsync(new AsyncCollectionEqualAssertionError<TExpected, TActual>(expectedSnapshot, actualSnapshot, index, message, actualExpression, expectedExpression)).ConfigureAwait(false));
            }

            index++;
        }
    }

    public static void Equal(System.Collections.IEnumerable expected, [NotNullIfNotNull(nameof(expected))] System.Collections.IEnumerable? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new EqualAssertionError<System.Collections.IEnumerable, System.Collections.IEnumerable?>(expected, actual, message, actualExpression, expectedExpression)));
        }

        Equal(expected, actual, comparer: null, message, actualExpression, expectedExpression);
    }

    public static void Equal(System.Collections.IEnumerable expected, [NotNullIfNotNull(nameof(expected))] System.Collections.IEnumerable? actual, System.Collections.IEqualityComparer? comparer, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
        {
            throw new AssertionException(ErrorFormatter.Format(new EqualAssertionError<System.Collections.IEnumerable, System.Collections.IEnumerable?>(expected, actual, message, actualExpression, expectedExpression)));
        }

        using var actualSnapshot = CollectionSnapshot.Create(actual);
        using var expectedSnapshot = CollectionSnapshot.Create(expected);

        var index = 0;
        while (true)
        {
            var actualHasNext = actualSnapshot.TryGetItem(index, out var actualItem);
            var expectedHasNext = expectedSnapshot.TryGetItem(index, out var expectedItem);

            if (!actualHasNext && !expectedHasNext)
                break;

            if (actualHasNext != expectedHasNext)
            {
                throw new AssertionException(ErrorFormatter.Format(new CollectionEqualAssertionError<object?, object?>(expectedSnapshot, actualSnapshot, index, message, actualExpression, expectedExpression)));
            }

            if (!ValuesEqual(expectedItem, actualItem, comparer))
            {
                throw new AssertionException(ErrorFormatter.Format(new CollectionEqualAssertionError<object?, object?>(expectedSnapshot, actualSnapshot, index, message, actualExpression, expectedExpression)));
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
}
