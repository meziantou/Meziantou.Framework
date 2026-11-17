using System.Globalization;

namespace Meziantou.Framework.Markdown.Parsing;

/// <summary>
/// Reads the generic attributes of Markdig: <c>{#id .class name=value name="value" name}</c>, as
/// Meziantou.Framework.Markdown 1.x reads them. The end of the range reads as <c>\0</c>, which ends the attributes.
/// </summary>
internal static class AttributesScanner
{
    // The steps of TryParse that can be reached at a position of the text, whose outcome only depends on this position
    private const byte AttributesValid = 1;
    private const byte ValueValid = 2;
    private const byte AfterEqualsValid = 4;
    private const byte AfterNameValid = 8;

    /// <summary>
    /// Reads the attributes whose <c>{</c> is at <paramref name="start"/>. Returns where they end, after the <c>}</c> and the line
    /// ending that follows it, or -1.
    /// </summary>
    /// <param name="textStart">Where the text that the attributes are read in starts: the characters before it are not read.</param>
    public static int TryParse(string text, int start, int end, out ParsedAttributes? attributes, int textStart = 0)
    {
        attributes = null;
        if (start > textStart && text[start - 1] == '{')
        {
            return -1;
        }

        string? id = null;
        List<string>? classes = null;
        List<KeyValuePair<string, string?>>? properties = null;
        var position = start + 1;
        var c = At(text, position, end);
        while (true)
        {
            if (c == '}')
            {
                position++;
                if (At(text, position, end) == '\n')
                {
                    position++;
                }
                else if (At(text, position, end) == '\r' && At(text, position + 1, end) == '\n')
                {
                    position += 2;
                }

                attributes = new ParsedAttributes(id, classes ?? [], properties ?? []);
                return position;
            }

            if (c == '\0')
            {
                return -1;
            }

            if (c is '#' or '.')
            {
                var isClass = c == '.';
                var valueStart = ++position;
                c = At(text, position, end);
                while (c != '}' && !IsWhiteSpaceOrZero(c))
                {
                    c = At(text, ++position, end);
                }

                // An empty id or class makes the attributes invalid
                if (position == valueStart)
                {
                    return -1;
                }

                if (isClass)
                {
                    (classes ??= []).Add(text[valueStart..position]);
                }
                else
                {
                    id = text[valueStart..position];
                }

                continue;
            }

            if (!IsWhitespace(c))
            {
                if (!IsStartAttributeName(c))
                {
                    return -1;
                }

                var nameStart = position;
                do
                {
                    c = At(text, ++position, end);
                }
                while (IsAttributeNameChar(c));

                var name = text[nameStart..position];
                var hasSpace = c is ' ' or '\t';
                while (IsWhitespace(At(text, position, end)))
                {
                    position++;
                }

                c = At(text, position, end);

                // A boolean attribute is not followed by =
                if ((hasSpace && (c is '.' or '#' || IsStartAttributeName(c))) || c == '}')
                {
                    (properties ??= []).Add(new KeyValuePair<string, string?>(name, null));
                    continue;
                }

                if (c != '=')
                {
                    return -1;
                }

                position++;
                while (IsWhitespace(At(text, position, end)))
                {
                    position++;
                }

                c = At(text, position, end);
                int valueStart;
                int valueEnd;
                if (c is '\'' or '"')
                {
                    var quote = c;
                    valueStart = position + 1;
                    do
                    {
                        c = At(text, ++position, end);
                        if (c == '\0')
                        {
                            return -1;
                        }
                    }
                    while (c != quote);

                    valueEnd = position;
                    c = At(text, ++position, end);
                }
                else
                {
                    // Up to whitespace or }
                    valueStart = position;
                    while (!IsWhitespace(c) && c != '}')
                    {
                        if (c == '\0')
                        {
                            return -1;
                        }

                        c = At(text, ++position, end);
                    }

                    valueEnd = position;
                    if (valueEnd == valueStart)
                    {
                        return -1;
                    }
                }

                (properties ??= []).Add(new KeyValuePair<string, string?>(name, text[valueStart..valueEnd]));
                continue;
            }

            c = At(text, ++position, end);
        }
    }

    /// <summary>
    /// Computes, for each position from <paramref name="start"/> to <paramref name="end"/>, whether attributes whose <c>{</c> is just
    /// before it are valid, in linear time: a text with many <c>{</c> would otherwise be scanned again from each of them.
    /// </summary>
    public static byte[] ComputeOutcomes(string text, int start, int end)
    {
        // The positions from start to end, end reading as \0
        var outcomes = new byte[end - start + 1];
        var nextIdentifierEnd = end;
        var nextNonWhitespace = end;
        var nextSingleQuote = end;
        var nextDoubleQuote = end;
        var nextNameEnd = end;
        for (var position = end; position >= start; position--)
        {
            var c = position < end ? text[position] : '\0';
            var identifierEndAfter = nextIdentifierEnd;
            var singleQuoteAfter = nextSingleQuote;
            var doubleQuoteAfter = nextDoubleQuote;
            var nameEndAfter = nextNameEnd;
            if (c == '}' || IsWhiteSpaceOrZero(c))
            {
                nextIdentifierEnd = position;
            }

            if (!IsWhitespace(c))
            {
                nextNonWhitespace = position;
            }

            if (c is '\'' or '\0')
            {
                nextSingleQuote = position;
            }

            if (c is '"' or '\0')
            {
                nextDoubleQuote = position;
            }

            if (!IsAttributeNameChar(c))
            {
                nextNameEnd = position;
            }

            bool attributesValid;
            if (c == '}')
            {
                attributesValid = true;
            }
            else if (c == '\0')
            {
                attributesValid = false;
            }
            else if (c is '#' or '.')
            {
                attributesValid = identifierEndAfter != position + 1 && Get(identifierEndAfter, AttributesValid);
            }
            else if (!IsWhitespace(c))
            {
                attributesValid = IsStartAttributeName(c) && Get(nameEndAfter, AfterNameValid);
            }
            else
            {
                attributesValid = Get(position + 1, AttributesValid);
            }

            bool valueValid;
            if (c is '\'' or '"')
            {
                var closingQuote = c == '\'' ? singleQuoteAfter : doubleQuoteAfter;
                valueValid = closingQuote < end && text[closingQuote] == c && Get(closingQuote + 1, AttributesValid);
            }
            else
            {
                valueValid = nextIdentifierEnd != position && nextIdentifierEnd < end && text[nextIdentifierEnd] != '\0' && Get(nextIdentifierEnd, AttributesValid);
            }

            var afterEqualsValid = nextNonWhitespace == position ? valueValid : Get(nextNonWhitespace, ValueValid);
            bool afterNameValid;
            var next = nextNonWhitespace < end ? text[nextNonWhitespace] : '\0';
            if ((c is ' ' or '\t' && (next is '.' or '#' || IsStartAttributeName(next))) || next == '}')
            {
                afterNameValid = nextNonWhitespace == position ? attributesValid : Get(nextNonWhitespace, AttributesValid);
            }
            else
            {
                afterNameValid = next == '=' && Get(nextNonWhitespace + 1, AfterEqualsValid);
            }

            outcomes[position - start] = (byte)((attributesValid ? AttributesValid : 0) | (valueValid ? ValueValid : 0) | (afterEqualsValid ? AfterEqualsValid : 0) | (afterNameValid ? AfterNameValid : 0));
        }

        return outcomes;

        bool Get(int position, byte step) => position <= end && (outcomes[position - start] & step) != 0;
    }

    /// <summary>Gets whether attributes whose <c>{</c> is at <paramref name="brace"/> are valid, from the outcomes computed from <paramref name="start"/>.</summary>
    public static bool IsValid(byte[] outcomes, int start, int brace)
    {
        var index = brace + 1 - start;
        return index >= 0 && index < outcomes.Length && (outcomes[index] & AttributesValid) != 0;
    }

    private static char At(string text, int position, int end) => position < end ? text[position] : '\0';

    private static bool IsStartAttributeName(char c) => char.IsAsciiLetter(c) || c is '_' or ':';

    private static bool IsAttributeNameChar(char c) => char.IsAsciiLetterOrDigit(c) || c is '_' or ':' or '.' or '-';

    // As Markdig: the whitespace of CommonMark, without the vertical tab
    private static bool IsWhitespace(char c) => c is ' ' or '\t' or '\n' or '\f' or '\r' || (c >= '\u00A0' && CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.SpaceSeparator);

    private static bool IsWhiteSpaceOrZero(char c) => c == '\0' || IsWhitespace(c);
}
