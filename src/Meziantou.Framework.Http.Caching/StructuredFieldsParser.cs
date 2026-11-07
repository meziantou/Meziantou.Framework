namespace Meziantou.Framework.Http.Caching;

/// <summary>Parses the Structured Fields dictionary syntax (RFC 8941 Section 4.2.2).</summary>
/// <remarks>
/// Only the shapes used by <c>No-Vary-Search</c> are materialized: booleans, strings, and inner lists of
/// those. Any other item type still has to parse, because an unknown dictionary member must not fail the
/// whole field, but its value is reported as <see cref="StructuredFieldItemType.Other"/>.
/// </remarks>
internal static class StructuredFieldsParser
{
    /// <summary>Parses <paramref name="value"/> as a dictionary, or returns <see langword="false"/> when it is malformed.</summary>
    public static bool TryParseDictionary(string value, [NotNullWhen(true)] out Dictionary<string, StructuredFieldValue>? result)
    {
        result = null;

        var input = value.AsSpan();
        var position = 0;
        var dictionary = new Dictionary<string, StructuredFieldValue>(StringComparer.Ordinal);

        SkipSpaces(input, ref position);
        while (position < input.Length)
        {
            if (!TryParseKey(input, ref position, out var key))
                return false;

            StructuredFieldValue memberValue;
            if (position < input.Length && input[position] is '=')
            {
                position++;
                if (!TryParseItemOrInnerList(input, ref position, out memberValue))
                    return false;
            }
            else
            {
                // RFC 8941 Section 4.2.2: a member without a value is the boolean true
                memberValue = StructuredFieldValue.FromItem(StructuredFieldItem.True);
                if (!TrySkipParameters(input, ref position))
                    return false;
            }

            // RFC 8941 Section 4.2.2: a duplicate key keeps the last value
            dictionary[key] = memberValue;

            SkipOptionalWhitespace(input, ref position);
            if (position >= input.Length)
                break;

            if (input[position] is not ',')
                return false;

            position++;
            SkipOptionalWhitespace(input, ref position);

            // RFC 8941 Section 4.2.2: a trailing comma is not allowed
            if (position >= input.Length)
                return false;
        }

        result = dictionary;
        return true;
    }

    private static bool TryParseKey(ReadOnlySpan<char> input, ref int position, [NotNullWhen(true)] out string? key)
    {
        // RFC 8941 Section 4.2.3.3
        key = null;
        if (position >= input.Length)
            return false;

        if (!IsLowerCaseAlpha(input[position]) && input[position] is not '*')
            return false;

        var start = position;
        position++;
        while (position < input.Length && (IsLowerCaseAlpha(input[position]) || char.IsAsciiDigit(input[position]) || input[position] is '_' or '-' or '.' or '*'))
        {
            position++;
        }

        key = input[start..position].ToString();
        return true;
    }

    private static bool TryParseItemOrInnerList(ReadOnlySpan<char> input, ref int position, out StructuredFieldValue value)
    {
        // RFC 8941 Section 4.2.1
        if (position < input.Length && input[position] is '(')
            return TryParseInnerList(input, ref position, out value);

        value = default;
        if (!TryParseBareItem(input, ref position, out var item))
            return false;

        if (!TrySkipParameters(input, ref position))
            return false;

        value = StructuredFieldValue.FromItem(item);
        return true;
    }

    private static bool TryParseInnerList(ReadOnlySpan<char> input, ref int position, out StructuredFieldValue value)
    {
        // RFC 8941 Section 4.2.1.2
        value = default;
        position++;

        var items = new List<StructuredFieldItem>();
        while (true)
        {
            SkipSpaces(input, ref position);
            if (position >= input.Length)
                return false;

            if (input[position] is ')')
            {
                position++;
                if (!TrySkipParameters(input, ref position))
                    return false;

                value = StructuredFieldValue.FromInnerList(items);
                return true;
            }

            if (!TryParseBareItem(input, ref position, out var item))
                return false;

            if (!TrySkipParameters(input, ref position))
                return false;

            items.Add(item);

            if (position >= input.Length)
                return false;

            if (input[position] is not ' ' and not ')')
                return false;
        }
    }

    private static bool TrySkipParameters(ReadOnlySpan<char> input, ref int position)
    {
        // RFC 8941 Section 4.2.3.2: parameters are parsed but carry no meaning for No-Vary-Search
        while (position < input.Length && input[position] is ';')
        {
            position++;
            SkipSpaces(input, ref position);

            if (!TryParseKey(input, ref position, out _))
                return false;

            if (position < input.Length && input[position] is '=')
            {
                position++;
                if (!TryParseBareItem(input, ref position, out _))
                    return false;
            }
        }

        return true;
    }

    private static bool TryParseBareItem(ReadOnlySpan<char> input, ref int position, out StructuredFieldItem item)
    {
        // RFC 8941 Section 4.2.3.1
        item = default;
        if (position >= input.Length)
            return false;

        var c = input[position];
        if (c is '-' || char.IsAsciiDigit(c))
            return TrySkipNumber(input, ref position);

        if (c is '"')
            return TryParseString(input, ref position, out item);

        if (c is '?')
            return TryParseBoolean(input, ref position, out item);

        if (c is ':')
            return TrySkipByteSequence(input, ref position);

        if (c is '*' || char.IsAsciiLetter(c))
            return TrySkipToken(input, ref position);

        return false;
    }

    private static bool TryParseBoolean(ReadOnlySpan<char> input, ref int position, out StructuredFieldItem item)
    {
        // RFC 8941 Section 4.2.8
        item = default;
        if (position + 1 >= input.Length)
            return false;

        var value = input[position + 1];
        if (value is not '0' and not '1')
            return false;

        position += 2;
        item = value is '1' ? StructuredFieldItem.True : StructuredFieldItem.False;
        return true;
    }

    private static bool TryParseString(ReadOnlySpan<char> input, ref int position, out StructuredFieldItem item)
    {
        // RFC 8941 Section 4.2.5
        item = default;
        position++;

        var builder = new StringBuilder();
        while (position < input.Length)
        {
            var c = input[position++];
            if (c is '\\')
            {
                if (position >= input.Length)
                    return false;

                var escaped = input[position++];
                if (escaped is not '\\' and not '"')
                    return false;

                builder.Append(escaped);
                continue;
            }

            if (c is '"')
            {
                item = StructuredFieldItem.FromString(builder.ToString());
                return true;
            }

            if (c is < ' ' or > '~')
                return false;

            builder.Append(c);
        }

        return false;
    }

    private static bool TrySkipNumber(ReadOnlySpan<char> input, ref int position)
    {
        // RFC 8941 Section 4.2.4
        if (input[position] is '-')
        {
            position++;
        }

        var digits = 0;
        var hasDecimalPoint = false;
        while (position < input.Length)
        {
            if (char.IsAsciiDigit(input[position]))
            {
                digits++;
                position++;
                continue;
            }

            if (input[position] is '.' && !hasDecimalPoint && digits > 0)
            {
                hasDecimalPoint = true;
                position++;
                continue;
            }

            break;
        }

        return digits > 0;
    }

    private static bool TrySkipByteSequence(ReadOnlySpan<char> input, ref int position)
    {
        // RFC 8941 Section 4.2.7
        position++;
        while (position < input.Length)
        {
            var c = input[position++];
            if (c is ':')
                return true;

            if (!char.IsAsciiLetterOrDigit(c) && c is not '+' and not '/' and not '=')
                return false;
        }

        return false;
    }

    private static bool TrySkipToken(ReadOnlySpan<char> input, ref int position)
    {
        // RFC 8941 Section 4.2.6
        position++;
        while (position < input.Length && IsTokenChar(input[position]))
        {
            position++;
        }

        return true;
    }

    private static bool IsTokenChar(char c)
    {
        // RFC 9110 Section 5.6.2 tchar, plus ":" and "/"
        return char.IsAsciiLetterOrDigit(c) || c is '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.' or '^' or '_' or '`' or '|' or '~' or ':' or '/';
    }

    private static bool IsLowerCaseAlpha(char c) => c is >= 'a' and <= 'z';

    private static void SkipSpaces(ReadOnlySpan<char> input, ref int position)
    {
        while (position < input.Length && input[position] is ' ')
        {
            position++;
        }
    }

    private static void SkipOptionalWhitespace(ReadOnlySpan<char> input, ref int position)
    {
        while (position < input.Length && input[position] is ' ' or '\t')
        {
            position++;
        }
    }
}
