using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Formatting;

public sealed partial class CssMinifyFormatter
{
    /// <summary>The kind of the text compacted by <see cref="WriteCompacted"/>, which determines the whitespace that is not needed.</summary>
    internal enum CssTextKind
    {
        /// <summary>A selector list: the whitespace around the combinators other than the descendant combinator is not needed.</summary>
        Selector,

        /// <summary>The prelude of an at-rule or the key text of a keyframe: the whitespace after the colons and around the comparisons of the media queries is not needed.</summary>
        Prelude,

        /// <summary>The value of a declaration: the whitespace around the slashes and the multiplications is not needed.</summary>
        Value,
    }

    /// <summary>
    /// Writes <paramref name="text"/> without the whitespace that is not needed: the leading and trailing whitespace, and the
    /// whitespace after a comma or an opening bracket, before a comma or a closing bracket, and around the delimiters that
    /// separate the tokens in the kind of text. The text of the other tokens and the comments between two tokens are written
    /// as is, so the text is parsed to the same tokens.
    /// </summary>
    internal static void WriteCompacted(TextWriter output, string text, CssTextKind kind, bool shortenColors, bool removeLeadingZeros)
    {
        // The list of the tokens is reused by the next calls (each value and each prelude of a style sheet)
        var tokens = s_tokens ?? [];
        s_tokens = null;
        try
        {
            var tokenizer = new CssTokenizer(text);
            while (tokenizer.ConsumeToken() is { Type: not CssTokenType.EndOfFile } token)
            {
                tokens.Add(token);
            }

            WriteCompactedTokens(output, tokenizer.Input, tokens, kind, shortenColors, removeLeadingZeros);
        }
        finally
        {
            tokens.Clear();
            if (tokens.Capacity <= MaxCachedTokenCount)
            {
                s_tokens = tokens;
            }
        }
    }

    private const int MaxCachedTokenCount = 1024;

    [ThreadStatic]
    private static List<CssToken>? s_tokens;

    private static void WriteCompactedTokens(TextWriter output, string input, List<CssToken> tokens, CssTextKind kind, bool shortenColors, bool removeLeadingZeros)
    {
        // The end of the last token written, and its last character
        var position = -1;
        var lastChar = '\0';
        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];

            // A comment between two tokens is kept: without it, the tokens could be parsed as a single token
            if (position >= 0 && token.Start > position)
            {
                output.Write(input.AsSpan(position, token.Start - position));
                lastChar = input[token.Start - 1];
            }

            if (token.Type == CssTokenType.Whitespace)
            {
                position = token.End;
                if (i == 0 || i == tokens.Count - 1)
                    continue;

                // Without the whitespace, "/" followed by "*" would start a comment
                if (CanRemoveWhitespace(tokens[i - 1], tokens[i + 1], kind) && !(lastChar == '/' && input[tokens[i + 1].Start] == '*'))
                    continue;

                output.Write(' ');
                lastChar = ' ';
                continue;
            }

            if (kind == CssTextKind.Value && shortenColors && token.Type == CssTokenType.Function && InfraAscii.EqualsIgnoreAsciiCase(token.Value, "rgb") &&
                TryReadRgbComponents(tokens, i + 1, out var red, out var green, out var blue, out var endIndex))
            {
                WriteHexColor(output, red, green, blue);
                lastChar = '\0';
                i = endIndex;
                position = tokens[endIndex].End;
                continue;
            }

            var tokenText = input.AsSpan(token.Start, token.End - token.Start);
            if (kind == CssTextKind.Value && removeLeadingZeros && token.Type is CssTokenType.Number or CssTokenType.Percentage or CssTokenType.Dimension)
            {
                // "0.5" is written ".5", "-0.5" is written "-.5"
                var signLength = tokenText[0] is '+' or '-' ? 1 : 0;
                if (tokenText.Length > signLength + 2 && tokenText[signLength] == '0' && tokenText[signLength + 1] == '.')
                {
                    output.Write(tokenText[..signLength]);
                    tokenText = tokenText[(signLength + 1)..];
                }
            }

            output.Write(tokenText);
            lastChar = tokenText[^1];
            position = token.End;
        }
    }

    /// <summary>Whether the whitespace between <paramref name="previous"/> and <paramref name="next"/> can be removed without changing the meaning of the text.</summary>
    private static bool CanRemoveWhitespace(CssToken previous, CssToken next, CssTextKind kind)
    {
        if (previous.Type is CssTokenType.Comma or CssTokenType.LeftParenthesis or CssTokenType.Function or CssTokenType.LeftSquareBracket ||
            next.Type is CssTokenType.Comma or CssTokenType.RightParenthesis or CssTokenType.RightSquareBracket)
        {
            return true;
        }

        return kind switch
        {
            // The whitespace is the descendant combinator: it is only removed around the other combinators
            CssTextKind.Selector => IsDelim(previous, ">+~") || IsDelim(next, ">+~"),

            // "and (" is kept: "and(" is a function
            CssTextKind.Prelude => previous.Type == CssTokenType.Colon || IsDelim(previous, "<>=") || IsDelim(next, "<>="),

            // The whitespace around "+" and "-" is needed in calc()
            _ => IsDelim(previous, "/*") || IsDelim(next, "/*"),
        };

        static bool IsDelim(CssToken token, string chars) => token.Type == CssTokenType.Delim && token.Value.Length == 1 && chars.Contains(token.Value[0], StringComparison.Ordinal);
    }

    /// <summary>Reads the components of <c>rgb(r, g, b)</c> when they are integers between 0 and 255, from the token that follows the function token to the closing parenthesis.</summary>
    private static bool TryReadRgbComponents(List<CssToken> tokens, int index, out int red, out int green, out int blue, out int endIndex)
    {
        red = green = blue = endIndex = 0;
        Span<int> components = stackalloc int[3];
        for (var component = 0; component < 3; component++)
        {
            if (component > 0)
            {
                if (!SkipWhitespace(tokens, ref index) || tokens[index].Type != CssTokenType.Comma)
                    return false;

                index++;
            }

            if (!SkipWhitespace(tokens, ref index))
                return false;

            var token = tokens[index];
            if (token.Type != CssTokenType.Number || !token.IsInteger || token.Sign != '\0' || token.NumericValue is < 0 or > 255)
                return false;

            components[component] = (int)token.NumericValue;
            index++;
        }

        if (!SkipWhitespace(tokens, ref index) || tokens[index].Type != CssTokenType.RightParenthesis)
            return false;

        red = components[0];
        green = components[1];
        blue = components[2];
        endIndex = index;
        return true;

        static bool SkipWhitespace(List<CssToken> tokens, ref int index)
        {
            while (index < tokens.Count && tokens[index].Type == CssTokenType.Whitespace)
            {
                index++;
            }

            return index < tokens.Count;
        }
    }

    /// <summary>Writes <c>#rrggbb</c>, or <c>#rgb</c> when each component is written with two identical hexadecimal digits.</summary>
    private static void WriteHexColor(TextWriter output, int red, int green, int blue)
    {
        const string HexDigits = "0123456789abcdef";
        output.Write('#');
        if (red % 17 == 0 && green % 17 == 0 && blue % 17 == 0)
        {
            output.Write(HexDigits[red / 17]);
            output.Write(HexDigits[green / 17]);
            output.Write(HexDigits[blue / 17]);
            return;
        }

        foreach (var component in (ReadOnlySpan<int>)[red, green, blue])
        {
            output.Write(HexDigits[component >> 4]);
            output.Write(HexDigits[component & 0xF]);
        }
    }
}
