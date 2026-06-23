using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

partial class Assert
{
    public static void HasCount<T>(int expectedCount, ReadOnlySpan<T> actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual.Length == expectedCount)
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new ReadOnlySpanCountAssertionError<T>(nameof(HasCount), expectedCount.ToString(CultureInfo.InvariantCulture), actual.Length, actual, actualExpression)));
    }

    public static void HasCount(int expectedCount, string actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual.Length == expectedCount)
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new StringCountAssertionError(nameof(HasCount), expectedCount.ToString(CultureInfo.InvariantCulture), actual.Length, actual, actualExpression)));
    }

    public static void HasCount<T>(int expectedCount, IEnumerable<T> actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        using var actualSnapshot = new CollectionSnapshot<T>(actual);
        EnsureComplete(actualSnapshot);
        if (actualSnapshot.Items.Count == expectedCount)
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new CollectionCountAssertionError<T>(nameof(HasCount), expectedCount.ToString(CultureInfo.InvariantCulture), actualSnapshot.Items.Count, actualSnapshot, actualExpression)));
    }

    public static void HasCount(int expectedCount, System.Collections.IEnumerable actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        using var actualSnapshot = new CollectionSnapshot<object?>(EnumerateObjects(actual));
        EnsureComplete(actualSnapshot);
        if (actualSnapshot.Items.Count == expectedCount)
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new CollectionCountAssertionError<object?>(nameof(HasCount), expectedCount.ToString(CultureInfo.InvariantCulture), actualSnapshot.Items.Count, actualSnapshot, actualExpression)));
    }

    public static async Task HasCount<T>(int expectedCount, IAsyncEnumerable<T> actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        await using var actualSnapshot = new AsyncCollectionSnapshot<T>(actual);
        await EnsureCompleteAsync(actualSnapshot).ConfigureAwait(false);
        if (actualSnapshot.Items.Count == expectedCount)
            return;

        throw new AssertionException(await AssertionFormatter.Default.FormatAsync(new AsyncCollectionCountAssertionError<T>(nameof(HasCount), expectedCount.ToString(CultureInfo.InvariantCulture), actualSnapshot.Items.Count, actualSnapshot, actualExpression)).ConfigureAwait(false));
    }

    public static void HasCountGreaterThan<T>(int expectedCount, ReadOnlySpan<T> actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual.Length > expectedCount)
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new ReadOnlySpanCountAssertionError<T>(nameof(HasCountGreaterThan), "> " + expectedCount.ToString(CultureInfo.InvariantCulture), actual.Length, actual, actualExpression)));
    }

    public static void HasCountGreaterThan(int expectedCount, string actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual.Length > expectedCount)
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new StringCountAssertionError(nameof(HasCountGreaterThan), "> " + expectedCount.ToString(CultureInfo.InvariantCulture), actual.Length, actual, actualExpression)));
    }

    public static void HasCountGreaterThan<T>(int expectedCount, IEnumerable<T> actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        AssertCount(expectedCount, actual, CountComparison.GreaterThan, nameof(HasCountGreaterThan), "> " + expectedCount.ToString(CultureInfo.InvariantCulture), actualExpression);
    }

    public static void HasCountGreaterThan(int expectedCount, System.Collections.IEnumerable actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        AssertCount(expectedCount, EnumerateObjects(actual), CountComparison.GreaterThan, nameof(HasCountGreaterThan), "> " + expectedCount.ToString(CultureInfo.InvariantCulture), actualExpression);
    }

    public static Task HasCountGreaterThan<T>(int expectedCount, IAsyncEnumerable<T> actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        return AssertCountAsync(expectedCount, actual, CountComparison.GreaterThan, nameof(HasCountGreaterThan), "> " + expectedCount.ToString(CultureInfo.InvariantCulture), actualExpression);
    }

    public static void HasCountGreaterThanOrEqual<T>(int expectedCount, ReadOnlySpan<T> actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual.Length >= expectedCount)
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new ReadOnlySpanCountAssertionError<T>(nameof(HasCountGreaterThanOrEqual), ">= " + expectedCount.ToString(CultureInfo.InvariantCulture), actual.Length, actual, actualExpression)));
    }

    public static void HasCountGreaterThanOrEqual(int expectedCount, string actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual.Length >= expectedCount)
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new StringCountAssertionError(nameof(HasCountGreaterThanOrEqual), ">= " + expectedCount.ToString(CultureInfo.InvariantCulture), actual.Length, actual, actualExpression)));
    }

    public static void HasCountGreaterThanOrEqual<T>(int expectedCount, IEnumerable<T> actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        AssertCount(expectedCount, actual, CountComparison.GreaterThanOrEqual, nameof(HasCountGreaterThanOrEqual), ">= " + expectedCount.ToString(CultureInfo.InvariantCulture), actualExpression);
    }

    public static void HasCountGreaterThanOrEqual(int expectedCount, System.Collections.IEnumerable actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        AssertCount(expectedCount, EnumerateObjects(actual), CountComparison.GreaterThanOrEqual, nameof(HasCountGreaterThanOrEqual), ">= " + expectedCount.ToString(CultureInfo.InvariantCulture), actualExpression);
    }

    public static Task HasCountGreaterThanOrEqual<T>(int expectedCount, IAsyncEnumerable<T> actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        return AssertCountAsync(expectedCount, actual, CountComparison.GreaterThanOrEqual, nameof(HasCountGreaterThanOrEqual), ">= " + expectedCount.ToString(CultureInfo.InvariantCulture), actualExpression);
    }

    public static void HasCountLessThan<T>(int expectedCount, ReadOnlySpan<T> actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual.Length < expectedCount)
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new ReadOnlySpanCountAssertionError<T>(nameof(HasCountLessThan), "< " + expectedCount.ToString(CultureInfo.InvariantCulture), actual.Length, actual, actualExpression)));
    }

    public static void HasCountLessThan(int expectedCount, string actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual.Length < expectedCount)
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new StringCountAssertionError(nameof(HasCountLessThan), "< " + expectedCount.ToString(CultureInfo.InvariantCulture), actual.Length, actual, actualExpression)));
    }

    public static void HasCountLessThan<T>(int expectedCount, IEnumerable<T> actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        AssertCount(expectedCount, actual, CountComparison.LessThan, nameof(HasCountLessThan), "< " + expectedCount.ToString(CultureInfo.InvariantCulture), actualExpression);
    }

    public static void HasCountLessThan(int expectedCount, System.Collections.IEnumerable actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        AssertCount(expectedCount, EnumerateObjects(actual), CountComparison.LessThan, nameof(HasCountLessThan), "< " + expectedCount.ToString(CultureInfo.InvariantCulture), actualExpression);
    }

    public static Task HasCountLessThan<T>(int expectedCount, IAsyncEnumerable<T> actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        return AssertCountAsync(expectedCount, actual, CountComparison.LessThan, nameof(HasCountLessThan), "< " + expectedCount.ToString(CultureInfo.InvariantCulture), actualExpression);
    }

    public static void HasCountLessThanOrEqual<T>(int expectedCount, ReadOnlySpan<T> actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual.Length <= expectedCount)
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new ReadOnlySpanCountAssertionError<T>(nameof(HasCountLessThanOrEqual), "<= " + expectedCount.ToString(CultureInfo.InvariantCulture), actual.Length, actual, actualExpression)));
    }

    public static void HasCountLessThanOrEqual(int expectedCount, string actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual.Length <= expectedCount)
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new StringCountAssertionError(nameof(HasCountLessThanOrEqual), "<= " + expectedCount.ToString(CultureInfo.InvariantCulture), actual.Length, actual, actualExpression)));
    }

    public static void HasCountLessThanOrEqual<T>(int expectedCount, IEnumerable<T> actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        AssertCount(expectedCount, actual, CountComparison.LessThanOrEqual, nameof(HasCountLessThanOrEqual), "<= " + expectedCount.ToString(CultureInfo.InvariantCulture), actualExpression);
    }

    public static void HasCountLessThanOrEqual(int expectedCount, System.Collections.IEnumerable actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        AssertCount(expectedCount, EnumerateObjects(actual), CountComparison.LessThanOrEqual, nameof(HasCountLessThanOrEqual), "<= " + expectedCount.ToString(CultureInfo.InvariantCulture), actualExpression);
    }

    public static Task HasCountLessThanOrEqual<T>(int expectedCount, IAsyncEnumerable<T> actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        return AssertCountAsync(expectedCount, actual, CountComparison.LessThanOrEqual, nameof(HasCountLessThanOrEqual), "<= " + expectedCount.ToString(CultureInfo.InvariantCulture), actualExpression);
    }

    private static void AssertCount<T>(int expectedCount, IEnumerable<T> actual, CountComparison comparison, string assertionName, string expectedCountText, string? actualExpression)
    {
        using var actualSnapshot = new CollectionSnapshot<T>(actual);
        EnsureComplete(actualSnapshot);
        if (CompareCount(actualSnapshot.Items.Count, expectedCount, comparison))
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new CollectionCountAssertionError<T>(assertionName, expectedCountText, actualSnapshot.Items.Count, actualSnapshot, actualExpression)));
    }

    private static async Task AssertCountAsync<T>(int expectedCount, IAsyncEnumerable<T> actual, CountComparison comparison, string assertionName, string expectedCountText, string? actualExpression)
    {
        await using var actualSnapshot = new AsyncCollectionSnapshot<T>(actual);
        await EnsureCompleteAsync(actualSnapshot).ConfigureAwait(false);
        if (CompareCount(actualSnapshot.Items.Count, expectedCount, comparison))
            return;

        throw new AssertionException(await AssertionFormatter.Default.FormatAsync(new AsyncCollectionCountAssertionError<T>(assertionName, expectedCountText, actualSnapshot.Items.Count, actualSnapshot, actualExpression)).ConfigureAwait(false));
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
