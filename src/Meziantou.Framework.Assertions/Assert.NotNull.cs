using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    public static object NotNull([NotNull] object? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual is not null)
            return actual;

        throw new AssertionException(ErrorFormatter.Format(new NegativeActualValueAssertionError<object?>(nameof(NotNull), "<null>", actual, actualExpression, message)));
    }

    public static T NotNull<T>([NotNull] T? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
        where T : struct
    {
        if (actual.HasValue)
            return actual.GetValueOrDefault();

        throw new AssertionException(ErrorFormatter.Format(new NegativeActualValueAssertionError<T?>(nameof(NotNull), "<null>", actual, actualExpression, message)));
    }
}
