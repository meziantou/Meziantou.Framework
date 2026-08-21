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
        AssertCount(expectedCount, actual, CountComparison.Equal, nameof(HasCount), expectedCount.ToString(CultureInfo.InvariantCulture), message, actualExpression);
    }

    public static void HasCount(int expectedCount, System.Collections.IEnumerable actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        using var actualSnapshot = CollectionSnapshot.Create(actual);
        if (CountSatisfies(actualSnapshot, expectedCount, CountComparison.Equal))
            return;

        actualSnapshot.EnsureComplete();
        throw new AssertionException(ErrorFormatter.Format(new CollectionCountAssertionError<object?>(nameof(HasCount), expectedCount.ToString(CultureInfo.InvariantCulture), actualSnapshot.Items.Count, actualSnapshot, actualExpression, message)));
    }

    public static Task HasCount<T>(int expectedCount, IAsyncEnumerable<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        return AssertCountAsync(expectedCount, actual, CountComparison.Equal, nameof(HasCount), expectedCount.ToString(CultureInfo.InvariantCulture), message, actualExpression);
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
        if (TryGetKnownCount(actual, out var knownCount) && CompareCount(knownCount, expectedCount, comparison))
            return;

        using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        if (CountSatisfies(actualSnapshot, expectedCount, comparison))
            return;

        actualSnapshot.EnsureComplete();
        throw new AssertionException(ErrorFormatter.Format(new CollectionCountAssertionError<T>(assertionName, expectedCountText, actualSnapshot.Items.Count, actualSnapshot, actualExpression, message)));
    }

    private static async Task AssertCountAsync<T>(int expectedCount, IAsyncEnumerable<T> actual, CountComparison comparison, string assertionName, string expectedCountText, string? message, string? actualExpression)
    {
        await using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        if (await CountSatisfiesAsync(actualSnapshot, expectedCount, comparison).ConfigureAwait(false))
            return;

        await actualSnapshot.EnsureCompleteAsync().ConfigureAwait(false);
        throw new AssertionException(await ErrorFormatter.FormatAsync(new AsyncCollectionCountAssertionError<T>(assertionName, expectedCountText, actualSnapshot.Items.Count, actualSnapshot, actualExpression, message)).ConfigureAwait(false));
    }

    /// <summary>Gets the number of items in <paramref name="source"/> when it is known without enumerating.</summary>
    private static bool TryGetKnownCount<T>(IEnumerable<T> source, out int count)
    {
        // Covers ICollection<T>, the non-generic ICollection and several LINQ operators.
        if (Enumerable.TryGetNonEnumeratedCount(source, out count))
            return true;

        if (source is IReadOnlyCollection<T> readOnlyCollection)
        {
            count = readOnlyCollection.Count;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Evaluates the comparison by observing at most <paramref name="expectedCount"/> + 1 items, so a sequence is
    /// never enumerated further than the answer requires.
    /// </summary>
    private static bool CountSatisfies<T>(CollectionSnapshot<T> snapshot, int expectedCount, CountComparison comparison)
    {
        return comparison switch
        {
            CountComparison.Equal => HasAtLeast(snapshot, expectedCount) && !HasAtLeast(snapshot, expectedCount + 1L),
            CountComparison.GreaterThan => HasAtLeast(snapshot, expectedCount + 1L),
            CountComparison.GreaterThanOrEqual => HasAtLeast(snapshot, expectedCount),
            CountComparison.LessThan => !HasAtLeast(snapshot, expectedCount),
            CountComparison.LessThanOrEqual => !HasAtLeast(snapshot, expectedCount + 1L),
            _ => throw new ArgumentOutOfRangeException(nameof(comparison)),
        };

        static bool HasAtLeast(CollectionSnapshot<T> snapshot, long count)
        {
            if (count <= 0)
                return true;

            if (count > int.MaxValue)
                return false;

            return snapshot.TryGetItem((int)(count - 1), out _);
        }
    }

    private static async Task<bool> CountSatisfiesAsync<T>(AsyncCollectionSnapshot<T> snapshot, int expectedCount, CountComparison comparison)
    {
        return comparison switch
        {
            CountComparison.Equal => await HasAtLeastAsync(snapshot, expectedCount).ConfigureAwait(false) && !await HasAtLeastAsync(snapshot, expectedCount + 1L).ConfigureAwait(false),
            CountComparison.GreaterThan => await HasAtLeastAsync(snapshot, expectedCount + 1L).ConfigureAwait(false),
            CountComparison.GreaterThanOrEqual => await HasAtLeastAsync(snapshot, expectedCount).ConfigureAwait(false),
            CountComparison.LessThan => !await HasAtLeastAsync(snapshot, expectedCount).ConfigureAwait(false),
            CountComparison.LessThanOrEqual => !await HasAtLeastAsync(snapshot, expectedCount + 1L).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(comparison)),
        };

        static async Task<bool> HasAtLeastAsync(AsyncCollectionSnapshot<T> snapshot, long count)
        {
            if (count <= 0)
                return true;

            if (count > int.MaxValue)
                return false;

            return await snapshot.TryGetItem((int)(count - 1)).ConfigureAwait(false) is (true, _);
        }
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
