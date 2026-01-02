namespace Meziantou.Framework;

public static class AnsiUtilities
{
    public static string RemoveAnsiSequences(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Length == 0)
            return value;

        var firstEscapeIndex = value.IndexOf('\x1b', StringComparison.Ordinal);
        if (firstEscapeIndex < 0)
            return value;

        return RemoveAnsiSequencesCore(value, firstEscapeIndex);

        static string RemoveAnsiSequencesCore(string value, int startIndex)
        {
            var result = new StringBuilder(value.Length);
            result.Append(value.AsSpan(0, startIndex));

            var i = startIndex;
            while (i < value.Length)
            {
                var escapeIndex = value.IndexOf('\x1b', i);
                if (escapeIndex < 0)
                {
                    result.Append(value.AsSpan(i));
                    break;
                }

                result.Append(value.AsSpan(i, escapeIndex - i));

                if (escapeIndex + 1 < value.Length && value[escapeIndex + 1] == '[')
                {
                    i = escapeIndex + 2;
                    var foundTerminator = false;
                    while (i < value.Length)
                    {
                        var ch = value[i];
                        i++;
                        if (ch is >= (char)0x40 and <= (char)0x7E)
                        {
                            foundTerminator = true;
                            break;
                        }
                    }

                    if (!foundTerminator)
                    {
                        result.Append(value.AsSpan(escapeIndex, i - escapeIndex));
                    }
                }
                else
                {
                    result.Append('\x1b');
                    i = escapeIndex + 1;
                }
            }

            return result.ToString();
        }
    }

    public static string RemoveAnsiSequences(ReadOnlySpan<char> value)
    {
        if (value.IsEmpty)
            return "";

        var firstEscapeIndex = value.IndexOf('\x1b');
        if (firstEscapeIndex < 0)
            return value.ToString();

        return RemoveAnsiSequencesCore(value, firstEscapeIndex);

        static string RemoveAnsiSequencesCore(ReadOnlySpan<char> value, int startIndex)
        {
            var result = new StringBuilder(value.Length);
            result.Append(value[..startIndex]);

            var i = startIndex;
            while (i < value.Length)
            {
                var escapeIndex = value[i..].IndexOf('\x1b');
                if (escapeIndex < 0)
                {
                    result.Append(value[i..]);
                    break;
                }

                escapeIndex += i;
                result.Append(value[i..escapeIndex]);

                if (escapeIndex + 1 < value.Length && value[escapeIndex + 1] == '[')
                {
                    i = escapeIndex + 2;
                    var foundTerminator = false;
                    while (i < value.Length)
                    {
                        var ch = value[i];
                        i++;
                        if (ch is >= (char)0x40 and <= (char)0x7E)
                        {
                            foundTerminator = true;
                            break;
                        }
                    }

                    if (!foundTerminator)
                    {
                        result.Append(value[escapeIndex..i]);
                    }
                }
                else
                {
                    result.Append('\x1b');
                    i = escapeIndex + 1;
                }
            }

            return result.ToString();
        }
    }

    public static bool ContainsAnsiSequences(string value)
    {
        ArgumentNullException.ThrowIfNull(value);

        return ContainsAnsiSequences(value.AsSpan());
    }

    public static bool ContainsAnsiSequences(ReadOnlySpan<char> value)
    {
        var i = 0;
        while (i < value.Length)
        {
            var escapeIndex = value[i..].IndexOf('\x1b');
            if (escapeIndex < 0)
                return false;

            escapeIndex += i;
            if (escapeIndex + 1 < value.Length && value[escapeIndex + 1] == '[')
                return true;

            i = escapeIndex + 1;
        }

        return false;
    }
}
