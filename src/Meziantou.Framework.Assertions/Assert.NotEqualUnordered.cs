using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    /// <summary>Asserts that two sequences do not contain the same items, regardless of their order.</summary>
    /// <remarks>
    /// This assertion is the exact complement of <c>EqualUnordered</c>: two <see langword="null"/> sequences are equal, and
    /// a <see langword="null"/> sequence differs from any other sequence.
    /// </remarks>
    public static void NotEqualUnordered<T>(IEnumerable<T>? expected, IEnumerable<T>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        NotEqualUnordered(expected, actual, comparer: null, message, actualExpression, expectedExpression);
    }

    /// <summary>Asserts that two sequences do not contain the same items, regardless of their order.</summary>
    /// <param name="comparer">The comparer used to compare items. When <see langword="null"/>, items are compared like <c>Assert.Equal</c> compares two values.</param>
    public static void NotEqualUnordered<T>(IEnumerable<T>? expected, IEnumerable<T>? actual, IEqualityComparer<T>? comparer, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (expected is null || actual is null)
        {
            NotEqualUnorderedNull(expected, actual, message, actualExpression, expectedExpression);
            return;
        }

        using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        using var expectedSnapshot = CollectionSnapshot.Create<T>(expected);
        actualSnapshot.EnsureComplete();
        expectedSnapshot.EnsureComplete();
        if (!AreEqualUnordered(expectedSnapshot.Items, actualSnapshot.Items, comparer))
            return;

        throw new AssertionException(ErrorFormatter.Format(new NotEqualUnorderedAssertionError<IReadOnlyList<T>, IReadOnlyList<T>>("Not expected", expectedSnapshot.Items, actualSnapshot.Items, actualExpression, expectedExpression, message)));
    }

    [OverloadResolutionPriority(-1)]
    public static void NotEqualUnordered<TExpected, TActual>(IEnumerable<TExpected>? expected, IEnumerable<TActual>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (expected is null || actual is null)
        {
            NotEqualUnorderedNull(expected, actual, message, actualExpression, expectedExpression);
            return;
        }

        using var actualSnapshot = CollectionSnapshot.Create<TActual>(actual);
        using var expectedSnapshot = CollectionSnapshot.Create<TExpected>(expected);
        actualSnapshot.EnsureComplete();
        expectedSnapshot.EnsureComplete();
        if (!AreValuesEqualUnordered(expectedSnapshot.Items, actualSnapshot.Items, comparer: null))
            return;

        throw new AssertionException(ErrorFormatter.Format(new NotEqualUnorderedAssertionError<IReadOnlyList<TExpected>, IReadOnlyList<TActual>>("Not expected", expectedSnapshot.Items, actualSnapshot.Items, actualExpression, expectedExpression, message)));
    }

    /// <inheritdoc cref="NotEqualUnordered{T}(IEnumerable{T}, IEnumerable{T}, string, string, string)"/>
    public static async Task NotEqualUnordered<T>(IAsyncEnumerable<T>? expected, IAsyncEnumerable<T>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        await NotEqualUnordered(expected, actual, comparer: null, message, actualExpression, expectedExpression).ConfigureAwait(false);
    }

    /// <inheritdoc cref="NotEqualUnordered{T}(IEnumerable{T}, IEnumerable{T}, IEqualityComparer{T}, string, string, string)"/>
    public static async Task NotEqualUnordered<T>(IAsyncEnumerable<T>? expected, IAsyncEnumerable<T>? actual, IEqualityComparer<T>? comparer, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (expected is null || actual is null)
        {
            NotEqualUnorderedNull(expected, actual, message, actualExpression, expectedExpression);
            return;
        }

        await using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        await using var expectedSnapshot = CollectionSnapshot.Create<T>(expected);
        await actualSnapshot.EnsureCompleteAsync().ConfigureAwait(false);
        await expectedSnapshot.EnsureCompleteAsync().ConfigureAwait(false);
        if (!AreEqualUnordered(expectedSnapshot.Items, actualSnapshot.Items, comparer))
            return;

        throw new AssertionException(ErrorFormatter.Format(new NotEqualUnorderedAssertionError<IReadOnlyList<T>, IReadOnlyList<T>>("Not expected", expectedSnapshot.Items, actualSnapshot.Items, actualExpression, expectedExpression, message)));
    }

    [OverloadResolutionPriority(-1)]
    public static async Task NotEqualUnordered<TExpected, TActual>(IAsyncEnumerable<TExpected>? expected, IAsyncEnumerable<TActual>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (expected is null || actual is null)
        {
            NotEqualUnorderedNull(expected, actual, message, actualExpression, expectedExpression);
            return;
        }

        await using var actualSnapshot = CollectionSnapshot.Create<TActual>(actual);
        await using var expectedSnapshot = CollectionSnapshot.Create<TExpected>(expected);
        await actualSnapshot.EnsureCompleteAsync().ConfigureAwait(false);
        await expectedSnapshot.EnsureCompleteAsync().ConfigureAwait(false);
        if (!AreValuesEqualUnordered(expectedSnapshot.Items, actualSnapshot.Items, comparer: null))
            return;

        throw new AssertionException(ErrorFormatter.Format(new NotEqualUnorderedAssertionError<IReadOnlyList<TExpected>, IReadOnlyList<TActual>>("Not expected", expectedSnapshot.Items, actualSnapshot.Items, actualExpression, expectedExpression, message)));
    }

    public static void NotEqualUnordered(System.Collections.IEnumerable? expected, System.Collections.IEnumerable? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        NotEqualUnordered(expected, actual, comparer: null, message, actualExpression, expectedExpression);
    }

    public static void NotEqualUnordered(System.Collections.IEnumerable? expected, System.Collections.IEnumerable? actual, System.Collections.IEqualityComparer? comparer, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(expected))] string? expectedExpression = null)
    {
        if (expected is null || actual is null)
        {
            NotEqualUnorderedNull(expected, actual, message, actualExpression, expectedExpression);
            return;
        }

        using var expectedSnapshot = CollectionSnapshot.Create(expected);
        using var actualSnapshot = CollectionSnapshot.Create(actual);
        expectedSnapshot.EnsureComplete();
        actualSnapshot.EnsureComplete();
        if (!AreValuesEqualUnordered(expectedSnapshot.Items, actualSnapshot.Items, comparer))
            return;

        throw new AssertionException(ErrorFormatter.Format(new NotEqualUnorderedAssertionError<IReadOnlyList<object?>, IReadOnlyList<object?>>("Not expected", expectedSnapshot.Items, actualSnapshot.Items, actualExpression, expectedExpression, message)));
    }

    /// <summary>Succeeds when only one of the sequences is <see langword="null"/>, and fails when both are.</summary>
    private static void NotEqualUnorderedNull(object? expected, object? actual, string? message, string? actualExpression, string? expectedExpression)
    {
        if (expected is not null || actual is not null)
            return;

        throw new AssertionException(ErrorFormatter.Format(new NotEqualUnorderedAssertionError<object?, object?>("Not expected", null, null, actualExpression, expectedExpression, message)));
    }
}
