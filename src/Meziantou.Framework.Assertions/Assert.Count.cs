using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    // Arrays would otherwise bind to the ReadOnlySpan<T> overload, which turns a null array into an empty span.
    [OverloadResolutionPriority(1)]
    public static void HasCount<T>(int expectedCount, [NotNull] T[]? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual is null)
        {
            ThrowNullCollection(nameof(HasCount), actualExpression, message, "Expected count", expectedCount.ToString(CultureInfo.InvariantCulture));
        }

        HasCount(expectedCount, new ReadOnlySpan<T>(actual), message, actualExpression);
    }

    public static void HasCount<T>(int expectedCount, ReadOnlySpan<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (CompareCount(actual.Length, expectedCount, CountComparison.Equal))
            return;

        throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanCountAssertionError<T>(nameof(HasCount), expectedCount.ToString(CultureInfo.InvariantCulture), actual.Length, actual, actualExpression, message)));
    }

    public static void HasCount(int expectedCount, [NotNull] string? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual is null)
        {
            ThrowNullCollection(nameof(HasCount), actualExpression, message, "Expected count", expectedCount.ToString(CultureInfo.InvariantCulture));
        }

        if (CompareCount(actual.Length, expectedCount, CountComparison.Equal))
            return;

        throw new AssertionException(ErrorFormatter.Format(new StringCountAssertionError(nameof(HasCount), expectedCount.ToString(CultureInfo.InvariantCulture), actual.Length, actual, actualExpression, message)));
    }

    public static void HasCount<T>(int expectedCount, [NotNull] IEnumerable<T>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        AssertCount(expectedCount, actual, CountComparison.Equal, nameof(HasCount), expectedCount.ToString(CultureInfo.InvariantCulture), message, actualExpression);
    }

    public static void HasCount(int expectedCount, [NotNull] System.Collections.IEnumerable? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        AssertCount(expectedCount, actual, CountComparison.Equal, nameof(HasCount), expectedCount.ToString(CultureInfo.InvariantCulture), message, actualExpression);
    }

    public static async Task HasCount<T>(int expectedCount, [NotNull] IAsyncEnumerable<T>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual is null)
        {
            ThrowNullCollection(nameof(HasCount), actualExpression, message, "Expected count", expectedCount.ToString(CultureInfo.InvariantCulture));
        }

        await AssertCountAsync(expectedCount, actual, CountComparison.Equal, nameof(HasCount), expectedCount.ToString(CultureInfo.InvariantCulture), message, actualExpression).ConfigureAwait(false);
    }

    // Arrays would otherwise bind to the ReadOnlySpan<T> overload, which turns a null array into an empty span.
    [OverloadResolutionPriority(1)]
    public static void HasCountGreaterThan<T>(int expectedCount, [NotNull] T[]? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual is null)
        {
            ThrowNullCollection(nameof(HasCountGreaterThan), actualExpression, message, "Expected count", "> " + expectedCount.ToString(CultureInfo.InvariantCulture));
        }

        HasCountGreaterThan(expectedCount, new ReadOnlySpan<T>(actual), message, actualExpression);
    }

    public static void HasCountGreaterThan<T>(int expectedCount, ReadOnlySpan<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (CompareCount(actual.Length, expectedCount, CountComparison.GreaterThan))
            return;

        throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanCountAssertionError<T>(nameof(HasCountGreaterThan), "> " + expectedCount.ToString(CultureInfo.InvariantCulture), actual.Length, actual, actualExpression, message)));
    }

    public static void HasCountGreaterThan(int expectedCount, [NotNull] string? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual is null)
        {
            ThrowNullCollection(nameof(HasCountGreaterThan), actualExpression, message, "Expected count", "> " + expectedCount.ToString(CultureInfo.InvariantCulture));
        }

        if (CompareCount(actual.Length, expectedCount, CountComparison.GreaterThan))
            return;

        throw new AssertionException(ErrorFormatter.Format(new StringCountAssertionError(nameof(HasCountGreaterThan), "> " + expectedCount.ToString(CultureInfo.InvariantCulture), actual.Length, actual, actualExpression, message)));
    }

    public static void HasCountGreaterThan<T>(int expectedCount, [NotNull] IEnumerable<T>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        AssertCount(expectedCount, actual, CountComparison.GreaterThan, nameof(HasCountGreaterThan), "> " + expectedCount.ToString(CultureInfo.InvariantCulture), message, actualExpression);
    }

    public static void HasCountGreaterThan(int expectedCount, [NotNull] System.Collections.IEnumerable? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        AssertCount(expectedCount, actual, CountComparison.GreaterThan, nameof(HasCountGreaterThan), "> " + expectedCount.ToString(CultureInfo.InvariantCulture), message, actualExpression);
    }

    public static async Task HasCountGreaterThan<T>(int expectedCount, [NotNull] IAsyncEnumerable<T>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual is null)
        {
            ThrowNullCollection(nameof(HasCountGreaterThan), actualExpression, message, "Expected count", "> " + expectedCount.ToString(CultureInfo.InvariantCulture));
        }

        await AssertCountAsync(expectedCount, actual, CountComparison.GreaterThan, nameof(HasCountGreaterThan), "> " + expectedCount.ToString(CultureInfo.InvariantCulture), message, actualExpression).ConfigureAwait(false);
    }

    // Arrays would otherwise bind to the ReadOnlySpan<T> overload, which turns a null array into an empty span.
    [OverloadResolutionPriority(1)]
    public static void HasCountGreaterThanOrEqual<T>(int expectedCount, [NotNull] T[]? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual is null)
        {
            ThrowNullCollection(nameof(HasCountGreaterThanOrEqual), actualExpression, message, "Expected count", ">= " + expectedCount.ToString(CultureInfo.InvariantCulture));
        }

        HasCountGreaterThanOrEqual(expectedCount, new ReadOnlySpan<T>(actual), message, actualExpression);
    }

    public static void HasCountGreaterThanOrEqual<T>(int expectedCount, ReadOnlySpan<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (CompareCount(actual.Length, expectedCount, CountComparison.GreaterThanOrEqual))
            return;

        throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanCountAssertionError<T>(nameof(HasCountGreaterThanOrEqual), ">= " + expectedCount.ToString(CultureInfo.InvariantCulture), actual.Length, actual, actualExpression, message)));
    }

    public static void HasCountGreaterThanOrEqual(int expectedCount, [NotNull] string? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual is null)
        {
            ThrowNullCollection(nameof(HasCountGreaterThanOrEqual), actualExpression, message, "Expected count", ">= " + expectedCount.ToString(CultureInfo.InvariantCulture));
        }

        if (CompareCount(actual.Length, expectedCount, CountComparison.GreaterThanOrEqual))
            return;

        throw new AssertionException(ErrorFormatter.Format(new StringCountAssertionError(nameof(HasCountGreaterThanOrEqual), ">= " + expectedCount.ToString(CultureInfo.InvariantCulture), actual.Length, actual, actualExpression, message)));
    }

    public static void HasCountGreaterThanOrEqual<T>(int expectedCount, [NotNull] IEnumerable<T>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        AssertCount(expectedCount, actual, CountComparison.GreaterThanOrEqual, nameof(HasCountGreaterThanOrEqual), ">= " + expectedCount.ToString(CultureInfo.InvariantCulture), message, actualExpression);
    }

    public static void HasCountGreaterThanOrEqual(int expectedCount, [NotNull] System.Collections.IEnumerable? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        AssertCount(expectedCount, actual, CountComparison.GreaterThanOrEqual, nameof(HasCountGreaterThanOrEqual), ">= " + expectedCount.ToString(CultureInfo.InvariantCulture), message, actualExpression);
    }

    public static async Task HasCountGreaterThanOrEqual<T>(int expectedCount, [NotNull] IAsyncEnumerable<T>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual is null)
        {
            ThrowNullCollection(nameof(HasCountGreaterThanOrEqual), actualExpression, message, "Expected count", ">= " + expectedCount.ToString(CultureInfo.InvariantCulture));
        }

        await AssertCountAsync(expectedCount, actual, CountComparison.GreaterThanOrEqual, nameof(HasCountGreaterThanOrEqual), ">= " + expectedCount.ToString(CultureInfo.InvariantCulture), message, actualExpression).ConfigureAwait(false);
    }

    // Arrays would otherwise bind to the ReadOnlySpan<T> overload, which turns a null array into an empty span.
    [OverloadResolutionPriority(1)]
    public static void HasCountLessThan<T>(int expectedCount, [NotNull] T[]? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual is null)
        {
            ThrowNullCollection(nameof(HasCountLessThan), actualExpression, message, "Expected count", "< " + expectedCount.ToString(CultureInfo.InvariantCulture));
        }

        HasCountLessThan(expectedCount, new ReadOnlySpan<T>(actual), message, actualExpression);
    }

    public static void HasCountLessThan<T>(int expectedCount, ReadOnlySpan<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (CompareCount(actual.Length, expectedCount, CountComparison.LessThan))
            return;

        throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanCountAssertionError<T>(nameof(HasCountLessThan), "< " + expectedCount.ToString(CultureInfo.InvariantCulture), actual.Length, actual, actualExpression, message)));
    }

    public static void HasCountLessThan(int expectedCount, [NotNull] string? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual is null)
        {
            ThrowNullCollection(nameof(HasCountLessThan), actualExpression, message, "Expected count", "< " + expectedCount.ToString(CultureInfo.InvariantCulture));
        }

        if (CompareCount(actual.Length, expectedCount, CountComparison.LessThan))
            return;

        throw new AssertionException(ErrorFormatter.Format(new StringCountAssertionError(nameof(HasCountLessThan), "< " + expectedCount.ToString(CultureInfo.InvariantCulture), actual.Length, actual, actualExpression, message)));
    }

    public static void HasCountLessThan<T>(int expectedCount, [NotNull] IEnumerable<T>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        AssertCount(expectedCount, actual, CountComparison.LessThan, nameof(HasCountLessThan), "< " + expectedCount.ToString(CultureInfo.InvariantCulture), message, actualExpression);
    }

    public static void HasCountLessThan(int expectedCount, [NotNull] System.Collections.IEnumerable? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        AssertCount(expectedCount, actual, CountComparison.LessThan, nameof(HasCountLessThan), "< " + expectedCount.ToString(CultureInfo.InvariantCulture), message, actualExpression);
    }

    public static async Task HasCountLessThan<T>(int expectedCount, [NotNull] IAsyncEnumerable<T>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual is null)
        {
            ThrowNullCollection(nameof(HasCountLessThan), actualExpression, message, "Expected count", "< " + expectedCount.ToString(CultureInfo.InvariantCulture));
        }

        await AssertCountAsync(expectedCount, actual, CountComparison.LessThan, nameof(HasCountLessThan), "< " + expectedCount.ToString(CultureInfo.InvariantCulture), message, actualExpression).ConfigureAwait(false);
    }

    // Arrays would otherwise bind to the ReadOnlySpan<T> overload, which turns a null array into an empty span.
    [OverloadResolutionPriority(1)]
    public static void HasCountLessThanOrEqual<T>(int expectedCount, [NotNull] T[]? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual is null)
        {
            ThrowNullCollection(nameof(HasCountLessThanOrEqual), actualExpression, message, "Expected count", "<= " + expectedCount.ToString(CultureInfo.InvariantCulture));
        }

        HasCountLessThanOrEqual(expectedCount, new ReadOnlySpan<T>(actual), message, actualExpression);
    }

    public static void HasCountLessThanOrEqual<T>(int expectedCount, ReadOnlySpan<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (CompareCount(actual.Length, expectedCount, CountComparison.LessThanOrEqual))
            return;

        throw new AssertionException(ErrorFormatter.Format(new ReadOnlySpanCountAssertionError<T>(nameof(HasCountLessThanOrEqual), "<= " + expectedCount.ToString(CultureInfo.InvariantCulture), actual.Length, actual, actualExpression, message)));
    }

    public static void HasCountLessThanOrEqual(int expectedCount, [NotNull] string? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual is null)
        {
            ThrowNullCollection(nameof(HasCountLessThanOrEqual), actualExpression, message, "Expected count", "<= " + expectedCount.ToString(CultureInfo.InvariantCulture));
        }

        if (CompareCount(actual.Length, expectedCount, CountComparison.LessThanOrEqual))
            return;

        throw new AssertionException(ErrorFormatter.Format(new StringCountAssertionError(nameof(HasCountLessThanOrEqual), "<= " + expectedCount.ToString(CultureInfo.InvariantCulture), actual.Length, actual, actualExpression, message)));
    }

    public static void HasCountLessThanOrEqual<T>(int expectedCount, [NotNull] IEnumerable<T>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        AssertCount(expectedCount, actual, CountComparison.LessThanOrEqual, nameof(HasCountLessThanOrEqual), "<= " + expectedCount.ToString(CultureInfo.InvariantCulture), message, actualExpression);
    }

    public static void HasCountLessThanOrEqual(int expectedCount, [NotNull] System.Collections.IEnumerable? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        AssertCount(expectedCount, actual, CountComparison.LessThanOrEqual, nameof(HasCountLessThanOrEqual), "<= " + expectedCount.ToString(CultureInfo.InvariantCulture), message, actualExpression);
    }

    public static async Task HasCountLessThanOrEqual<T>(int expectedCount, [NotNull] IAsyncEnumerable<T>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual is null)
        {
            ThrowNullCollection(nameof(HasCountLessThanOrEqual), actualExpression, message, "Expected count", "<= " + expectedCount.ToString(CultureInfo.InvariantCulture));
        }

        await AssertCountAsync(expectedCount, actual, CountComparison.LessThanOrEqual, nameof(HasCountLessThanOrEqual), "<= " + expectedCount.ToString(CultureInfo.InvariantCulture), message, actualExpression).ConfigureAwait(false);
    }

    private static void AssertCount<T>(int expectedCount, [NotNull] IEnumerable<T>? actual, CountComparison comparison, string assertionName, string expectedCountText, string? message, string? actualExpression)
    {
        if (actual is null)
        {
            ThrowNullCollection(assertionName, actualExpression, message, "Expected count", expectedCountText);
        }

        if (TryGetKnownCount(actual, out var knownCount) && CompareCount(knownCount, expectedCount, comparison))
            return;

        using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        if (CountSatisfies(actualSnapshot, expectedCount, comparison))
            return;

        // The sequence is not drained: the message reports the items the formatter reads, and the count as a lower
        // bound when the sequence has more items than that.
        throw new AssertionException(ErrorFormatter.Format(new CollectionCountAssertionError<T>(assertionName, expectedCountText, actualSnapshot, actualExpression, message)));
    }

    private static void AssertCount(int expectedCount, [NotNull] System.Collections.IEnumerable? actual, CountComparison comparison, string assertionName, string expectedCountText, string? message, string? actualExpression)
    {
        if (actual is null)
        {
            ThrowNullCollection(assertionName, actualExpression, message, "Expected count", expectedCountText);
        }

        if (actual is System.Collections.ICollection collection && CompareCount(collection.Count, expectedCount, comparison))
            return;

        using var actualSnapshot = CollectionSnapshot.Create(actual);
        if (CountSatisfies(actualSnapshot, expectedCount, comparison))
            return;

        throw new AssertionException(ErrorFormatter.Format(new CollectionCountAssertionError<object?>(assertionName, expectedCountText, actualSnapshot, actualExpression, message)));
    }

    private static async Task AssertCountAsync<T>(int expectedCount, IAsyncEnumerable<T> actual, CountComparison comparison, string assertionName, string expectedCountText, string? message, string? actualExpression)
    {
        await using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        if (await CountSatisfiesAsync(actualSnapshot, expectedCount, comparison).ConfigureAwait(false))
            return;

        throw new AssertionException(await ErrorFormatter.FormatAsync(new AsyncCollectionCountAssertionError<T>(assertionName, expectedCountText, actualSnapshot, actualExpression, message)).ConfigureAwait(false));
    }

    [DoesNotReturn]
    private static void ThrowNullCollection(string assertionName, string? actualExpression, string? message, string? expectedLabel = null, string? expectedText = null)
    {
        throw new AssertionException(ErrorFormatter.Format(new CollectionNullActualAssertionError(assertionName, actualExpression, expectedLabel, expectedText, message)));
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
