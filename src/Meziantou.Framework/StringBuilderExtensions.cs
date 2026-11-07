namespace Meziantou.Framework;

public static partial class StringBuilderExtensions
{
    public static bool StartsWith(this StringBuilder stringBuilder, char prefix)
    {
        ArgumentNullException.ThrowIfNull(stringBuilder);

        if (stringBuilder.Length is 0)
            return false;

        return stringBuilder[0] == prefix;
    }

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

    public static bool EndsWith(this StringBuilder stringBuilder, char suffix)
    {
        ArgumentNullException.ThrowIfNull(stringBuilder);

        if (stringBuilder.Length is 0)
            return false;

        return stringBuilder[^1] == suffix;
    }

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

        var count = 0;
        while (count < stringBuilder.Length && stringBuilder[count] == trimChar)
        {
            count++;
        }

        if (count > 0)
        {
            stringBuilder.Remove(0, count);
        }
    }

    public static void TrimEnd(this StringBuilder stringBuilder, char trimChar)
    {
        ArgumentNullException.ThrowIfNull(stringBuilder);

        var length = stringBuilder.Length;
        while (length > 0 && stringBuilder[length - 1] == trimChar)
        {
            length--;
        }

        stringBuilder.Length = length;
    }

    public static void Trim(this StringBuilder stringBuilder, char trimChar)
    {
        ArgumentNullException.ThrowIfNull(stringBuilder);

        TrimEnd(stringBuilder, trimChar);
        TrimStart(stringBuilder, trimChar);
    }
}
