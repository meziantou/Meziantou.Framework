namespace Meziantou.Framework.PublicApiGenerator;

internal static class CSharpLiteralFormatter
{
    public static string Format(object? value)
    {
        return value switch
        {
            null => "null",
            string text => FormatString(text),
            char character => FormatChar(character),
            bool boolean => boolean ? "true" : "false",
            float single => FormatSingle(single),
            double @double => FormatDouble(@double),
            decimal @decimal => @decimal.ToString(CultureInfo.InvariantCulture) + "m",
            long int64 => int64.ToString(CultureInfo.InvariantCulture) + "L",
            ulong uint64 => uint64.ToString(CultureInfo.InvariantCulture) + "UL",
            uint uint32 => uint32.ToString(CultureInfo.InvariantCulture) + "U",
            int int32 => int32.ToString(CultureInfo.InvariantCulture),
            short int16 => int16.ToString(CultureInfo.InvariantCulture),
            ushort uint16 => uint16.ToString(CultureInfo.InvariantCulture),
            sbyte int8 => int8.ToString(CultureInfo.InvariantCulture),
            byte uint8 => uint8.ToString(CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null",
        };
    }

    public static string FormatString(string value)
    {
        var sb = new StringBuilder(value.Length + 2);
        sb.Append('"');
        foreach (var character in value)
        {
            AppendCharacter(sb, character, quote: '"');
        }

        sb.Append('"');
        return sb.ToString();
    }

    public static string FormatChar(char value)
    {
        var sb = new StringBuilder(4);
        sb.Append('\'');
        AppendCharacter(sb, value, quote: '\'');
        sb.Append('\'');
        return sb.ToString();
    }

    private static void AppendCharacter(StringBuilder sb, char character, char quote)
    {
        if (character == quote)
        {
            sb.Append('\\').Append(quote);
            return;
        }

        switch (character)
        {
            case '\\':
                sb.Append("\\\\");
                return;
            case '\0':
                sb.Append("\\0");
                return;
            case '\a':
                sb.Append("\\a");
                return;
            case '\b':
                sb.Append("\\b");
                return;
            case '\f':
                sb.Append("\\f");
                return;
            case '\n':
                sb.Append("\\n");
                return;
            case '\r':
                sb.Append("\\r");
                return;
            case '\t':
                sb.Append("\\t");
                return;
            case '\v':
                sb.Append("\\v");
                return;
        }

        if (RequiresUnicodeEscape(character))
        {
            sb.Append("\\u").Append(((int)character).ToString("x4", CultureInfo.InvariantCulture));
            return;
        }

        sb.Append(character);
    }

    private static bool RequiresUnicodeEscape(char character)
    {
        // Surrogates are escaped individually so that unpaired ones survive a round-trip.
        // U+0085, U+2028 and U+2029 are new-line characters for the C# lexer even though they are not control characters.
        return char.IsControl(character) || char.IsSurrogate(character) || character is '\u0085' or '\u2028' or '\u2029';
    }

    private static string FormatSingle(float value)
    {
        if (float.IsNaN(value))
            return "float.NaN";

        if (float.IsPositiveInfinity(value))
            return "float.PositiveInfinity";

        if (float.IsNegativeInfinity(value))
            return "float.NegativeInfinity";

        return value.ToString("R", CultureInfo.InvariantCulture) + "f";
    }

    private static string FormatDouble(double value)
    {
        if (double.IsNaN(value))
            return "double.NaN";

        if (double.IsPositiveInfinity(value))
            return "double.PositiveInfinity";

        if (double.IsNegativeInfinity(value))
            return "double.NegativeInfinity";

        return value.ToString("R", CultureInfo.InvariantCulture) + "d";
    }
}
