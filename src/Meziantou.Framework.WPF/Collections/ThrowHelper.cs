using System.Runtime.CompilerServices;

namespace Meziantou.Framework.WPF.Collections;

internal static class ThrowHelper
{
    // Allow nulls for reference types and Nullable<U>, but not for value types.
    // Aggressively inline so the jit evaluates the if in place and either drops the call altogether
    // Or just leaves null test and call to the Non-returning ThrowHelper.ThrowArgumentNullException
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void IfNullAndNullsAreIllegalThenThrow<T>(object? value, string argName)
    {
        // Note that default(T) is not equal to null for value types except when T is Nullable<U>.
        if (!(default(T) == null) && value == null)
        {
#if NET462
            throw new ArgumentNullException(argName);
#else
            ArgumentNullException.ThrowIfNull(value, argName);
#endif
        }
    }

    [DoesNotReturn]
    public static void ThrowInvalidTypeException<T>(object? value) => throw new ArgumentException($"The value '{value}' is not of type '{typeof(T)}' and cannot be used in this generic collection.", nameof(value));
}
