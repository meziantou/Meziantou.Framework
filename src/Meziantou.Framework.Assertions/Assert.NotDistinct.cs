using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

partial class Assert
{
    public static void NotDistinct<T>(ReadOnlySpan<T> actual, IEqualityComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        try
        {
            Distinct(actual, comparer, actualExpression);
        }
        catch (AssertionException)
        {
            return;
        }

        throw new AssertionException(AssertionFormatter.Default.Format(new NegativeActualValueAssertionError<object>(nameof(NotDistinct), "all distinct items", MaterializeSpan(actual), actualExpression, message: null)));
    }

    public static void NotDistinct(string actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        SucceedWhenAssertionFails(() => Distinct(actual, actualExpression), () => new AssertionException(AssertionFormatter.Default.Format(new NegativeActualValueAssertionError<string>(nameof(NotDistinct), "all distinct characters", actual, actualExpression, message: null))));
    }

    public static void NotDistinct<T>(IEnumerable<T> actual, IEqualityComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        SucceedWhenAssertionFails(() => Distinct(actual, comparer, actualExpression), () => new AssertionException(AssertionFormatter.Default.Format(new NegativeActualValueAssertionError<IEnumerable<T>>(nameof(NotDistinct), "all distinct items", actual, actualExpression, message: null))));
    }

    public static void NotDistinct(System.Collections.IEnumerable actual, System.Collections.IEqualityComparer? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        SucceedWhenAssertionFails(() => Distinct(actual, comparer, actualExpression), () => new AssertionException(AssertionFormatter.Default.Format(new NegativeActualValueAssertionError<System.Collections.IEnumerable>(nameof(NotDistinct), "all distinct items", actual, actualExpression, message: null))));
    }

    public static Task NotDistinct<T>(IAsyncEnumerable<T> actual, IEqualityComparer<T>? comparer = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        return SucceedWhenAssertionFailsAsync(() => Distinct(actual, comparer, actualExpression), () => CreateNegativeTextAssertion(nameof(NotDistinct), "all distinct items", ActualExpressionText(actualExpression)));
    }
}
