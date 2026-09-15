using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    public static void DoesNotHaveCount<T>(int expectedCount, ReadOnlySpan<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual.Length != expectedCount)
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeReadOnlySpanCountAssertionError<T>(nameof(DoesNotHaveCount), expectedCount, actual.Length, actual, actualExpression, message)));
    }

    public static void DoesNotHaveCount(int expectedCount, string actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual.Length != expectedCount)
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeCountAssertionError<string>(nameof(DoesNotHaveCount), expectedCount, actual.Length, actual, actualExpression, message)));
    }

    public static void DoesNotHaveCount<T>(int expectedCount, IEnumerable<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (TryGetKnownCount(actual, out var knownCount) && knownCount != expectedCount)
            return;

        // Observing expectedCount + 1 items decides the comparison, so a longer or infinite sequence is not drained.
        // When the assertion fails, the sequence has exactly expectedCount items and is therefore complete.
        using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        if (!CountSatisfies(actualSnapshot, expectedCount, CountComparison.Equal))
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeCountAssertionError<IReadOnlyList<T>>(nameof(DoesNotHaveCount), expectedCount, actualSnapshot.Items.Count, actualSnapshot.Items, actualExpression, message)));
    }

    public static void DoesNotHaveCount(int expectedCount, System.Collections.IEnumerable actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        using var actualSnapshot = CollectionSnapshot.Create(actual);
        if (!CountSatisfies(actualSnapshot, expectedCount, CountComparison.Equal))
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeCountAssertionError<IReadOnlyList<object?>>(nameof(DoesNotHaveCount), expectedCount, actualSnapshot.Items.Count, actualSnapshot.Items, actualExpression, message)));
    }

    public static async Task DoesNotHaveCount<T>(int expectedCount, IAsyncEnumerable<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        await using var actualSnapshot = CollectionSnapshot.Create<T>(actual);
        if (!await CountSatisfiesAsync(actualSnapshot, expectedCount, CountComparison.Equal).ConfigureAwait(false))
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeExpressionAssertionError(nameof(DoesNotHaveCount), "count " + expectedCount.ToString(CultureInfo.InvariantCulture), AssertionFormatter.FormatExpression(actualExpression), message)));
    }
}
