using Meziantou.Framework.Barcodes.Internal.Linear;

namespace Meziantou.Framework.Barcodes.Internal;

internal static class Gs1ElementStrings
{
    /// <summary>
    /// Concatenates the element strings, with <paramref name="separator"/> after each element whose application identifier
    /// does not have a predefined length, except the last one: the separator stands for a FNC1.
    /// </summary>
    public static string Concatenate(ReadOnlySpan<Gs1Element> elements, char separator, string paramName)
    {
        var data = new StringBuilder();
        for (var i = 0; i < elements.Length; i++)
        {
            var element = elements[i];
            if (element is null)
                throw new ArgumentException("The elements cannot contain null.", paramName);

            data.Append(element.ApplicationIdentifier).Append(element.Value);
            if (i < elements.Length - 1 && Gs1128Encoder.GetPredefinedValueLength(element.ApplicationIdentifier) < 0)
            {
                data.Append(separator);
            }
        }

        return data.ToString();
    }

    /// <summary>
    /// Formats element strings read from a symbol, separated by GS characters (FNC1), as their human-readable
    /// interpretation: each application identifier in parentheses, followed by its data. Returns <see langword="null"/>
    /// when the data does not start with a known application identifier.
    /// </summary>
    public static string? FormatHumanReadable(string data)
    {
        var result = new StringBuilder(data.Length + 8);
        var index = 0;
        while (index < data.Length)
        {
            if (data[index] == '\u001D')
            {
                index++;
                continue;
            }

            // The application identifiers are prefix-free: the first prefix of 2 to 4 digits in the dictionary is the one
            string? applicationIdentifier = null;
            for (var length = 2; length <= 4 && index + length <= data.Length; length++)
            {
                var candidate = data.Substring(index, length);
                if (Gs1.Gs1SyntaxDictionary.Find(candidate) is not null)
                {
                    applicationIdentifier = candidate;
                    break;
                }
            }

            if (applicationIdentifier is null)
                return null;

            index += applicationIdentifier.Length;
            var valueLength = Gs1128Encoder.GetPredefinedValueLength(applicationIdentifier);
            var end = valueLength > 0 ? Math.Min(data.Length, index + valueLength) : data.IndexOf('\u001D', index, StringComparison.Ordinal);
            if (end < 0)
            {
                end = data.Length;
            }

            if (end == index)
                return null;

            result.Append('(').Append(applicationIdentifier).Append(')').Append(data, index, end - index);
            index = end;
        }

        return result.ToString();
    }
}
