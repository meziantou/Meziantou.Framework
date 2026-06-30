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
        using var actualSnapshot = new CollectionSnapshot<T>(actual);
        EnsureComplete(actualSnapshot);
        if (actualSnapshot.Items.Count != expectedCount)
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeCountAssertionError<IEnumerable<T>>(nameof(DoesNotHaveCount), expectedCount, actualSnapshot.Items.Count, actual, actualExpression, message)));
    }

    public static void DoesNotHaveCount(int expectedCount, System.Collections.IEnumerable actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        using var actualSnapshot = new CollectionSnapshot<object?>(EnumerateObjects(actual));
        EnsureComplete(actualSnapshot);
        if (actualSnapshot.Items.Count != expectedCount)
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeCountAssertionError<System.Collections.IEnumerable>(nameof(DoesNotHaveCount), expectedCount, actualSnapshot.Items.Count, actual, actualExpression, message)));
    }

    public static async Task DoesNotHaveCount<T>(int expectedCount, IAsyncEnumerable<T> actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        await using var actualSnapshot = new AsyncCollectionSnapshot<T>(actual);
        await EnsureCompleteAsync(actualSnapshot).ConfigureAwait(false);
        if (actualSnapshot.Items.Count != expectedCount)
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeExpressionAssertionError(nameof(DoesNotHaveCount), "count " + expectedCount.ToString(CultureInfo.InvariantCulture), AssertionFormatter.FormatExpression(actualExpression), message)));
    }
}
