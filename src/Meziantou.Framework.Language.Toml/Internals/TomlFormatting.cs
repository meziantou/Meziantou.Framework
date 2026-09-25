namespace Meziantou.Framework.Language.Toml.Internals;

/// <summary>Writes keys and values as TOML text.</summary>
internal static class TomlFormatting
{
    /// <summary>Writes a dotted key, quoting each part that cannot be written bare.</summary>
    public static string FormatKey(IEnumerable<string> names) => string.Join('.', names.Select(FormatKeyPart));

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
