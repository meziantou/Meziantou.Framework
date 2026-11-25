using System.Text;

namespace Meziantou.Framework.WebTransport.Http3;

// The sf-string items and lists of RFC 9651 that wt-available-protocols (a list of strings) and wt-protocol (a string)
// use
internal static class StructuredFieldStrings
{
    public static string SerializeString(string value)
    {
        var builder = new StringBuilder(value.Length + 2);
        builder.Append('"');
        foreach (var c in value)
        {
            if (c is < ' ' or > '~')
                throw new ArgumentException("A protocol must contain printable ASCII characters only.", nameof(value));

            if (c is '"' or '\\')
            {
                builder.Append('\\');
            }

            builder.Append(c);
        }

        builder.Append('"');
        return builder.ToString();
    }

    public static string SerializeList(IEnumerable<string> values) => string.Join(", ", values.Select(SerializeString));

    // Returns null when the value is not a list of strings (parameters are ignored)
    public static List<string>? ParseList(string value)
    {
        var result = new List<string>();
        var position = 0;
        SkipWhitespace(value, ref position);
        if (position == value.Length)
            return result;

        while (true)
        {
            var item = ParseString(value, ref position);
            if (item is null)
                return null;

            result.Add(item);
            SkipParameters(value, ref position);
            SkipWhitespace(value, ref position);
            if (position == value.Length)
                return result;

            if (value[position] != ',')
                return null;

            position++;
            SkipWhitespace(value, ref position);
            if (position == value.Length)
                return null;
        }
    }

    public static string? ParseItem(string value)
    {
        var position = 0;
        SkipWhitespace(value, ref position);
        var result = ParseString(value, ref position);
        if (result is null)
            return null;

        SkipParameters(value, ref position);
        SkipWhitespace(value, ref position);
        return position == value.Length ? result : null;
    }

    private static string? ParseString(string value, ref int position)
    {
        if (position >= value.Length || value[position] != '"')
            return null;

        position++;
        var builder = new StringBuilder();
        while (position < value.Length)
        {
            var c = value[position++];
            if (c == '\\')
            {
                if (position >= value.Length || value[position] is not ('"' or '\\'))
                    return null;

                builder.Append(value[position++]);
            }
            else if (c == '"')
            {
                return builder.ToString();
            }
            else if (c is < ' ' or > '~')
            {
                return null;
            }
            else
            {
                builder.Append(c);
            }
        }

        return null;
    }

    private static void SkipParameters(string value, ref int position)
    {
        // ;key=value parameters: skipped up to the next list separator
        while (position < value.Length && value[position] == ';')
        {
            while (position < value.Length && value[position] is not ',')
            {
                position++;
            }
        }
    }

    private static void SkipWhitespace(string value, ref int position)
    {
        while (position < value.Length && value[position] is ' ' or '\t')
        {
            position++;
        }
    }
}
