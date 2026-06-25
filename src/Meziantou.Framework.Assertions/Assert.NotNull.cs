using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

partial class Assert
{
    public static object NotNull([NotNull] object? actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual is not null)
            return actual;

        return ThrowFailure<object>(new AssertionException(AssertionFormatter.Default.Format(new NegativeActualValueAssertionError<object?>(nameof(NotNull), "<null>", actual, actualExpression, message: null))));
    }

    public static T NotNull<T>([NotNull] T? actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
        where T : struct
    {
        if (actual.HasValue)
            return actual.GetValueOrDefault();

        return ThrowFailure<T>(new AssertionException(AssertionFormatter.Default.Format(new NegativeActualValueAssertionError<T?>(nameof(NotNull), "<null>", actual, actualExpression, message: null))));
    }
}
