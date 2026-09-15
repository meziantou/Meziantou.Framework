using System.Numerics;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    public static void NotEqual(Half expected, Half actual, Half tolerance, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (expected.Equals(actual) || MathF.Abs((float)expected - (float)actual) <= (float)tolerance)
        {
            throw new AssertionException(ErrorFormatter.Format(new NegativeEqualWithToleranceAssertionError<Half>(expected, actual, tolerance, message, actualExpression, expectedExpression)));
        }
    }

    public static void NotEqual(float expected, float actual, float tolerance, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (expected.Equals(actual) || MathF.Abs(expected - actual) <= tolerance)
        {
            throw new AssertionException(ErrorFormatter.Format(new NegativeEqualWithToleranceAssertionError<float>(expected, actual, tolerance, message, actualExpression, expectedExpression)));
        }
    }

    public static void NotEqual(double expected, double actual, double tolerance, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (expected.Equals(actual) || Math.Abs(expected - actual) <= tolerance)
        {
            throw new AssertionException(ErrorFormatter.Format(new NegativeEqualWithToleranceAssertionError<double>(expected, actual, tolerance, message, actualExpression, expectedExpression)));
        }
    }

    public static void NotEqual(decimal expected, decimal actual, decimal tolerance, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (IsWithinTolerance(expected, actual, tolerance))
        {
            throw new AssertionException(ErrorFormatter.Format(new NegativeEqualWithToleranceAssertionError<decimal>(expected, actual, tolerance, message, actualExpression, expectedExpression)));
        }
    }

    public static void NotEqual<T>(T expected, T actual, T tolerance, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
        where T : IFloatingPoint<T>
    {
        if (expected.Equals(actual) || T.Abs(expected - actual) <= tolerance)
        {
            throw new AssertionException(ErrorFormatter.Format(new NegativeEqualWithToleranceAssertionError<T>(expected, actual, tolerance, message, actualExpression, expectedExpression)));
        }
    }

    public static void NotEqual<T>(T expected, T? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        // Memory<T> and ReadOnlyMemory<T> compare by backing object, index and length, so a boxed pair holding equal
        // content has to be unwrapped and compared element by element, exactly as Assert.Equal does.
        if (TryNotEqualMemory(expected, actual, message, actualExpression, expectedExpression))
            return;

        if (ValuesEqual(expected, actual))
        {
            throw new AssertionException(ErrorFormatter.Format(new NotEqualAssertionError<T, T?>("Not expected", expected, actual, actualExpression, expectedExpression, message)));
        }
    }

    [OverloadResolutionPriority(-1)]
    public static void NotEqual<TExpected, TActual>(TExpected expected, TActual? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (TryNotEqualMemory(expected, actual, message, actualExpression, expectedExpression))
            return;

        if (ValuesEqual(expected, actual))
        {
            throw new AssertionException(ErrorFormatter.Format(new NotEqualAssertionError<TExpected, TActual?>("Not expected", expected, actual, actualExpression, expectedExpression, message)));
        }
    }

    public static void NotEqual<T>(ReadOnlySpan<T> expected, ReadOnlySpan<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        // Assert.Equal compares the items as values, so a boxed 1 equals a boxed 1L. Comparing the raw bytes gives the
        // same answer only for the primitive types BitwiseEquatable admits.
        var areEqual = BitwiseEquatable<T>.IsSupported
            ? expected.Length == actual.Length && BitwiseSequenceEqual(expected, actual)
            : SpansEqual(expected, actual);
        if (!areEqual)
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeReadOnlySpanValueAssertionError<T, T>(nameof(NotEqual), "Not expected", expected, actual, actualExpression, expectedExpression, message)));
    }

    [OverloadResolutionPriority(-1)]
    public static void NotEqual<TExpected, TActual>(ReadOnlySpan<TExpected> expected, ReadOnlySpan<TActual> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (!SpansEqual(expected, actual))
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeReadOnlySpanValueAssertionError<TExpected, TActual>(nameof(NotEqual), "Not expected", expected, actual, actualExpression, expectedExpression, message)));
    }

    public static void NotEqual<TExpected, TActual>(ReadOnlyMemory<TExpected> expected, ReadOnlyMemory<TActual> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (!SpansEqual(expected.Span, actual.Span))
            return;

        throw new AssertionException(ErrorFormatter.Format(new NotEqualAssertionError<ReadOnlyMemory<TExpected>, ReadOnlyMemory<TActual>>("Not expected", expected, actual, actualExpression, expectedExpression, message)));
    }

    public static void NotEqual<T>(IEnumerable<T> expected, IEnumerable<T>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        NotEqual(expected, actual, comparer: null, message, actualExpression, expectedExpression);
    }

    public static void NotEqual<T>(IEnumerable<T> expected, IEnumerable<T>? actual, IEqualityComparer<T>? comparer, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
            return;

        using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        using var expectedSnapshot = CollectionSnapshot.Create<T>(expected);
        if (IndexOfFirstDifference(expectedSnapshot, actualSnapshot, comparer) >= 0)
            return;

        // The sequences may not be enumerable twice, so the message is built from what the comparison already read.
        throw new AssertionException(ErrorFormatter.Format(new NotEqualAssertionError<IReadOnlyList<T>, IReadOnlyList<T>>("Not expected", expectedSnapshot.Items, actualSnapshot.Items, actualExpression, expectedExpression, message)));
    }

    [OverloadResolutionPriority(-1)]
    public static void NotEqual<TExpected, TActual>(IEnumerable<TExpected> expected, IEnumerable<TActual>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
            return;

        using var actualSnapshot = CollectionSnapshot.Create<TActual>(actual);
        using var expectedSnapshot = CollectionSnapshot.Create<TExpected>(expected);
        if (IndexOfFirstValueDifference(expectedSnapshot, actualSnapshot, comparer: null) >= 0)
            return;

        throw new AssertionException(ErrorFormatter.Format(new NotEqualAssertionError<IReadOnlyList<TExpected>, IReadOnlyList<TActual>>("Not expected", expectedSnapshot.Items, actualSnapshot.Items, actualExpression, expectedExpression, message)));
    }

    public static async Task NotEqual<T>(IAsyncEnumerable<T> expected, IAsyncEnumerable<T>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        await NotEqual(expected, actual, comparer: null, message, actualExpression, expectedExpression).ConfigureAwait(false);
    }

    public static async Task NotEqual<T>(IAsyncEnumerable<T> expected, IAsyncEnumerable<T>? actual, IEqualityComparer<T>? comparer, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
            return;

        await using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        await using var expectedSnapshot = CollectionSnapshot.Create<T>(expected);
        if (await IndexOfFirstDifferenceAsync(expectedSnapshot, actualSnapshot, comparer).ConfigureAwait(false) >= 0)
            return;

        throw new AssertionException(ErrorFormatter.Format(new NotEqualAssertionError<IReadOnlyList<T>, IReadOnlyList<T>>("Not expected", expectedSnapshot.Items, actualSnapshot.Items, actualExpression, expectedExpression, message)));
    }

    [OverloadResolutionPriority(-1)]
    public static async Task NotEqual<TExpected, TActual>(IAsyncEnumerable<TExpected> expected, IAsyncEnumerable<TActual>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
            return;

        await using var actualSnapshot = CollectionSnapshot.Create<TActual>(actual);
        await using var expectedSnapshot = CollectionSnapshot.Create<TExpected>(expected);
        if (await IndexOfFirstValueDifferenceAsync(expectedSnapshot, actualSnapshot, comparer: null).ConfigureAwait(false) >= 0)
            return;

        throw new AssertionException(ErrorFormatter.Format(new NotEqualAssertionError<IReadOnlyList<TExpected>, IReadOnlyList<TActual>>("Not expected", expectedSnapshot.Items, actualSnapshot.Items, actualExpression, expectedExpression, message)));
    }

    public static void NotEqual(System.Collections.IEnumerable expected, System.Collections.IEnumerable? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        NotEqual(expected, actual, comparer: null, message, actualExpression, expectedExpression);
    }

    public static void NotEqual(System.Collections.IEnumerable expected, System.Collections.IEnumerable? actual, System.Collections.IEqualityComparer? comparer, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (actual is null)
            return;

        using var actualSnapshot = CollectionSnapshot.Create(actual);
        using var expectedSnapshot = CollectionSnapshot.Create(expected);
        if (IndexOfFirstValueDifference(expectedSnapshot, actualSnapshot, comparer) >= 0)
            return;

        throw new AssertionException(ErrorFormatter.Format(new NotEqualAssertionError<IReadOnlyList<object?>, IReadOnlyList<object?>>("Not expected", expectedSnapshot.Items, actualSnapshot.Items, actualExpression, expectedExpression, message)));
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
}
