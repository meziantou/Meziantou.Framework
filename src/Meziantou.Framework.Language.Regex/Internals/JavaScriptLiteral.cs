namespace Meziantou.Framework.Language.Regex.Internals;

/// <summary>The three parts of a JavaScript regular-expression literal, <c>/pattern/flags</c>.</summary>
/// <remarks>
/// Splitting happens before parsing because the flags decide how the pattern is read: <c>u</c> and <c>v</c> change
/// what an escape and a character class mean, and there is nowhere else those can come from.
/// </remarks>
/// <param name="LineTerminatorPosition">
/// Where a line terminator was found inside the body, or -1. A literal may not contain one, escaped or not, so finding
/// one means the literal is not closed however much text follows it.
/// </param>
internal sealed record JavaScriptLiteral(int BodyStart, int BodyEnd, int FlagsEnd, RegexPatternOptions Options, bool HasOpeningSlash, bool HasClosingSlash, int LineTerminatorPosition = -1)
{
    /// <summary>Finds the delimiters of a literal and reads its flags.</summary>
    /// <remarks>
    /// The closing delimiter is the first <c>/</c> that is neither escaped nor inside a character class, which is the
    /// same rule a JavaScript tokenizer uses. Text that is not a literal at all is treated as a bare pattern, so
    /// passing one to <c>ParseJavaScriptLiteral</c> still produces a usable tree.
    /// </remarks>
    public static JavaScriptLiteral Split(string text)
    {
        if (text.Length == 0 || text[0] != '/')
            return new JavaScriptLiteral(0, text.Length, text.Length, RegexPatternOptions.None, HasOpeningSlash: false, HasClosingSlash: false);

        // A literal lives on one line: the grammar excludes a line terminator from both an ordinary character and the
        // character after a backslash, so the first one ends the search whatever comes later.
        var lineTerminator = -1;

        var inClass = false;
        var index = 1;
        while (index < text.Length)
        {
            var ch = text[index];
            if (IsLineTerminator(ch))
            {
                lineTerminator = index;
                break;
            }

            if (ch == '\\')
            {
                // The escaped character cannot be a line terminator either, so the backslash must not skip over one.
                if (index + 1 < text.Length && IsLineTerminator(text[index + 1]))
                {
                    lineTerminator = index + 1;
                    break;
                }

                index += 2;
                continue;
            }

            if (ch == '[')
            {
                inClass = true;
            }
            else if (ch == ']')
            {
                inClass = false;
            }
            else if (ch == '/' && !inClass)
            {
                break;
            }

            index++;
        }

        if (lineTerminator >= 0)
            return new JavaScriptLiteral(1, lineTerminator, text.Length, RegexPatternOptions.None, HasOpeningSlash: true, HasClosingSlash: false, lineTerminator);

        if (index >= text.Length)
            return new JavaScriptLiteral(1, text.Length, text.Length, RegexPatternOptions.None, HasOpeningSlash: true, HasClosingSlash: false);

        var flagsStart = index + 1;
        var flagsEnd = flagsStart;
        while (flagsEnd < text.Length && char.IsAsciiLetter(text[flagsEnd]))
        {
            flagsEnd++;
        }

        return new JavaScriptLiteral(1, index, flagsEnd, ReadFlags(text.AsSpan(flagsStart, flagsEnd - flagsStart)), HasOpeningSlash: true, HasClosingSlash: true);
    }

    /// <summary>The four characters ECMAScript counts as a line terminator.</summary>
    private static bool IsLineTerminator(char ch) => ch is '\n' or '\r' or '\u2028' or '\u2029';

    /// <summary>Maps the flag letters onto options. An unknown letter is left to the parser to report.</summary>
    public static RegexPatternOptions ReadFlags(ReadOnlySpan<char> flags)
    {
        var options = RegexPatternOptions.None;
        foreach (var flag in flags)
        {
            options |= flag switch
            {
                'i' => RegexPatternOptions.IgnoreCase,
                'm' => RegexPatternOptions.Multiline,
                's' => RegexPatternOptions.DotAll | RegexPatternOptions.Singleline,
                'u' => RegexPatternOptions.Unicode,
                'v' => RegexPatternOptions.Unicode | RegexPatternOptions.UnicodeSets,
                'g' => RegexPatternOptions.Global,
                'y' => RegexPatternOptions.Sticky,
                'd' => RegexPatternOptions.HasIndices,
                _ => RegexPatternOptions.None,
            };
        }

        return options;
    }
}
