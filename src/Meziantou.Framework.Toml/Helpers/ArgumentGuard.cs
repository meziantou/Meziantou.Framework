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
}

