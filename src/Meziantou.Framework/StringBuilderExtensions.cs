using System.Diagnostics.Contracts;
using System.Text;

namespace Meziantou.Framework;

public static partial class StringBuilderExtensions
{
    [Pure]
    public static bool StartsWith(this StringBuilder stringBuilder, char prefix)
    {
        ArgumentNullException.ThrowIfNull(stringBuilder);

        if (stringBuilder.Length == 0)
            return false;

        return stringBuilder[0] == prefix;
    }

    [Pure]
    public static bool StartsWith(this StringBuilder stringBuilder, string prefix)
    {
        ArgumentNullException.ThrowIfNull(stringBuilder);
        ArgumentNullException.ThrowIfNull(prefix);

        if (stringBuilder.Length < prefix.Length)
            return false;

        for (var i = 0; i < prefix.Length; i++)
        {
            if (stringBuilder[i] != prefix[i])
                return false;
        }

        return true;
    }

    [Pure]
    public static bool EndsWith(this StringBuilder stringBuilder, char suffix)
    {
        ArgumentNullException.ThrowIfNull(stringBuilder);

        if (stringBuilder.Length == 0)
            return false;

        return stringBuilder[^1] == suffix;
    }

    [Pure]
    public static bool EndsWith(this StringBuilder stringBuilder, string suffix)
    {
        ArgumentNullException.ThrowIfNull(stringBuilder);
        ArgumentNullException.ThrowIfNull(suffix);

        if (stringBuilder.Length < suffix.Length)
            return false;

        for (var index = 0; index < suffix.Length; index++)
        {
            if (stringBuilder[stringBuilder.Length - 1 - index] != suffix[suffix.Length - 1 - index])
                return false;
        }

        return true;
    }

    public static void TrimStart(this StringBuilder stringBuilder, char trimChar)
    {
        ArgumentNullException.ThrowIfNull(stringBuilder);

        for (var i = 0; i < stringBuilder.Length; i++)
        {
            if (stringBuilder[i] == trimChar)
                continue;

            if (i > 0)
            {
                stringBuilder.Remove(0, i);
            }

            return;
        }
    }

    public static void TrimEnd(this StringBuilder stringBuilder, char trimChar)
    {
        ArgumentNullException.ThrowIfNull(stringBuilder);

        for (var i = stringBuilder.Length - 1; i >= 0; i--)
        {
            if (stringBuilder[i] == trimChar)
                continue;

            if (i != stringBuilder.Length - 1)
            {
                stringBuilder.Remove(i + 1, stringBuilder.Length - i - 1);
            }

            return;
        }
    }

    public static void Trim(this StringBuilder stringBuilder, char trimChar)
    {
        ArgumentNullException.ThrowIfNull(stringBuilder);

        TrimEnd(stringBuilder, trimChar);
        TrimStart(stringBuilder, trimChar);
    }
}
