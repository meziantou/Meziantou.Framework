using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    public static void IsNotType<T>(object? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual?.GetType() != typeof(T))
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeTypeAssertionError(nameof(IsNotType), "Not expected type", typeof(T), actual, actualExpression, message)));
    }

    public static void IsNotType(Type expectedType, object? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual?.GetType() != expectedType)
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeTypeAssertionError(nameof(IsNotType), "Not expected type", expectedType, actual, actualExpression, message)));
    }

    public static void IsNotAssignableTo<T>(object? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual is not T)
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeTypeAssertionError(nameof(IsNotAssignableTo), "Not expected assignable type", typeof(T), actual, actualExpression, message)));
    }

    public static void IsNotAssignableTo(Type expectedType, object? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual is null || !expectedType.IsAssignableFrom(actual.GetType()))
            return;

        throw new AssertionException(ErrorFormatter.Format(new NegativeTypeAssertionError(nameof(IsNotAssignableTo), "Not expected assignable type", expectedType, actual, actualExpression, message)));
    }
}
