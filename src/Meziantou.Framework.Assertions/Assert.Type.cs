using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    /// <summary>Asserts that an object is exactly of the specified type.</summary>
    /// <param name="actual">The value to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <typeparam name="T">The expected type.</typeparam>
    /// <returns>The value cast to <typeparamref name="T"/>.</returns>
    public static T IsType<T>(object? actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual?.GetType() == typeof(T))
            return (T)actual;

        throw new AssertionException(ErrorFormatter.Format(new IsTypeAssertionError(typeof(T), actual, actualExpression)));
    }

    /// <summary>Asserts that an object is exactly of the specified type.</summary>
    /// <param name="expectedType">The expected type.</param>
    /// <param name="actual">The value to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <returns>The value.</returns>
    public static object IsType(Type expectedType, object? actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual?.GetType() == expectedType)
            return actual;

        throw new AssertionException(ErrorFormatter.Format(new IsTypeAssertionError(expectedType, actual, actualExpression)));
    }

    /// <summary>Asserts that an object can be assigned to the specified type.</summary>
    /// <param name="actual">The value to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <typeparam name="T">The expected assignable type.</typeparam>
    /// <returns>The value cast to <typeparamref name="T"/>.</returns>
    public static T IsAssignableTo<T>(object? actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual is T value)
            return value;

        throw new AssertionException(ErrorFormatter.Format(new IsAssignableToAssertionError(typeof(T), actual, actualExpression)));
    }

    /// <summary>Asserts that an object can be assigned to the specified type.</summary>
    /// <param name="expectedType">The expected assignable type.</param>
    /// <param name="actual">The value to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <returns>The value.</returns>
    public static object IsAssignableTo(Type expectedType, object? actual, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual is not null && expectedType.IsAssignableFrom(actual.GetType()))
            return actual;

        throw new AssertionException(ErrorFormatter.Format(new IsAssignableToAssertionError(expectedType, actual, actualExpression)));
    }
}
