namespace Meziantou.Framework.Language.Toml.Internals;

/// <summary>Writes keys and values as TOML text.</summary>
internal static class TomlFormatting
{
    /// <summary>The most characters of a key a diagnostic message shows, roughly: past that, the middle of the key is left out.</summary>
    private const int MaxMessageKeyLength = 200;

    /// <summary>The most characters of one part of a key a diagnostic message shows.</summary>
    private const int MaxMessageKeyPartLength = 64;

    /// <summary>Writes the dotted key made of <paramref name="prefix"/> followed by <paramref name="names"/>, for a diagnostic message.</summary>
    /// <remarks>
    /// A document can repeat a long key many times, each time with a mistake, and every message lives as long as its
    /// diagnostic. So both the work and the text are bounded, whatever the length of the key: a long part is cut short,
    /// and a long key keeps its first and last parts with <c>…</c> in place of the ones between them.
    /// </remarks>
    public static string FormatKeyForMessage(ReadOnlySpan<string> prefix, ReadOnlySpan<string> names)
    {
        var count = prefix.Length + names.Length;
        var head = new List<string>();
        var headLength = 0;
        var first = 0;
        while (first < count && headLength < MaxMessageKeyLength / 2)
        {
            var part = FormatMessageKeyPart(first < prefix.Length ? prefix[first] : names[first - prefix.Length]);
            head.Add(part);
            headLength += part.Length + 1;
            first++;
        }

        var tail = new List<string>();
        var tailLength = 0;
        var last = count;
        while (last > first && tailLength < MaxMessageKeyLength / 2)
        {
            last--;
            var part = FormatMessageKeyPart(last < prefix.Length ? prefix[last] : names[last - prefix.Length]);
            tail.Add(part);
            tailLength += part.Length + 1;
        }

        tail.Reverse();

        return last > first ? string.Join('.', [.. head, "…", .. tail]) : string.Join('.', [.. head, .. tail]);

        static string FormatMessageKeyPart(string name)
        {
            if (name.Length <= MaxMessageKeyPartLength)
                return FormatKeyPart(name);

            var length = char.IsHighSurrogate(name[MaxMessageKeyPartLength - 1]) ? MaxMessageKeyPartLength - 1 : MaxMessageKeyPartLength;

            return FormatKeyPart(string.Concat(name.AsSpan(0, length), "…"));
        }
    }

    /// <summary>Writes one part of a key: bare when it can be, as a basic string otherwise.</summary>
    public static string FormatKeyPart(string name) => SyntaxFacts.IsBareKey(name) ? name : QuoteBasicString(name);

    /// <summary>Writes <paramref name="value"/> as a basic string, escaping what a basic string cannot hold.</summary>
    public static string QuoteBasicString(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (var character in value)
        {
            switch (character)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                default:
                    if (character < ' ' || character == '\u007F')
                    {
                        builder.Append("\\u").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(character);
                    }

                    break;
            }
        }

        builder.Append('"');
        return builder.ToString();
    }

    /// <summary>Determines whether <paramref name="value"/> has no lone surrogate, which no TOML text can hold, escaped or not.</summary>
    public static bool IsWellFormedUtf16(ReadOnlySpan<char> value)
    {
        var index = value.IndexOfAnyInRange('\uD800', '\uDFFF');
        if (index < 0)
            return true;

        for (var i = index; i < value.Length; i++)
        {
            if (char.IsHighSurrogate(value[i]) && i + 1 < value.Length && char.IsLowSurrogate(value[i + 1]))
            {
                i++;
            }
            else if (char.IsSurrogate(value[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Writes a float so that reading it back gives the same value, and so that it reads as a float.</summary>
    public static string FormatFloat(double value)
    {
        if (double.IsNaN(value))
            return "nan";

        if (double.IsPositiveInfinity(value))
            return "inf";

        if (double.IsNegativeInfinity(value))
            return "-inf";

        var text = value.ToString("R", CultureInfo.InvariantCulture);

        // A float needs a fraction or an exponent, or it reads back as an integer: 1 is written 1.0.
        if (!text.Contains('.', StringComparison.Ordinal) && !text.Contains('E', StringComparison.Ordinal))
        {
            text += ".0";
        }

        return text.Replace("E", "e", StringComparison.Ordinal);
    }

    /// <summary>Writes an offset date-time as RFC 3339, keeping as many fractional digits as the value needs.</summary>
    public static string FormatDateTime(DateTimeOffset value)
    {
        var offset = value.Offset == TimeSpan.Zero ? "Z" : value.ToString("zzz", CultureInfo.InvariantCulture);

        return FormatDateTime(value.DateTime) + offset;
    }

    public static string FormatDateTime(DateTime value)
        => value.ToString("yyyy'-'MM'-'dd'T'", CultureInfo.InvariantCulture) + FormatTime(TimeOnly.FromDateTime(value));

    public static string FormatDate(DateOnly value) => value.ToString("yyyy'-'MM'-'dd", CultureInfo.InvariantCulture);

    public static string FormatTime(TimeOnly value)
        => value.ToString(value.Ticks % TimeSpan.TicksPerSecond == 0 ? "HH':'mm':'ss" : "HH':'mm':'ss'.'FFFFFFF", CultureInfo.InvariantCulture);
}
