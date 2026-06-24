using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

#pragma warning disable CA1720 // Assertion method name intentionally matches the established Assert.Single API shape.
partial class Assert
{
    public static void NotSingle<T>(ReadOnlySpan<T> actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual.Length != 1)
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new NegativeActualValueAssertionError<object>(nameof(NotSingle), "a single item", MaterializeSpan(actual), actualExpression, message: null)));
    }

    public static void NotSingle(ReadOnlySpan<char> actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual.Length != 1)
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new NegativeActualValueAssertionError<object>(nameof(NotSingle), "a single character", MaterializeSpan(actual), actualExpression, message: null)));
    }

    public static void NotSingle(string actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual.Length != 1)
            return;

        throw new AssertionException(AssertionFormatter.Default.Format(new NegativeActualValueAssertionError<string>(nameof(NotSingle), "a single character", actual, actualExpression, message: null)));
    }

    public static void NotSingle<T>(IEnumerable<T> actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        SucceedWhenAssertionFails(() => Single(actual, actualExpression), () => new AssertionException(AssertionFormatter.Default.Format(new NegativeActualValueAssertionError<IEnumerable<T>>(nameof(NotSingle), "a single item", actual, actualExpression, message: null))));
    }

    public static void NotSingle<T>(IEnumerable<T> actual, Func<T, bool> predicate, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(predicate))] string? predicateExpression = null)
    {
        SucceedWhenAssertionFails(() => Single(actual, predicate, actualExpression, predicateExpression), () => new AssertionException(AssertionFormatter.Default.Format(new NegativeActualValueAssertionError<IEnumerable<T>>(nameof(NotSingle), "a single matching item", actual, actualExpression, message: null))));
    }

    public static void NotSingle(System.Collections.IEnumerable actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        SucceedWhenAssertionFails(() => Single(actual, actualExpression), () => new AssertionException(AssertionFormatter.Default.Format(new NegativeActualValueAssertionError<System.Collections.IEnumerable>(nameof(NotSingle), "a single item", actual, actualExpression, message: null))));
    }

    public static Task NotSingle<T>(IAsyncEnumerable<T> actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        return SucceedWhenAssertionFailsAsync(() => Single(actual, actualExpression), () => CreateNegativeTextAssertion(nameof(NotSingle), "a single item", ActualExpressionText(actualExpression)));
    }
}
#pragma warning restore CA1720
