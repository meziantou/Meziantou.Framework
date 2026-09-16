using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    /// <summary>Asserts that an object is exactly of the specified type.</summary>
    /// <param name="actual">The value to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <typeparam name="T">The expected type.</typeparam>
    /// <returns>The value cast to <typeparamref name="T"/>.</returns>
    public static T IsType<T>([NotNull] object? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (IsExactType(typeof(T), actual))
            return (T)actual;

        throw new AssertionException(ErrorFormatter.Format(new IsTypeAssertionError(typeof(T), actual, actualExpression, message)));
    }

    /// <summary>Asserts that an object is exactly of the specified type.</summary>
    /// <param name="expectedType">The expected type.</param>
    /// <param name="actual">The value to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <returns>The value.</returns>
    public static object IsType(Type expectedType, [NotNull] object? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (IsExactType(expectedType, actual))
            return actual;

        throw new AssertionException(ErrorFormatter.Format(new IsTypeAssertionError(expectedType, actual, actualExpression, message)));
    }

    // Boxing a Nullable<T> produces either null or a boxed T, so no object has a Nullable<T> runtime type.
    // A boxed T is the only value a Nullable<T> can hold, so it is considered to be exactly of type Nullable<T>.
    private static bool IsExactType(Type expectedType, [NotNullWhen(true)] object? actual)
    {
        if (actual is null)
            return false;

        var actualType = actual.GetType();
        return actualType == expectedType || actualType == Nullable.GetUnderlyingType(expectedType);
    }

    /// <summary>Asserts that an object can be assigned to the specified type.</summary>
    /// <param name="actual">The value to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <typeparam name="T">The expected assignable type.</typeparam>
    /// <returns>The value cast to <typeparamref name="T"/>.</returns>
    public static T IsAssignableTo<T>([NotNull] object? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        if (actual is T value)
            return value;

        throw new AssertionException(ErrorFormatter.Format(new IsAssignableToAssertionError(typeof(T), actual, actualExpression, message)));
    }

    /// <summary>Asserts that an object can be assigned to the specified type.</summary>
    /// <param name="expectedType">The expected assignable type.</param>
    /// <param name="actual">The value to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <returns>The value.</returns>
    public static object IsAssignableTo(Type expectedType, [NotNull] object? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
    {
        // IsInstanceOfType matches "actual is T", which also considers COM objects and IDynamicInterfaceCastable
        if (expectedType.IsInstanceOfType(actual))
            return actual;

        throw new AssertionException(ErrorFormatter.Format(new IsAssignableToAssertionError(expectedType, actual, actualExpression, message)));
    }
}
