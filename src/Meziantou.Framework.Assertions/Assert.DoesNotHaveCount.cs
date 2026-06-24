using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

partial class Assert
{
    public static void DoesNotHaveCount<T>(int expectedCount, ReadOnlySpan<T> actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual.Length != expectedCount)
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new NegativeCountAssertionError<object>(nameof(DoesNotHaveCount), expectedCount, actual.Length, MaterializeSpan(actual), actualExpression)));
    }

    public static void DoesNotHaveCount(int expectedCount, string actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual.Length != expectedCount)
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new NegativeCountAssertionError<string>(nameof(DoesNotHaveCount), expectedCount, actual.Length, actual, actualExpression)));
    }

    public static void DoesNotHaveCount<T>(int expectedCount, IEnumerable<T> actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        SucceedWhenAssertionFails(() => HasCount(expectedCount, actual, actualExpression), () => new AssertionException(AssertionFormatter.Default.Format(new NegativeCountAssertionError<IEnumerable<T>>(nameof(DoesNotHaveCount), expectedCount, expectedCount, actual, actualExpression))));
    }

    public static void DoesNotHaveCount(int expectedCount, System.Collections.IEnumerable actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        SucceedWhenAssertionFails(() => HasCount(expectedCount, actual, actualExpression), () => new AssertionException(AssertionFormatter.Default.Format(new NegativeCountAssertionError<System.Collections.IEnumerable>(nameof(DoesNotHaveCount), expectedCount, expectedCount, actual, actualExpression))));
    }

    public static Task DoesNotHaveCount<T>(int expectedCount, IAsyncEnumerable<T> actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        return SucceedWhenAssertionFailsAsync(() => HasCount(expectedCount, actual, actualExpression), () => CreateNegativeTextAssertion(nameof(DoesNotHaveCount), "count " + expectedCount.ToString(CultureInfo.InvariantCulture), ActualExpressionText(actualExpression)));
    }
}
