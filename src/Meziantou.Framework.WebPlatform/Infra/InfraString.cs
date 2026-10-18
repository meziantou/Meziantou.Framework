using System.Text;

namespace Meziantou.Framework.WebPlatform.Infra;

internal static class InfraString
{
    /// <summary>
    /// The UTF-8 encoding of a string (https://encoding.spec.whatwg.org/#utf-8-encode), as an isomorphic string: each
    /// character is a byte (e.g. the value of an HTTP header). The lone surrogates are encoded as U+FFFD.
    /// </summary>
    public static string Utf8EncodeToIsomorphic(string value)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(value);
        return System.Text.Encoding.Latin1.GetString(bytes);
    }

    /// <summary>
    /// The UTF-8 decoding without BOM (https://encoding.spec.whatwg.org/#utf-8-decode-without-bom) of an isomorphic string
    /// (each character is a byte): the invalid sequences are decoded as U+FFFD.
    /// </summary>
    public static string Utf8DecodeWithoutBom(string isomorphic)
    {
        if (System.Text.Ascii.IsValid(isomorphic))
            return isomorphic;

        var bytes = System.Text.Encoding.Latin1.GetBytes(isomorphic);
        return System.Text.Encoding.UTF8.GetString(bytes);
    }

    /// <summary>
    /// Converts a string to a scalar value string (https://infra.spec.whatwg.org/#javascript-string-convert),
    /// replacing lone surrogates with U+FFFD. This is the conversion applied to USVString arguments.
    /// </summary>
    public static string ToScalarValueString(string value)
    {
        var span = value.AsSpan();
        var index = IndexOfLoneSurrogate(span);
        if (index < 0)
            return value;

        var builder = new StringBuilder(value.Length);
        foreach (var rune in span.EnumerateRunes())
        {
            builder.Append(rune);
        }

        return builder.ToString();
    }

    private static int IndexOfLoneSurrogate(ReadOnlySpan<char> value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (!char.IsSurrogate(c))
                continue;

            if (char.IsHighSurrogate(c) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                i++;
                continue;
            }

            return i;
        }

        return -1;
    }

    /// <summary>https://infra.spec.whatwg.org/#strip-and-collapse-ascii-whitespace</summary>
    public static string StripAndCollapseAsciiWhitespace(string value)
    {
        if (IsStrippedAndCollapsed(value))
            return value;

        var builder = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var c in value)
        {
            if (InfraAscii.IsAsciiWhitespace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(c);
        }

        return builder.ToString();
    }

    /// <summary>Returns whether the value has no leading or trailing ASCII whitespace, and no ASCII whitespace but single spaces.</summary>
    private static bool IsStrippedAndCollapsed(string value)
    {
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (InfraAscii.IsAsciiWhitespace(c) && (c != ' ' || i == 0 || i == value.Length - 1 || value[i - 1] == ' '))
                return false;
        }

        return true;
    }

    /// <summary>Replaces each sequence of ASCII whitespace by a single space. Returns <paramref name="value"/> when it does not change.</summary>
    public static string CollapseAsciiWhitespace(string value)
    {
        StringBuilder? builder = null;
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            if (!InfraAscii.IsAsciiWhitespace(c))
            {
                builder?.Append(c);
                continue;
            }

            var end = i + 1;
            while (end < value.Length && InfraAscii.IsAsciiWhitespace(value[end]))
            {
                end++;
            }

            if (builder is null && c == ' ' && end == i + 1)
                continue;

            builder ??= new StringBuilder(value.Length).Append(value, 0, i);
            builder.Append(' ');
            i = end - 1;
        }

        return builder?.ToString() ?? value;
    }

    /// <summary>Returns whether the value is empty or contains only ASCII whitespace.</summary>
    public static bool IsAsciiWhitespaceOnly(ReadOnlySpan<char> value)
    {
        foreach (var c in value)
        {
            if (!InfraAscii.IsAsciiWhitespace(c))
                return false;
        }

        return true;
    }

    /// <summary>https://infra.spec.whatwg.org/#split-on-ascii-whitespace</summary>
    public static List<string> SplitOnAsciiWhitespace(string value)
    {
        var result = new List<string>();
        foreach (var range in value.AsSpan().SplitAny(" \t\n\f\r"))
        {
            if (range.End.GetOffset(value.Length) > range.Start.GetOffset(value.Length))
            {
                result.Add(value[range]);
            }
        }

        return result;
    }
}
