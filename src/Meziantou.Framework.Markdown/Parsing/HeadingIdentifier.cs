using System.Buffers;
using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Markdown.Parsing;

/// <summary>Makes the identifier of a heading from its text, as Meziantou.Framework.Markdown 1.x (Markdig) does.</summary>
internal static class HeadingIdentifier
{
    // The generic attributes of the line are not part of the text of the heading
    public static string GetLabelWithoutAttributes(string text, int start, int end)
    {
        if (text.AsSpan(start, end - start).IndexOf('{') < 0)
        {
            return text[start..end];
        }

        var label = new StringBuilder(end - start);
        var copyStart = start;
        var index = start;
        while (index < end)
        {
            var c = text[index];
            if (c == '\\' && index + 1 < end && Scanners.IsEscapable(text[index + 1]))
            {
                // An escaped character is never the start of attributes
                index += 2;
            }
            else if (c == '`')
            {
                index = SkipCodeSpan(text, index, end);
            }
            else if (c == '{' && AttributesScanner.TryParse(text, index, end, out _) is >= 0 and var attributesEnd)
            {
                // The whitespace around the attributes is collapsed when the label is normalized
                label.Append(text, copyStart, index - copyStart);
                index = attributesEnd;
                copyStart = index;
            }
            else
            {
                index++;
            }
        }

        label.Append(text, copyStart, end - copyStart);
        return label.ToString();
    }

    // Skips a code span that starts at the backtick, or only its opening backticks when it is not closed
    private static int SkipCodeSpan(string text, int start, int end)
    {
        var index = start;
        while (index < end && text[index] == '`')
        {
            index++;
        }

        var openingLength = index - start;
        while (index < end)
        {
            if (text[index] != '`')
            {
                index++;
                continue;
            }

            var closingStart = index;
            while (index < end && text[index] == '`')
            {
                index++;
            }

            if (index - closingStart == openingLength)
            {
                return index;
            }
        }

        return start + openingLength;
    }

    public static string Create(string text, MarkdownAutoIdentifierOptions options) => options.HasFlag(MarkdownAutoIdentifierOptions.GitHub)
        ? CreateAsGitHub(text)
        : Create(text, options.HasFlag(MarkdownAutoIdentifierOptions.AllowOnlyAscii));

    // Following https://github.com/jch/html-pipeline/blob/master/lib/html/pipeline/toc_filter.rb, which keeps the word characters
    // of Ruby (letters, marks, decimal and letter numbers, connector punctuation), the hyphens and the spaces, as github-slugger
    // does. The characters are code points, so that a letter outside the BMP is kept, and a lone surrogate is removed.
    private static string CreateAsGitHub(string text)
    {
        var builder = new StringBuilder(text.Length);
        Span<char> buffer = stackalloc char[2];
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value == ' ')
            {
                builder.Append('-');
            }
            else if (rune.Value == '-' || IsWordCharacter(rune))
            {
                var length = Rune.ToLowerInvariant(rune).EncodeToUtf16(buffer);
                builder.Append(buffer[..length]);
            }
        }

        return builder.ToString();

        // EnumerateRunes reads a lone surrogate as U+FFFD, which is a symbol
        static bool IsWordCharacter(Rune rune) => Rune.GetUnicodeCategory(rune) is UnicodeCategory.UppercaseLetter or UnicodeCategory.LowercaseLetter or UnicodeCategory.TitlecaseLetter or UnicodeCategory.ModifierLetter or UnicodeCategory.OtherLetter
            or UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark
            or UnicodeCategory.DecimalDigitNumber or UnicodeCategory.LetterNumber
            or UnicodeCategory.ConnectorPunctuation;
    }

    // The identifier starts at the first letter, keeps the letters and the digits, and writes a run of whitespace as a hyphen. The
    // hyphen, the underscore and the dot are kept once in a row, and removed at the end.
    private static string Create(string headingText, bool allowOnlyAscii)
    {
        var builder = new StringBuilder(headingText.Length);
        var hasLetter = false;
        var previousIsSpace = false;
        var text = allowOnlyAscii ? NormalizeFormD(headingText) : headingText;
        foreach (var character in text)
        {
            var c = character;

            // The accents that the normalization separated from their letter
            if (allowOnlyAscii && CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            var normalized = !allowOnlyAscii ? null : GetGermanOrScandinavianLetters(c) ?? CharNormalizer.ConvertToAscii(c);
            for (var j = 0; j < (normalized is null or "" ? 1 : normalized.Length); j++)
            {
                if (normalized is { Length: > 0 })
                {
                    c = normalized[j];
                }

                if (char.IsLetter(c))
                {
                    if (allowOnlyAscii && (c < ' ' || c >= 127))
                    {
                        continue;
                    }

                    builder.Append(char.IsUpper(c) ? char.ToLowerInvariant(c) : c);
                    hasLetter = true;
                    previousIsSpace = false;
                }
                else if (hasLetter)
                {
                    if (IsReservedPunctuation(c))
                    {
                        if (previousIsSpace)
                        {
                            builder.Length--;
                        }

                        if (builder[^1] != c)
                        {
                            builder.Append(c);
                        }

                        previousIsSpace = false;
                    }
                    else if (char.IsAsciiDigit(c))
                    {
                        builder.Append(c);
                        previousIsSpace = false;
                    }
                    else if (!previousIsSpace && IsWhitespace(c))
                    {
                        if (!IsReservedPunctuation(builder[^1]))
                        {
                            builder.Append('-');
                        }

                        previousIsSpace = true;
                    }
                }
            }
        }

        while (builder.Length > 0 && IsReservedPunctuation(builder[^1]))
        {
            builder.Length--;
        }

        return builder.ToString();
    }

    // String.Normalize throws on invalid code points, such as a lone surrogate or a noncharacter like U+FFFE. An identifier never
    // keeps them, so they are removed first.
    private static string NormalizeFormD(string text)
    {
        var builder = new StringBuilder(text.Length);
        var remaining = text.AsSpan();
        while (!remaining.IsEmpty)
        {
            var status = Rune.DecodeFromUtf16(remaining, out var rune, out var length);
            if (status == OperationStatus.Done && !IsNonCharacter(rune.Value))
            {
                builder.Append(remaining[..length]);
            }

            remaining = remaining[length..];
        }

        return builder.ToString().Normalize(NormalizationForm.FormD);

        static bool IsNonCharacter(int value) => (value & 0xFFFE) == 0xFFFE || value is >= 0xFDD0 and <= 0xFDEF;
    }

    // German ß, Norwegian and Danish æ and ø, and Icelandic þ and ð do not decompose. The German umlauts and å do: the text is
    // normalized first, so they are already a letter followed by an accent, which is removed (ü becomes u).
    private static string? GetGermanOrScandinavianLetters(char c) => c switch
    {
        'ß' => "ss",
        'æ' => "ae",
        'ø' => "oe",
        'Æ' => "Ae",
        'Ø' => "Oe",
        'þ' => "th",
        'Þ' => "Th",
        'ð' => "d",
        'Ð' => "D",
        _ => null,
    };

    private static bool IsReservedPunctuation(char c) => c is '_' or '-' or '.';

    private static bool IsWhitespace(char c) => c is ' ' or '\t' or '\n' or '\f' or '\r' || (c >= '\u00A0' && CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.SpaceSeparator);
}
