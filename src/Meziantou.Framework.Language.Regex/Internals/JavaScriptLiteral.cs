namespace Meziantou.Framework.Language.Regex.Internals;

/// <summary>The three parts of a JavaScript regular-expression literal, <c>/pattern/flags</c>.</summary>
/// <remarks>
/// Splitting happens before parsing because the flags decide how the pattern is read: <c>u</c> and <c>v</c> change
/// what an escape and a character class mean, and there is nowhere else those can come from.
/// </remarks>
internal sealed record JavaScriptLiteral(int BodyStart, int BodyEnd, int FlagsEnd, RegexPatternOptions Options, bool HasOpeningSlash, bool HasClosingSlash)
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

        var inClass = false;
        var index = 1;
        while (index < text.Length)
        {
            var ch = text[index];
            if (ch == '\\')
            {
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
