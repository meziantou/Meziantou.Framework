using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    // Arrays would otherwise bind to the ReadOnlySpan<T> overload, which turns a null array into an empty span.
    [OverloadResolutionPriority(1)]
    public static void DoesNotHaveCount<T>(int expectedCount, [NotNull] T[]? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual is null)
        {
            ThrowNullCollection(nameof(DoesNotHaveCount), actualExpression, message, "Not expected count", expectedCount.ToString(CultureInfo.InvariantCulture));
        }

        DoesNotHaveCount(expectedCount, new ReadOnlySpan<T>(actual), message, actualExpression);
    }

    public static void DoesNotHaveCount<T>(int expectedCount, ReadOnlySpan<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual.Length != expectedCount)
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeReadOnlySpanCountAssertionError<T>(nameof(DoesNotHaveCount), expectedCount, actual.Length, actual, actualExpression, message)));
    }

    public static void DoesNotHaveCount(int expectedCount, [NotNull] string? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual is null)
        {
            ThrowNullCollection(nameof(DoesNotHaveCount), actualExpression, message, "Not expected count", expectedCount.ToString(CultureInfo.InvariantCulture));
        }

        if (actual.Length != expectedCount)
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeCountAssertionError<string>(nameof(DoesNotHaveCount), expectedCount, actual.Length, actual, actualExpression, message)));
    }

    public static void DoesNotHaveCount<T>(int expectedCount, [NotNull] IEnumerable<T>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual is null)
        {
            ThrowNullCollection(nameof(DoesNotHaveCount), actualExpression, message, "Not expected count", expectedCount.ToString(CultureInfo.InvariantCulture));
        }

        if (TryGetKnownCount(actual, out var knownCount) && knownCount != expectedCount)
            return;

        // Observing expectedCount + 1 items decides the comparison, so a longer or infinite sequence is not drained.
        // When the assertion fails, the sequence has exactly expectedCount items and is therefore complete.
        using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        if (!CountSatisfies(actualSnapshot, expectedCount, CountComparison.Equal))
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeCountAssertionError<IReadOnlyList<T>>(nameof(DoesNotHaveCount), expectedCount, actualSnapshot.Items.Count, actualSnapshot.Items, actualExpression, message)));
    }

    public static void DoesNotHaveCount(int expectedCount, [NotNull] System.Collections.IEnumerable? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual is null)
        {
            ThrowNullCollection(nameof(DoesNotHaveCount), actualExpression, message, "Not expected count", expectedCount.ToString(CultureInfo.InvariantCulture));
        }

        if (actual is System.Collections.ICollection collection && collection.Count != expectedCount)
            return;

        using var actualSnapshot = CollectionSnapshot.Create(actual);
        if (!CountSatisfies(actualSnapshot, expectedCount, CountComparison.Equal))
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeCountAssertionError<IReadOnlyList<object?>>(nameof(DoesNotHaveCount), expectedCount, actualSnapshot.Items.Count, actualSnapshot.Items, actualExpression, message)));
    }

    public static async Task DoesNotHaveCount<T>(int expectedCount, [NotNull] IAsyncEnumerable<T>? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual is null)
        {
            ThrowNullCollection(nameof(DoesNotHaveCount), actualExpression, message, "Not expected count", expectedCount.ToString(CultureInfo.InvariantCulture));
        }

        await using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        if (!await CountSatisfiesAsync(actualSnapshot, expectedCount, CountComparison.Equal).ConfigureAwait(false))
            return;

        // The sequence has exactly expectedCount items, all observed, so it can be reported like a synchronous one.
        throw new AssertionException(ErrorFormatter.Format(new NegativeCountAssertionError<IReadOnlyList<T>>(nameof(DoesNotHaveCount), expectedCount, actualSnapshot.Items.Count, actualSnapshot.Items, actualExpression, message)));
    }
}
