using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Assertions;

public partial class Assert
{
    /// <summary>Asserts that a reference is not null.</summary>
    /// <param name="actual">The value to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <returns>The value, typed as non-nullable.</returns>
    public static T NotNull<T>([NotNull] T? actual, string? message = null, [CallerArgumentExpression(nameof(actual))] string? actualExpression = null)
        where T : class
    {
        if (actual is not null)
            return actual;

        throw new AssertionException(ErrorFormatter.Format(new NegativeActualValueAssertionError<object?>(nameof(NotNull), "<null>", actual, actualExpression, message)));
    }

    /// <summary>Asserts that a value is not null.</summary>
    /// <param name="actual">The value to inspect.</param>
    /// <param name="actualExpression">The expression that produced the actual value.</param>
    /// <returns>The value.</returns>
    /// <remarks>
    /// This overload only binds when neither generic overload applies, such as for a value of an unconstrained type parameter or the <see langword="null"/> literal.
    /// </remarks>
    [OverloadResolutionPriority(-1)]
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
