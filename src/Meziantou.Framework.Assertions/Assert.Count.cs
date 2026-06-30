using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    public static void HasCount<T>(int expectedCount, ReadOnlySpan<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual.Length == expectedCount)
            return;

        throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanCountAssertionError<T>(nameof(HasCount), expectedCount.ToString(CultureInfo.InvariantCulture), actual.Length, actual, actualExpression, message)));
    }

    public static void HasCount(int expectedCount, string actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual.Length == expectedCount)
            return;

        throw new AssertionException(ErrorFormatter.Format(new StringCountAssertionError(nameof(HasCount), expectedCount.ToString(CultureInfo.InvariantCulture), actual.Length, actual, actualExpression, message)));
    }

    public static void HasCount<T>(int expectedCount, IEnumerable<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        actualSnapshot.EnsureComplete();
        if (actualSnapshot.Items.Count == expectedCount)
            return;

        throw new AssertionException(ErrorFormatter.Format(new CollectionCountAssertionError<T>(nameof(HasCount), expectedCount.ToString(CultureInfo.InvariantCulture), actualSnapshot.Items.Count, actualSnapshot, actualExpression, message)));
    }

    public static void HasCount(int expectedCount, System.Collections.IEnumerable actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        using var actualSnapshot = CollectionSnapshot.Create(actual);
        actualSnapshot.EnsureComplete();
        if (actualSnapshot.Items.Count == expectedCount)
            return;

        throw new AssertionException(ErrorFormatter.Format(new CollectionCountAssertionError<object?>(nameof(HasCount), expectedCount.ToString(CultureInfo.InvariantCulture), actualSnapshot.Items.Count, actualSnapshot, actualExpression, message)));
    }

    public static async Task HasCount<T>(int expectedCount, IAsyncEnumerable<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        await using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        await actualSnapshot.EnsureCompleteAsync().ConfigureAwait(false);
        if (actualSnapshot.Items.Count == expectedCount)
            return;

        throw new AssertionException(await ErrorFormatter.FormatAsync(new AsyncCollectionCountAssertionError<T>(nameof(HasCount), expectedCount.ToString(CultureInfo.InvariantCulture), actualSnapshot.Items.Count, actualSnapshot, actualExpression, message)).ConfigureAwait(false));
    }

    public static void HasCountGreaterThan<T>(int expectedCount, ReadOnlySpan<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual.Length > expectedCount)
            return;

        throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanCountAssertionError<T>(nameof(HasCountGreaterThan), "> " + expectedCount.ToString(CultureInfo.InvariantCulture), actual.Length, actual, actualExpression, message)));
    }

    public static void HasCountGreaterThan(int expectedCount, string actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual.Length > expectedCount)
            return;

        throw new AssertionException(ErrorFormatter.Format(new StringCountAssertionError(nameof(HasCountGreaterThan), "> " + expectedCount.ToString(CultureInfo.InvariantCulture), actual.Length, actual, actualExpression, message)));
    }

    public static void HasCountGreaterThan<T>(int expectedCount, IEnumerable<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        AssertCount(expectedCount, actual, CountComparison.GreaterThan, nameof(HasCountGreaterThan), "> " + expectedCount.ToString(CultureInfo.InvariantCulture), message, actualExpression);
    }

    public static void HasCountGreaterThan(int expectedCount, System.Collections.IEnumerable actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        AssertCount(expectedCount, EnumerateObjects(actual), CountComparison.GreaterThan, nameof(HasCountGreaterThan), "> " + expectedCount.ToString(CultureInfo.InvariantCulture), message, actualExpression);
    }

    public static async Task HasCountGreaterThan<T>(int expectedCount, IAsyncEnumerable<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        await AssertCountAsync(expectedCount, actual, CountComparison.GreaterThan, nameof(HasCountGreaterThan), "> " + expectedCount.ToString(CultureInfo.InvariantCulture), message, actualExpression).ConfigureAwait(false);
    }

    public static void HasCountGreaterThanOrEqual<T>(int expectedCount, ReadOnlySpan<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual.Length >= expectedCount)
            return;

        throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanCountAssertionError<T>(nameof(HasCountGreaterThanOrEqual), ">= " + expectedCount.ToString(CultureInfo.InvariantCulture), actual.Length, actual, actualExpression, message)));
    }

    public static void HasCountGreaterThanOrEqual(int expectedCount, string actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual.Length >= expectedCount)
            return;

        throw new AssertionException(ErrorFormatter.Format(new StringCountAssertionError(nameof(HasCountGreaterThanOrEqual), ">= " + expectedCount.ToString(CultureInfo.InvariantCulture), actual.Length, actual, actualExpression, message)));
    }

    public static void HasCountGreaterThanOrEqual<T>(int expectedCount, IEnumerable<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        AssertCount(expectedCount, actual, CountComparison.GreaterThanOrEqual, nameof(HasCountGreaterThanOrEqual), ">= " + expectedCount.ToString(CultureInfo.InvariantCulture), message, actualExpression);
    }

    public static void HasCountGreaterThanOrEqual(int expectedCount, System.Collections.IEnumerable actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        AssertCount(expectedCount, EnumerateObjects(actual), CountComparison.GreaterThanOrEqual, nameof(HasCountGreaterThanOrEqual), ">= " + expectedCount.ToString(CultureInfo.InvariantCulture), message, actualExpression);
    }

    public static async Task HasCountGreaterThanOrEqual<T>(int expectedCount, IAsyncEnumerable<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        await AssertCountAsync(expectedCount, actual, CountComparison.GreaterThanOrEqual, nameof(HasCountGreaterThanOrEqual), ">= " + expectedCount.ToString(CultureInfo.InvariantCulture), message, actualExpression).ConfigureAwait(false);
    }

    public static void HasCountLessThan<T>(int expectedCount, ReadOnlySpan<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual.Length < expectedCount)
            return;

        throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanCountAssertionError<T>(nameof(HasCountLessThan), "< " + expectedCount.ToString(CultureInfo.InvariantCulture), actual.Length, actual, actualExpression, message)));
    }

    public static void HasCountLessThan(int expectedCount, string actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual.Length < expectedCount)
            return;

        throw new AssertionException(ErrorFormatter.Format(new StringCountAssertionError(nameof(HasCountLessThan), "< " + expectedCount.ToString(CultureInfo.InvariantCulture), actual.Length, actual, actualExpression, message)));
    }

    public static void HasCountLessThan<T>(int expectedCount, IEnumerable<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        AssertCount(expectedCount, actual, CountComparison.LessThan, nameof(HasCountLessThan), "< " + expectedCount.ToString(CultureInfo.InvariantCulture), message, actualExpression);
    }

    public static void HasCountLessThan(int expectedCount, System.Collections.IEnumerable actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        AssertCount(expectedCount, EnumerateObjects(actual), CountComparison.LessThan, nameof(HasCountLessThan), "< " + expectedCount.ToString(CultureInfo.InvariantCulture), message, actualExpression);
    }

    public static async Task HasCountLessThan<T>(int expectedCount, IAsyncEnumerable<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        await AssertCountAsync(expectedCount, actual, CountComparison.LessThan, nameof(HasCountLessThan), "< " + expectedCount.ToString(CultureInfo.InvariantCulture), message, actualExpression).ConfigureAwait(false);
    }

    public static void HasCountLessThanOrEqual<T>(int expectedCount, ReadOnlySpan<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual.Length <= expectedCount)
            return;

        throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanCountAssertionError<T>(nameof(HasCountLessThanOrEqual), "<= " + expectedCount.ToString(CultureInfo.InvariantCulture), actual.Length, actual, actualExpression, message)));
    }

    public static void HasCountLessThanOrEqual(int expectedCount, string actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual.Length <= expectedCount)
            return;

        throw new AssertionException(ErrorFormatter.Format(new StringCountAssertionError(nameof(HasCountLessThanOrEqual), "<= " + expectedCount.ToString(CultureInfo.InvariantCulture), actual.Length, actual, actualExpression, message)));
    }

    public static void HasCountLessThanOrEqual<T>(int expectedCount, IEnumerable<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        AssertCount(expectedCount, actual, CountComparison.LessThanOrEqual, nameof(HasCountLessThanOrEqual), "<= " + expectedCount.ToString(CultureInfo.InvariantCulture), message, actualExpression);
    }

    public static void HasCountLessThanOrEqual(int expectedCount, System.Collections.IEnumerable actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        AssertCount(expectedCount, EnumerateObjects(actual), CountComparison.LessThanOrEqual, nameof(HasCountLessThanOrEqual), "<= " + expectedCount.ToString(CultureInfo.InvariantCulture), message, actualExpression);
    }

    public static async Task HasCountLessThanOrEqual<T>(int expectedCount, IAsyncEnumerable<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        await AssertCountAsync(expectedCount, actual, CountComparison.LessThanOrEqual, nameof(HasCountLessThanOrEqual), "<= " + expectedCount.ToString(CultureInfo.InvariantCulture), message, actualExpression).ConfigureAwait(false);
    }

    private static void AssertCount<T>(int expectedCount, IEnumerable<T> actual, CountComparison comparison, string assertionName, string expectedCountText, string? message, string? actualExpression)
    {
        using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        actualSnapshot.EnsureComplete();
        if (CompareCount(actualSnapshot.Items.Count, expectedCount, comparison))
            return;

        throw new AssertionException(ErrorFormatter.Format(new CollectionCountAssertionError<T>(assertionName, expectedCountText, actualSnapshot.Items.Count, actualSnapshot, actualExpression, message)));
    }

    private static async Task AssertCountAsync<T>(int expectedCount, IAsyncEnumerable<T> actual, CountComparison comparison, string assertionName, string expectedCountText, string? message, string? actualExpression)
    {
        await using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        await actualSnapshot.EnsureCompleteAsync().ConfigureAwait(false);
        if (CompareCount(actualSnapshot.Items.Count, expectedCount, comparison))
            return;

        throw new AssertionException(await ErrorFormatter.FormatAsync(new AsyncCollectionCountAssertionError<T>(assertionName, expectedCountText, actualSnapshot.Items.Count, actualSnapshot, actualExpression, message)).ConfigureAwait(false));
    }

    private static bool CompareCount(int actualCount, int expectedCount, CountComparison comparison)
    {
        return comparison switch
        {
            CountComparison.Equal => actualCount == expectedCount,
            CountComparison.GreaterThan => actualCount > expectedCount,
            CountComparison.GreaterThanOrEqual => actualCount >= expectedCount,
            CountComparison.LessThan => actualCount < expectedCount,
            CountComparison.LessThanOrEqual => actualCount <= expectedCount,
            _ => throw new ArgumentOutOfRangeException(nameof(comparison)),
        };
    }

    private enum CountComparison
    {
        Equal,
        GreaterThan,
        GreaterThanOrEqual,
        LessThan,
        LessThanOrEqual,
    }
}
