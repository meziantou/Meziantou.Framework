using System.Globalization;

namespace Meziantou.Framework.WebPlatform.Infra;

/// <summary>A token of a structured field (https://www.rfc-editor.org/rfc/rfc8941#name-tokens), distinct from a string.</summary>
internal readonly record struct StructuredFieldToken(string Value);

/// <summary>An item of a structured field (https://www.rfc-editor.org/rfc/rfc8941#name-items): a bare item and its parameters.</summary>
/// <param name="Value">The bare item: a <see cref="StructuredFieldToken"/>, a string, a long, a decimal, a bool or a byte array.</param>
/// <param name="Parameters">The parameters, in order (a parameter without value is <c>true</c>).</param>
internal sealed record StructuredFieldItem(object Value, IReadOnlyList<KeyValuePair<string, object>> Parameters)
{
    /// <summary>Returns the value of a parameter, or null.</summary>
    public object? GetParameter(string key)
    {
        foreach (var (name, value) in Parameters)
        {
            if (name == key)
                return value;
        }

        return null;
    }
}

/// <summary>A member of a structured field dictionary or list (https://www.rfc-editor.org/rfc/rfc8941#name-dictionaries): an item or an inner list.</summary>
/// <param name="Item">The item, or null for an inner list.</param>
/// <param name="InnerList">The items of an inner list, or null for an item.</param>
internal sealed record StructuredFieldMember(StructuredFieldItem? Item, IReadOnlyList<StructuredFieldItem>? InnerList)
{
    /// <summary>The parameters of an inner list, in order (the parameters of an item are the ones of <see cref="Item"/>).</summary>
    public IReadOnlyList<KeyValuePair<string, object>> InnerListParameters { get; init; } = [];

    /// <summary>Returns the value of a parameter of the item or of the inner list, or null.</summary>
    public object? GetParameter(string key)
    {
        foreach (var (name, value) in Item?.Parameters ?? InnerListParameters)
        {
            if (name == key)
                return value;
        }

        return null;
    }
}

/// <summary>The parsing of the structured fields (https://www.rfc-editor.org/rfc/rfc8941).</summary>
internal static class StructuredFields
{
    /// <summary>
    /// https://fetch.spec.whatwg.org/#concept-header-list-get-structured-header with the type "item": the combined value of
    /// the header parsed as an item, or null when the header is missing or is not a valid item (e.g. a repeated header).
    /// </summary>
    public static StructuredFieldItem? GetItem(IReadOnlyList<KeyValuePair<string, string>> headers, string name)
    {
        // The values of a header list are normalized (https://fetch.spec.whatwg.org/#concept-header-value-normalize)
        return Dom.HeaderList.Get(headers, name) is { } value ? ParseItem(value.Trim(' ', '\t')) : null;
    }

    /// <summary>https://www.rfc-editor.org/rfc/rfc8941#name-parsing-structured-fields with the type "item".</summary>
    public static StructuredFieldItem? ParseItem(string value)
    {
        var position = 0;
        SkipSpaces(value, ref position);
        var item = ParseItem(value, ref position);
        SkipSpaces(value, ref position);
        return item is not null && position == value.Length ? item : null;
    }

    /// <summary>
    /// https://fetch.spec.whatwg.org/#concept-header-list-get-structured-header with the type "dictionary": the combined
    /// value of the header parsed as a dictionary, or null when the header is missing or is not a valid dictionary.
    /// </summary>
    public static List<KeyValuePair<string, StructuredFieldMember>>? GetDictionary(IReadOnlyList<KeyValuePair<string, string>> headers, string name)
    {
        return Dom.HeaderList.Get(headers, name) is { } value ? ParseDictionary(value.Trim(' ', '\t')) : null;
    }

    /// <summary>
    /// https://www.rfc-editor.org/rfc/rfc8941#name-parsing-a-dictionary: the members in order; a key that appears several
    /// times keeps its first position and its last value.
    /// </summary>
    public static List<KeyValuePair<string, StructuredFieldMember>>? ParseDictionary(string value)
    {
        var position = 0;
        SkipSpaces(value, ref position);
        var dictionary = new List<KeyValuePair<string, StructuredFieldMember>>();
        while (position < value.Length)
        {
            if (ParseKey(value, ref position) is not { } key)
                return null;

            StructuredFieldMember member;
            if (position < value.Length && value[position] == '=')
            {
                position++;
                if (position < value.Length && value[position] == '(')
                {
                    if (ParseInnerList(value, ref position, out var innerListParameters) is not { } innerList)
                        return null;

                    member = new StructuredFieldMember(null, innerList) { InnerListParameters = innerListParameters };
                }
                else
                {
                    if (ParseItem(value, ref position) is not { } item)
                        return null;

                    member = new StructuredFieldMember(item, null);
                }
            }
            else
            {
                // A member without value is the boolean true, with the parameters that follow the key
                if (ParseItemParameters(value, ref position, bareItem: true) is not { } item)
                    return null;

                member = new StructuredFieldMember(item, null);
            }

            var index = dictionary.FindIndex(entry => entry.Key == key);
            if (index >= 0)
            {
                dictionary[index] = new(key, member);
            }
            else
            {
                dictionary.Add(new(key, member));
            }

            // The optional whitespace, then a comma followed by the optional whitespace and another member
            while (position < value.Length && value[position] is ' ' or '\t')
            {
                position++;
            }

            if (position >= value.Length)
                return dictionary;

            if (value[position] != ',')
                return null;

            position++;
            while (position < value.Length && value[position] is ' ' or '\t')
            {
                position++;
            }

            if (position >= value.Length)
                return null;
        }

        return dictionary;
    }

    /// <summary>
    /// https://fetch.spec.whatwg.org/#concept-header-list-get-structured-header with the type "list": the combined value of
    /// the header parsed as a list (https://www.rfc-editor.org/rfc/rfc8941#name-parsing-a-list), or null when the header is
    /// missing or is not a valid list.
    /// </summary>
    public static List<StructuredFieldMember>? GetList(IReadOnlyList<KeyValuePair<string, string>> headers, string name)
    {
        if (Dom.HeaderList.Get(headers, name) is not { } headerValue)
            return null;

        var value = headerValue.Trim(' ', '\t');
        var position = 0;
        var members = new List<StructuredFieldMember>();
        while (position < value.Length)
        {
            if (value[position] == '(')
            {
                if (ParseInnerList(value, ref position, out var innerListParameters) is not { } innerList)
                    return null;

                members.Add(new StructuredFieldMember(null, innerList) { InnerListParameters = innerListParameters });
            }
            else
            {
                if (ParseItem(value, ref position) is not { } item)
                    return null;

                members.Add(new StructuredFieldMember(item, null));
            }

            while (position < value.Length && value[position] is ' ' or '\t')
            {
                position++;
            }

            if (position >= value.Length)
                return members;

            if (value[position] != ',')
                return null;

            position++;
            while (position < value.Length && value[position] is ' ' or '\t')
            {
                position++;
            }

            if (position >= value.Length)
                return null;
        }

        return members;
    }

    /// <summary>https://www.rfc-editor.org/rfc/rfc8941#name-parsing-an-inner-list</summary>
    private static List<StructuredFieldItem>? ParseInnerList(string value, ref int position, out IReadOnlyList<KeyValuePair<string, object>> parameters)
    {
        parameters = [];
        position++;
        var items = new List<StructuredFieldItem>();
        while (position < value.Length)
        {
            SkipSpaces(value, ref position);
            if (position < value.Length && value[position] == ')')
            {
                position++;
                if (ParseItemParameters(value, ref position, bareItem: true) is not { } innerList)
                    return null;

                parameters = innerList.Parameters;
                return items;
            }

            if (ParseItem(value, ref position) is not { } item)
                return null;

            items.Add(item);
            if (position < value.Length && value[position] is not (' ' or ')'))
                return null;
        }

        return null;
    }

    private static void SkipSpaces(string value, ref int position)
    {
        while (position < value.Length && value[position] == ' ')
        {
            position++;
        }
    }

    /// <summary>https://www.rfc-editor.org/rfc/rfc8941#name-parsing-an-item</summary>
    private static StructuredFieldItem? ParseItem(string value, ref int position)
    {
        if (ParseBareItem(value, ref position) is not { } bareItem)
            return null;

        return ParseItemParameters(value, ref position, bareItem);
    }

    /// <summary>The item of a bare item and of the parameters that follow it (https://www.rfc-editor.org/rfc/rfc8941#name-parsing-parameters).</summary>
    private static StructuredFieldItem? ParseItemParameters(string value, ref int position, object bareItem)
    {
        // https://www.rfc-editor.org/rfc/rfc8941#name-parsing-parameters
        var parameters = new List<KeyValuePair<string, object>>();
        while (position < value.Length && value[position] == ';')
        {
            position++;
            SkipSpaces(value, ref position);
            if (ParseKey(value, ref position) is not { } key)
                return null;

            object parameterValue = true;
            if (position < value.Length && value[position] == '=')
            {
                position++;
                if (ParseBareItem(value, ref position) is not { } parsed)
                    return null;

                parameterValue = parsed;
            }

            var index = parameters.FindIndex(parameter => parameter.Key == key);
            if (index >= 0)
            {
                parameters[index] = new(key, parameterValue);
            }
            else
            {
                parameters.Add(new(key, parameterValue));
            }
        }

        return new StructuredFieldItem(bareItem, parameters);
    }

    /// <summary>https://www.rfc-editor.org/rfc/rfc8941#name-parsing-a-key</summary>
    private static string? ParseKey(string value, ref int position)
    {
        if (position >= value.Length || !(value[position] is >= 'a' and <= 'z' or '*'))
            return null;

        var start = position;
        while (position < value.Length && value[position] is >= 'a' and <= 'z' or >= '0' and <= '9' or '_' or '-' or '.' or '*')
        {
            position++;
        }

        return value[start..position];
    }

    /// <summary>https://www.rfc-editor.org/rfc/rfc8941#name-parsing-a-bare-item</summary>
    private static object? ParseBareItem(string value, ref int position)
    {
        if (position >= value.Length)
            return null;

        var c = value[position];
        if (c is '-' or >= '0' and <= '9')
            return ParseNumber(value, ref position);

        if (c == '"')
            return ParseString(value, ref position);

        if (c is '*' or >= 'a' and <= 'z' or >= 'A' and <= 'Z')
            return ParseToken(value, ref position);

        if (c == ':')
            return ParseByteSequence(value, ref position);

        if (c == '?')
        {
            // https://www.rfc-editor.org/rfc/rfc8941#name-parsing-a-boolean
            if (position + 1 < value.Length && value[position + 1] is '0' or '1')
            {
                position += 2;
                return value[position - 1] == '1';
            }

            return null;
        }

        return null;
    }

    /// <summary>https://www.rfc-editor.org/rfc/rfc8941#name-parsing-an-integer-or-decim</summary>
    private static object? ParseNumber(string value, ref int position)
    {
        var start = position;
        if (value[position] == '-')
        {
            position++;
        }

        if (position >= value.Length || !char.IsAsciiDigit(value[position]))
            return null;

        var isDecimal = false;
        var digitsStart = position;
        while (position < value.Length)
        {
            var c = value[position];
            if (char.IsAsciiDigit(c))
            {
                position++;
            }
            else if (!isDecimal && c == '.')
            {
                if (position - digitsStart > 12)
                    return null;

                isDecimal = true;
                position++;
            }
            else
            {
                break;
            }

            if ((!isDecimal && position - digitsStart > 15) || (isDecimal && position - digitsStart > 16))
                return null;
        }

        var number = value[start..position];
        if (!isDecimal)
            return long.Parse(number, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

        if (number[^1] == '.' || number.Length - number.IndexOf('.', StringComparison.Ordinal) - 1 > 3)
            return null;

        return decimal.Parse(number, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
    }

    /// <summary>https://www.rfc-editor.org/rfc/rfc8941#name-parsing-a-string</summary>
    private static string? ParseString(string value, ref int position)
    {
        position++;
        var result = new System.Text.StringBuilder();
        while (position < value.Length)
        {
            var c = value[position++];
            if (c == '\\')
            {
                if (position >= value.Length || value[position] is not ('"' or '\\'))
                    return null;

                result.Append(value[position++]);
            }
            else if (c == '"')
            {
                return result.ToString();
            }
            else if (c is < '\x20' or > '\x7E')
            {
                return null;
            }
            else
            {
                result.Append(c);
            }
        }

        return null;
    }

    /// <summary>https://www.rfc-editor.org/rfc/rfc8941#name-parsing-a-token</summary>
    private static StructuredFieldToken ParseToken(string value, ref int position)
    {
        var start = position;
        position++;
        while (position < value.Length && (IsTChar(value[position]) || value[position] is ':' or '/'))
        {
            position++;
        }

        return new StructuredFieldToken(value[start..position]);

        static bool IsTChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.' or '^' or '_' or '`' or '|' or '~';
    }

    /// <summary>https://www.rfc-editor.org/rfc/rfc8941#name-parsing-a-byte-sequence</summary>
    private static byte[]? ParseByteSequence(string value, ref int position)
    {
        var end = value.IndexOf(':', position + 1, StringComparison.Ordinal);
        if (end < 0)
            return null;

        var content = value[(position + 1)..end];
        position = end + 1;
        try
        {
            return Convert.FromBase64String(content);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
