using System;
using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.Toml.Helpers;

internal static class ArgumentGuard
{
    public static void ThrowIfNull([NotNull] object? value, string paramName)
    {
        if (value is null)
        {
            throw new ArgumentNullException(paramName);
        }
    }

    // An undefined value would be treated like one of the defined values, depending on how each consumer compares it
    public static T ThrowIfNotDefined<T>(T value, string paramName)
        where T : struct, Enum
    {
        if (!Enum.IsDefined(value))
        {
            throw new ArgumentOutOfRangeException(paramName, value, $"The value is not a defined {typeof(T).Name}.");
        }

        return value;
    }
}

