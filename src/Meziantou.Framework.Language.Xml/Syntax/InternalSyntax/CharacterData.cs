using System.Buffers;
using System.Text;

namespace Meziantou.Framework.Language.Xml.Syntax.InternalSyntax;

/// <summary>Reads character data the way an XML processor does.</summary>
/// <remarks>
/// A character reference and one of the five predefined entities are replaced by what they stand for, and line
/// breaks are normalized to a line feed (XML 1.0 §2.11). An attribute value is normalized further, every whitespace
/// character becoming a space (§3.3.3). Any other entity is left as it was written, because resolving it would mean
/// reading the DTD.
/// </remarks>
internal static class CharacterData
{
    private static readonly SearchValues<char> TextSpecialCharacters = SearchValues.Create("&\r]<");
    private static readonly SearchValues<char> AttributeSpecialCharacters = SearchValues.Create("&\r\n\t<");

    private static readonly SearchValues<char> InvalidCharacters = SearchValues.Create(CreateInvalidCharacters());

    /// <summary>Decodes the character data <paramref name="text"/> holds, without reporting anything.</summary>
    public static string Decode(string text, bool isAttribute) => Read(text, 0, text.Length, isAttribute, reporter: null);

    /// <summary>Decodes <paramref name="text"/> from <paramref name="start"/> to <paramref name="end"/>, reporting what is wrong with it.</summary>
    public static string Read(string text, int start, int end, bool isAttribute, ICharacterDataReporter? reporter)
    {
        var span = text.AsSpan(start, end - start);
        var firstSpecial = span.IndexOfAny(isAttribute ? AttributeSpecialCharacters : TextSpecialCharacters);
        if (firstSpecial < 0)
            return text.Substring(start, end - start);

        var builder = new StringBuilder(end - start);
        builder.Append(span[..firstSpecial]);

        var index = start + firstSpecial;
        while (index < end)
        {
            var current = text[index];
            switch (current)
            {
                case '&':
                    index = ReadReference(text, index, end, builder, reporter);
                    continue;

                case '\r':
                    builder.Append(isAttribute ? ' ' : '\n');
                    index += index + 1 < end && text[index + 1] == '\n' ? 2 : 1;
                    continue;

                case '\n' or '\t' when isAttribute:
                    builder.Append(' ');
                    break;

                case '<' when isAttribute:
                    reporter?.Report(index, 1, XmlDiagnosticDescriptors.LessThanInAttributeValue);
                    builder.Append(current);
                    break;

                case ']' when !isAttribute && index + 2 < end && text[index + 1] == ']' && text[index + 2] == '>':
                    reporter?.Report(index, 3, XmlDiagnosticDescriptors.CDataEndInText);
                    builder.Append("]]>");
                    index += 3;
                    continue;

                default:
                    builder.Append(current);
                    break;
            }

            index++;
        }

        return builder.ToString();
    }

    /// <summary>Determines whether <paramref name="value"/> matches the <c>Char</c> production of XML 1.0.</summary>
    public static bool IsXmlCharacter(int value) => value switch
    {
        0x9 or 0xA or 0xD => true,
        >= 0x20 and <= 0xD7FF => true,
        >= 0xE000 and <= 0xFFFD => true,
        >= 0x10000 and <= 0x10FFFF => true,
        _ => false,
    };

    /// <summary>Returns the index of the first UTF-16 unit that is not part of an XML character, or -1.</summary>
    /// <remarks>A surrogate is only invalid when it is not half of a pair, which the caller confirms.</remarks>
    public static int IndexOfPossiblyInvalidCharacter(ReadOnlySpan<char> text) => text.IndexOfAny(InvalidCharacters);

    /// <summary>Reads the reference starting at the ampersand at <paramref name="index"/>, and returns where reading resumes.</summary>
    private static int ReadReference(string text, int index, int end, StringBuilder builder, ICharacterDataReporter? reporter)
    {
        if (index + 1 < end && text[index + 1] == '#')
            return ReadCharacterReference(text, index, end, builder, reporter);

        var nameEnd = index + 1;
        if (nameEnd < end && TryReadScalar(text, nameEnd, end, out var first, out var firstLength) && SyntaxFacts.IsNameStartCharacter(first))
        {
            nameEnd += firstLength;
            while (nameEnd < end && TryReadScalar(text, nameEnd, end, out var scalar, out var length) && SyntaxFacts.IsNameCharacter(scalar))
            {
                nameEnd += length;
            }
        }

        if (nameEnd == index + 1 || nameEnd >= end || text[nameEnd] != ';')
        {
            // Not a reference at all, so the ampersand stands for itself and what follows it is read as usual.
            reporter?.Report(index, 1, XmlDiagnosticDescriptors.UnescapedAmpersand);
            builder.Append('&');

            return index + 1;
        }

        var name = text[(index + 1)..nameEnd];
        var replacement = name switch
        {
            "lt" => "<",
            "gt" => ">",
            "amp" => "&",
            "apos" => "'",
            "quot" => "\"",
            _ => null,
        };

        if (replacement is null)
        {
            if (reporter is not null && !reporter.IsEntityDeclared(name))
            {
                reporter.Report(index, nameEnd + 1 - index, XmlDiagnosticDescriptors.UndeclaredEntity, name);
            }

            builder.Append(text, index, nameEnd + 1 - index);
        }
        else
        {
            builder.Append(replacement);
        }

        return nameEnd + 1;
    }

    private static int ReadCharacterReference(string text, int index, int end, StringBuilder builder, ICharacterDataReporter? reporter)
    {
        var position = index + 2;
        var isHexadecimal = position < end && text[position] == 'x';
        if (isHexadecimal)
        {
            position++;
        }

        var digitsStart = position;
        var value = 0;
        var overflow = false;
        while (position < end && TryGetDigit(text[position], isHexadecimal, out var digit))
        {
            value = (value * (isHexadecimal ? 16 : 10)) + digit;
            if (value > 0x10FFFF)
            {
                // Clamping keeps the arithmetic from overflowing; the value is out of range whatever the rest says.
                overflow = true;
                value = 0x110000;
            }

            position++;
        }

        if (position == digitsStart || position >= end || text[position] != ';')
        {
            var length = Math.Min(position + 1, end) - index;
            reporter?.Report(index, length, XmlDiagnosticDescriptors.MalformedCharacterReference, text.Substring(index, length));
            builder.Append('&');

            return index + 1;
        }

        position++;
        if (overflow || !IsXmlCharacter(value))
        {
            reporter?.Report(index, position - index, XmlDiagnosticDescriptors.InvalidCharacterReference, text[index..position]);
            builder.Append(text, index, position - index);
        }
        else
        {
            builder.Append(char.ConvertFromUtf32(value));
        }

        return position;
    }

    private static bool TryGetDigit(char value, bool isHexadecimal, out int digit)
    {
        digit = value switch
        {
            >= '0' and <= '9' => value - '0',
            >= 'a' and <= 'f' when isHexadecimal => value - 'a' + 10,
            >= 'A' and <= 'F' when isHexadecimal => value - 'A' + 10,
            _ => -1,
        };

        return digit >= 0;
    }

    /// <summary>Reads the scalar value at <paramref name="position"/>, and how many UTF-16 units it took.</summary>
    /// <remarks>A lone surrogate is not a scalar value at all, so it can be no part of a name.</remarks>
    public static bool TryReadScalar(string text, int position, int end, out Rune scalar, out int length)
    {
        if (position >= end)
        {
            scalar = default;
            length = 0;

            return false;
        }

        var first = text[position];
        if (!char.IsSurrogate(first))
        {
            scalar = new Rune(first);
            length = 1;

            return true;
        }

        if (position + 1 < end && Rune.TryCreate(first, text[position + 1], out scalar))
        {
            length = 2;

            return true;
        }

        scalar = default;
        length = 0;

        return false;
    }

    private static string CreateInvalidCharacters()
    {
        var builder = new StringBuilder();
        for (var value = 0; value < 0x20; value++)
        {
            if (value is not (0x9 or 0xA or 0xD))
            {
                builder.Append((char)value);
            }
        }

        for (var value = 0xD800; value <= 0xDFFF; value++)
        {
            builder.Append((char)value);
        }

        builder.Append('\uFFFE').Append('\uFFFF');

        return builder.ToString();
    }
}
