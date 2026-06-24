using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.Assertions;

partial class Assert
{
    [DoesNotReturn]
    private static T ThrowFailure<T>(AssertionException exception)
    {
        throw exception;
    }

    private static void SucceedWhenAssertionFails(Action assertion, Func<AssertionException> failureFactory)
    {
        try
        {
            assertion();
        }
        catch (AssertionException)
        {
            return;
        }

        throw failureFactory();
    }

    private static async Task SucceedWhenAssertionFailsAsync(Func<Task> assertion, Func<AssertionException> failureFactory)
    {
        try
        {
            await assertion().ConfigureAwait(false);
        }
        catch (AssertionException)
        {
            return;
        }

        throw failureFactory();
    }

    private static AssertionException CreateNegativeTextAssertion(string assertionName, string notExpectedText, string actualText, string? message = null)
    {
        return new AssertionException(AssertionFormatter.Default.Format(new NegativeTextAssertionError(assertionName, notExpectedText, actualText, message)));
    }

    private static string ActualExpressionText(string? actualExpression)
    {
        return string.IsNullOrEmpty(actualExpression) ? "<actual>" : actualExpression;
    }

    private static object MaterializeSpan<T>(ReadOnlySpan<T> value)
    {
        if (typeof(T) == typeof(char))
        {
            return value.ToString();
        }

        return value.ToArray();
    }
}
