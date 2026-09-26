using System;
using System.Diagnostics.CodeAnalysis;

namespace Tomlyn.Helpers;

internal static class ArgumentGuard
{
    public static void ThrowIfNull([NotNull] object? value, string paramName)
    {
        if (value is null)
        {
            throw new ArgumentNullException(paramName);
        }
    }
}

