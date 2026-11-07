namespace Meziantou.Framework.Language.Css.Internals;

/// <summary>Reads the An+B microsyntax of CSS Syntax Level 3, as in <c>:nth-child(2n+1)</c>.</summary>
/// <remarks>
/// The tokenizer splits An+B in surprising places -- <c>2n+1</c> is the dimension <c>2n</c> followed by the number
/// <c>+1</c>, while <c>2n-1</c> is a single dimension whose unit is <c>n-1</c> -- so it is read from the tokens rather
/// than from the text, exactly as the specification describes.
/// </remarks>
internal static class AnPlusB
{
    /// <summary>A token of an An+B expression, with what reading it needs.</summary>
    /// <param name="Kind">The kind of the token.</param>
    /// <param name="Text">The token as written.</param>
    /// <param name="Value">The value of an identifier or a dimension's unit, with its escapes resolved.</param>
    /// <param name="Number">The number of a number or dimension token.</param>
    /// <param name="IsInteger">Whether the number is written as an integer.</param>
    /// <param name="WhitespaceBefore">Whether whitespace separates the token from the one before it.</param>
    public readonly record struct Token(SyntaxKind Kind, string Text, string Value, double Number, bool IsInteger, bool WhitespaceBefore);

    /// <summary>Describes a token for <see cref="TryParse"/>.</summary>
    public static Token CreateToken(SyntaxKind kind, string text, string valueText, object? value, bool whitespaceBefore)
    {
        var number = value is double d ? d : 0;
        var isInteger = false;
        var tokenValue = valueText;
        if (kind is SyntaxKind.NumberToken or SyntaxKind.DimensionToken)
        {
            CssIdentifier.ScanNumber(text, out isInteger);
            if (kind == SyntaxKind.DimensionToken)
            {
                tokenValue = CssIdentifier.GetDimensionUnit(text);
            }
        }

        return new Token(kind, text, tokenValue, number, isInteger, whitespaceBefore);
    }

    public static bool TryParse(ReadOnlySpan<Token> tokens, out int a, out int b)
    {
        a = 0;
        b = 0;
        if (tokens.IsEmpty)
            return false;

        int index;
        var first = tokens[0];
        bool afterN;
        bool afterNDash;
        switch (first.Kind)
        {
            case SyntaxKind.NumberToken:
                if (!first.IsInteger || tokens.Length != 1)
                    return false;

                b = Clamp(first.Number);
                return true;

            case SyntaxKind.DimensionToken:
                if (!first.IsInteger || !TryReadNPart(first.Value, allowLeadingDash: false, out _, out afterN, out afterNDash, out b))
                    return false;

                a = Clamp(first.Number);
                index = 1;
                break;

            case SyntaxKind.IdentToken:
                var value = CssIdentifier.ToAsciiLowerCase(first.Value);
                if (value is "odd" or "even")
                {
                    if (tokens.Length != 1)
                        return false;

                    a = 2;
                    b = value == "odd" ? 1 : 0;
                    return true;
                }

                if (!TryReadNPart(first.Value, allowLeadingDash: true, out var negative, out afterN, out afterNDash, out b))
                    return false;

                a = negative ? -1 : 1;
                index = 1;
                break;

            case SyntaxKind.PlusToken:
                // "+n" is A=1 only when the plus touches the n; "+ n" is not An+B at all.
                if (tokens.Length < 2 || tokens[1].Kind != SyntaxKind.IdentToken || tokens[1].WhitespaceBefore)
                    return false;

                if (!TryReadNPart(tokens[1].Value, allowLeadingDash: false, out _, out afterN, out afterNDash, out b))
                    return false;

                a = 1;
                index = 2;
                break;

            default:
                return false;
        }

        if (afterNDash)
        {
            // "n- 1": the dash is in the unit or the identifier, and the number after it must be unsigned.
            if (index != tokens.Length - 1 || !IsSignlessInteger(tokens[index]))
                return false;

            b = -Clamp(tokens[index].Number);
            return true;
        }

        if (!afterN)
            return index == tokens.Length;

        if (index == tokens.Length)
            return true;

        var next = tokens[index];
        if (next.Kind == SyntaxKind.NumberToken && next.IsInteger && next.Text[0] is '+' or '-')
        {
            if (index != tokens.Length - 1)
                return false;

            b = Clamp(next.Number);
            return true;
        }

        if (next.Kind is SyntaxKind.PlusToken or SyntaxKind.MinusToken)
        {
            if (index != tokens.Length - 2 || !IsSignlessInteger(tokens[index + 1]))
                return false;

            b = Clamp(next.Kind == SyntaxKind.MinusToken ? -tokens[index + 1].Number : tokens[index + 1].Number);
            return true;
        }

        return false;
    }

    /// <summary>Reads the part of an identifier or a unit that holds the <c>n</c>: <c>n</c>, <c>n-</c>, or <c>n-</c> followed by digits.</summary>
    private static bool TryReadNPart(string value, bool allowLeadingDash, out bool negative, out bool afterN, out bool afterNDash, out int b)
    {
        negative = false;
        afterN = false;
        afterNDash = false;
        b = 0;
        var text = value.AsSpan();
        if (allowLeadingDash && text.Length > 0 && text[0] == '-')
        {
            negative = true;
            text = text[1..];
        }

        if (text.Length == 0 || text[0] is not ('n' or 'N'))
            return false;

        if (text.Length == 1)
        {
            afterN = true;
            return true;
        }

        if (text[1] != '-')
            return false;

        if (text.Length == 2)
        {
            afterNDash = true;
            return true;
        }

        var digits = text[2..];
        foreach (var c in digits)
        {
            if (!char.IsAsciiDigit(c))
                return false;
        }

        b = -Clamp(double.Parse(digits, NumberStyles.None, CultureInfo.InvariantCulture));
        return true;
    }

    private static bool IsSignlessInteger(Token token) => token.Kind == SyntaxKind.NumberToken && token.IsInteger && token.Text[0] is not ('+' or '-');

    /// <summary>Converts to an integer, saturating rather than overflowing as browsers do.</summary>
    private static int Clamp(double value) => value switch
    {
        >= int.MaxValue => int.MaxValue,
        <= int.MinValue => int.MinValue,
        _ => (int)value,
    };
}
