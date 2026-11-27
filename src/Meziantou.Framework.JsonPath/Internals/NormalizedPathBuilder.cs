using System.Buffers;

namespace Meziantou.Framework.Json.Internals;

/// <summary>
/// Builds normalized paths per RFC 9535 §2.7.
/// Format: $['name'][index]
/// </summary>
internal static class NormalizedPathBuilder
{
    /// <summary>The characters of a member name that a normalized path escapes: control characters, the quote and the backslash.</summary>
    private static readonly SearchValues<char> CharactersToEscape = SearchValues.Create(
        "\u0000\u0001\u0002\u0003\u0004\u0005\u0006\u0007\u0008\u0009\u000A\u000B\u000C\u000D\u000E\u000F" +
        "\u0010\u0011\u0012\u0013\u0014\u0015\u0016\u0017\u0018\u0019\u001A\u001B\u001C\u001D\u001E\u001F'\\");

    public static string Build(PathNode? path)
    {
        if (path is null)
        {
            return "$";
        }

        // The steps are linked from the last one back to the root, so the length is computed first and the string is
        // written from its end
        var length = 1;
        for (var step = path; step is not null; step = step.Parent)
        {
            length += step.Name is null ? CountDigits(step.Index) + 2 : GetEscapedLength(step.Name) + 4;
        }

        return string.Create(length, path, static (destination, path) =>
        {
            var position = destination.Length;
            for (var step = path; step is not null; step = step.Parent)
            {
                if (step.Name is null)
                {
                    destination[--position] = ']';
                    var digits = CountDigits(step.Index);
                    position -= digits;
                    step.Index.TryFormat(destination.Slice(position, digits), out _, provider: CultureInfo.InvariantCulture);
                    destination[--position] = '[';
                }
                else
                {
                    destination[--position] = ']';
                    destination[--position] = '\'';
                    var escapedLength = GetEscapedLength(step.Name);
                    position -= escapedLength;
                    WriteEscapedName(destination.Slice(position, escapedLength), step.Name);
                    destination[--position] = '\'';
                    destination[--position] = '[';
                }
            }

            destination[--position] = '$';
        });
    }

    /// <summary>Gets the number of characters of an index, which is never negative in a normalized path.</summary>
    private static int CountDigits(long value)
    {
        var digits = 1;
        while (value >= 10)
        {
            value /= 10;
            digits++;
        }

        return digits;
    }

    private static int GetEscapedLength(string name)
    {
        var index = name.AsSpan().IndexOfAny(CharactersToEscape);
        if (index < 0)
            return name.Length;

        var length = index;
        foreach (var ch in name.AsSpan(index))
        {
            length += ch switch
            {
                '\'' or '\\' or '\b' or '\f' or '\n' or '\r' or '\t' => 2,
                < '\x20' => 6,
                _ => 1,
            };
        }

        return length;
    }

    private static void WriteEscapedName(Span<char> destination, string name)
    {
        var index = name.AsSpan().IndexOfAny(CharactersToEscape);
        if (index < 0)
        {
            name.CopyTo(destination);
            return;
        }

        name.AsSpan(0, index).CopyTo(destination);
        var position = index;
        foreach (var ch in name.AsSpan(index))
        {
            switch (ch)
            {
                case '\'':
                    destination[position++] = '\\';
                    destination[position++] = '\'';
                    break;
                case '\\':
                    destination[position++] = '\\';
                    destination[position++] = '\\';
                    break;
                case '\b':
                    destination[position++] = '\\';
                    destination[position++] = 'b';
                    break;
                case '\f':
                    destination[position++] = '\\';
                    destination[position++] = 'f';
                    break;
                case '\n':
                    destination[position++] = '\\';
                    destination[position++] = 'n';
                    break;
                case '\r':
                    destination[position++] = '\\';
                    destination[position++] = 'r';
                    break;
                case '\t':
                    destination[position++] = '\\';
                    destination[position++] = 't';
                    break;
                case < '\x20':
                    // Control characters: use \u00XX hex escape with lowercase hex
                    destination[position++] = '\\';
                    destination[position++] = 'u';
                    destination[position++] = '0';
                    destination[position++] = '0';
                    destination[position++] = HexDigit(ch >> 4);
                    destination[position++] = HexDigit(ch & 0xF);
                    break;
                default:
                    destination[position++] = ch;
                    break;
            }
        }
    }

    private static char HexDigit(int value) => (char)(value < 10 ? '0' + value : 'a' + value - 10);
}
