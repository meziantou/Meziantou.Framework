using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

partial class Assert
{
    public static void NotAll<T>(ReadOnlySpan<T> actual, Action<T> assertion, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(assertion))] string? assertionExpression = null)
    {
        try
        {
            All(actual, assertion, actualExpression, assertionExpression);
        }
        catch (AssertionException)
        {
            return;
        }

        throw new AssertionException(AssertionFormatter.Default.Format(new NegativeActualValueAssertionError<object>(nameof(NotAll), "all items satisfy " + assertionExpression, MaterializeSpan(actual), actualExpression, message: null)));
    }

    public static void NotAll<T>(ReadOnlySpan<T> actual, Action<T, int> assertion, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(assertion))] string? assertionExpression = null)
    {
        try
        {
            All(actual, assertion, actualExpression, assertionExpression);
        }
        catch (AssertionException)
        {
            return;
        }

        throw new AssertionException(AssertionFormatter.Default.Format(new NegativeActualValueAssertionError<object>(nameof(NotAll), "all items satisfy " + assertionExpression, MaterializeSpan(actual), actualExpression, message: null)));
    }

    public static void NotAll<T>(IEnumerable<T> actual, Action<T> assertion, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(assertion))] string? assertionExpression = null)
    {
        SucceedWhenAssertionFails(() => All(actual, assertion, actualExpression, assertionExpression), () => new AssertionException(AssertionFormatter.Default.Format(new NegativeActualValueAssertionError<IEnumerable<T>>(nameof(NotAll), "all items satisfy " + assertionExpression, actual, actualExpression, message: null))));
    }

    public static void NotAll<T>(IEnumerable<T> actual, Action<T, int> assertion, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(assertion))] string? assertionExpression = null)
    {
        SucceedWhenAssertionFails(() => All(actual, assertion, actualExpression, assertionExpression), () => new AssertionException(AssertionFormatter.Default.Format(new NegativeActualValueAssertionError<IEnumerable<T>>(nameof(NotAll), "all items satisfy " + assertionExpression, actual, actualExpression, message: null))));
    }

    public static void NotAll(System.Collections.IEnumerable actual, Action<object?> assertion, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(assertion))] string? assertionExpression = null)
    {
        SucceedWhenAssertionFails(() => All(actual, assertion, actualExpression, assertionExpression), () => new AssertionException(AssertionFormatter.Default.Format(new NegativeActualValueAssertionError<System.Collections.IEnumerable>(nameof(NotAll), "all items satisfy " + assertionExpression, actual, actualExpression, message: null))));
    }

    public static void NotAll(System.Collections.IEnumerable actual, Action<object?, int> assertion, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(assertion))] string? assertionExpression = null)
    {
        SucceedWhenAssertionFails(() => All(actual, assertion, actualExpression, assertionExpression), () => new AssertionException(AssertionFormatter.Default.Format(new NegativeActualValueAssertionError<System.Collections.IEnumerable>(nameof(NotAll), "all items satisfy " + assertionExpression, actual, actualExpression, message: null))));
    }

    public static Task NotAll<T>(IAsyncEnumerable<T> actual, Action<T> assertion, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(assertion))] string? assertionExpression = null)
    {
        return SucceedWhenAssertionFailsAsync(() => All(actual, assertion, actualExpression, assertionExpression), () => CreateNegativeTextAssertion(nameof(NotAll), "all items satisfy " + assertionExpression, ActualExpressionText(actualExpression)));
    }

    public static Task NotAll<T>(IAsyncEnumerable<T> actual, Action<T, int> assertion, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(assertion))] string? assertionExpression = null)
    {
        return SucceedWhenAssertionFailsAsync(() => All(actual, assertion, actualExpression, assertionExpression), () => CreateNegativeTextAssertion(nameof(NotAll), "all items satisfy " + assertionExpression, ActualExpressionText(actualExpression)));
    }

    public static Task NotAll<T>(IAsyncEnumerable<T> actual, Func<T, Task> assertion, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(assertion))] string? assertionExpression = null)
    {
        return SucceedWhenAssertionFailsAsync(() => All(actual, assertion, actualExpression, assertionExpression), () => CreateNegativeTextAssertion(nameof(NotAll), "all items satisfy " + assertionExpression, ActualExpressionText(actualExpression)));
    }

    public static Task NotAll<T>(IAsyncEnumerable<T> actual, Func<T, int, Task> assertion, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null, [CallerArgumentExpression(nameof(assertion))] string? assertionExpression = null)
    {
        return SucceedWhenAssertionFailsAsync(() => All(actual, assertion, actualExpression, assertionExpression), () => CreateNegativeTextAssertion(nameof(NotAll), "all items satisfy " + assertionExpression, ActualExpressionText(actualExpression)));
    }
}
