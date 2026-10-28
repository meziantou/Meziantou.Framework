using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The functional notations of CSS Color Module Level 4 (sections 5 to 9): <c>rgb()</c>, <c>rgba()</c>, <c>hsl()</c>,
/// <c>hsla()</c>, <c>hwb()</c>, <c>lab()</c>, <c>lch()</c>, <c>oklab()</c> and <c>oklch()</c>, in the modern syntax
/// (components separated by white space, an optional <c>/ alpha</c>, <c>none</c>) and, for <c>rgb()</c> and
/// <c>hsl()</c>, the legacy syntax (components separated by commas).
/// </summary>
/// <remarks>
/// The components are read as the specification resolves them at parsed-value time: percentages are scaled to the
/// reference range of the component, the lightness of CIELAB and Oklab and the chroma are clamped to their range, the
/// saturation of HSL is clamped to 0 and above, the alpha to [0, 1], hues are angles brought to [0, 360), and
/// <c>none</c> is 0. Function names, units and <c>none</c> are ASCII case-insensitive; white space may surround the
/// notation. <c>calc()</c>, relative colors and the <c>color()</c> function are not supported.
/// </remarks>
internal static class CssColorSyntax
{
    private const int MaxTokens = 8;

    public enum ColorFunction
    {
        Rgb,
        Hsl,
        Hwb,
        Lab,
        Lch,
        Oklab,
        Oklch,
    }

    private enum TokenKind : byte
    {
        Number,
        Percentage,
        Angle,
        None,
        Comma,
        Slash,
    }

    /// <summary>
    /// Parses a functional notation. The components are in the units of the color value types: fractions of one for
    /// <c>rgb()</c>, the saturation and lightness of <c>hsl()</c> and the whiteness and blackness of <c>hwb()</c>,
    /// degrees for hues, and the reference ranges of CSS for CIELAB and Oklab (L in [0, 100] and [0, 1]).
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> text, out ColorFunction function, out double first, out double second, out double third, out double alpha)
    {
        function = default;
        first = second = third = 0;
        alpha = 1;
        text = text.Trim(" \t\n\r\f");
        var open = text.IndexOf('(');
        if (open <= 0 || text[^1] != ')')
            return false;

        var name = text[..open];
        bool allowsLegacy;
        if (name.Equals("rgb", StringComparison.OrdinalIgnoreCase) || name.Equals("rgba", StringComparison.OrdinalIgnoreCase))
        {
            function = ColorFunction.Rgb;
            allowsLegacy = true;
        }
        else if (name.Equals("hsl", StringComparison.OrdinalIgnoreCase) || name.Equals("hsla", StringComparison.OrdinalIgnoreCase))
        {
            function = ColorFunction.Hsl;
            allowsLegacy = true;
        }
        else
        {
            allowsLegacy = false;
            if (name.Equals("hwb", StringComparison.OrdinalIgnoreCase))
            {
                function = ColorFunction.Hwb;
            }
            else if (name.Equals("lab", StringComparison.OrdinalIgnoreCase))
            {
                function = ColorFunction.Lab;
            }
            else if (name.Equals("lch", StringComparison.OrdinalIgnoreCase))
            {
                function = ColorFunction.Lch;
            }
            else if (name.Equals("oklab", StringComparison.OrdinalIgnoreCase))
            {
                function = ColorFunction.Oklab;
            }
            else if (name.Equals("oklch", StringComparison.OrdinalIgnoreCase))
            {
                function = ColorFunction.Oklch;
            }
            else
            {
                return false;
            }
        }

        Span<Token> tokens = stackalloc Token[MaxTokens];
        if (!TryTokenize(text[(open + 1)..^1], tokens, out var count))
            return false;

        tokens = tokens[..count];
        Token alphaToken;
        var legacy = count >= 2 && tokens[1].Kind == TokenKind.Comma;
        if (legacy)
        {
            // <c1> , <c2> , <c3> [, <alpha>]: no 'none', and the components of rgb() are all numbers or all percentages
            if (!allowsLegacy || count is not (5 or 7) || tokens[3].Kind != TokenKind.Comma || (count == 7 && tokens[5].Kind != TokenKind.Comma))
                return false;

            alphaToken = count == 7 ? tokens[6] : new Token(TokenKind.Number, 1);
            if (tokens[0].Kind == TokenKind.None || tokens[2].Kind == TokenKind.None || tokens[4].Kind == TokenKind.None || alphaToken.Kind == TokenKind.None)
                return false;

            if (function == ColorFunction.Rgb && (tokens[0].Kind != tokens[2].Kind || tokens[2].Kind != tokens[4].Kind))
                return false;

            // The saturation and lightness of the legacy hsl() are percentages
            if (function == ColorFunction.Hsl && (tokens[2].Kind != TokenKind.Percentage || tokens[4].Kind != TokenKind.Percentage))
                return false;

            tokens[1] = tokens[2];
            tokens[2] = tokens[4];
            tokens = tokens[..3];
        }
        else
        {
            // <c1> <c2> <c3> [/ <alpha>]
            if (count is not (3 or 5) || (count == 5 && tokens[3].Kind != TokenKind.Slash))
                return false;

            alphaToken = count == 5 ? tokens[4] : new Token(TokenKind.Number, 1);
            tokens = tokens[..3];
        }

        if (!TryGetAlpha(alphaToken, out alpha))
            return false;

        switch (function)
        {
            case ColorFunction.Rgb:
                return TryGetRgbComponent(tokens[0], out first) && TryGetRgbComponent(tokens[1], out second) && TryGetRgbComponent(tokens[2], out third);

            case ColorFunction.Hsl:
                if (!TryGetHue(tokens[0], out first) || !TryGetScalar(tokens[1], 1.0 / 100, 1.0 / 100, out second) || !TryGetScalar(tokens[2], 1.0 / 100, 1.0 / 100, out third))
                    return false;

                // CSS Color 4 section 7.1: a negative saturation is clamped to 0
                second = Math.Max(second, 0);
                return true;

            case ColorFunction.Hwb:
                return TryGetHue(tokens[0], out first) && TryGetScalar(tokens[1], 1.0 / 100, 1.0 / 100, out second) && TryGetScalar(tokens[2], 1.0 / 100, 1.0 / 100, out third);

            case ColorFunction.Lab:
                if (!TryGetScalar(tokens[0], 1, 1, out first) || !TryGetScalar(tokens[1], 1, 1.25, out second) || !TryGetScalar(tokens[2], 1, 1.25, out third))
                    return false;

                first = Math.Clamp(first, 0, 100);
                return true;

            case ColorFunction.Lch:
                if (!TryGetScalar(tokens[0], 1, 1, out first) || !TryGetScalar(tokens[1], 1, 1.5, out second) || !TryGetHue(tokens[2], out third))
                    return false;

                first = Math.Clamp(first, 0, 100);
                second = Math.Max(second, 0);
                return true;

            case ColorFunction.Oklab:
                if (!TryGetScalar(tokens[0], 1, 1.0 / 100, out first) || !TryGetScalar(tokens[1], 1, 0.4 / 100, out second) || !TryGetScalar(tokens[2], 1, 0.4 / 100, out third))
                    return false;

                first = Math.Clamp(first, 0, 1);
                return true;

            default:
                if (!TryGetScalar(tokens[0], 1, 1.0 / 100, out first) || !TryGetScalar(tokens[1], 1, 0.4 / 100, out second) || !TryGetHue(tokens[2], out third))
                    return false;

                first = Math.Clamp(first, 0, 1);
                second = Math.Max(second, 0);
                return true;
        }
    }

    /// <summary>Formats a functional notation of the modern syntax: <c>name(first second third)</c>, with <c> / alpha</c> when the alpha is not 1.</summary>
    /// <param name="name">The name of the function.</param>
    /// <param name="first">The first component.</param>
    /// <param name="second">The second component.</param>
    /// <param name="third">The third component.</param>
    /// <param name="alpha">The alpha, in [0, 1].</param>
    /// <param name="percentages">Whether the second and third components are fractions of one written as percentages.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="alpha"/> is not in [0, 1].</exception>
    public static string Format(string name, double first, double second, double third, double alpha, bool percentages)
    {
        if (!(alpha >= 0 && alpha <= 1))
            throw new ArgumentOutOfRangeException(nameof(alpha), alpha, "The alpha must be in [0, 1].");

        var unit = percentages ? "%" : "";
        if (percentages)
        {
            second *= 100;
            third *= 100;
        }

        return alpha == 1
            ? string.Create(CultureInfo.InvariantCulture, $"{name}({Number(first)} {Number(second)}{unit} {Number(third)}{unit})")
            : string.Create(CultureInfo.InvariantCulture, $"{name}({Number(first)} {Number(second)}{unit} {Number(third)}{unit} / {Number(alpha)})");

        // The shortest text that reads back the same number; negative zero is zero. A number that is not finite cannot be
        // written in CSS without calc()
        static string Number(double value)
        {
            if (!double.IsFinite(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "A component that is not a finite number has no CSS notation.");

            return ColorValueMath.Canonical(value).ToString(CultureInfo.InvariantCulture);
        }
    }

    private static bool TryGetAlpha(Token token, out double alpha)
    {
        if (!TryGetScalar(token, 1, 1.0 / 100, out alpha))
            return false;

        alpha = Math.Clamp(alpha, 0, 1);
        return true;
    }

    private static bool TryGetRgbComponent(Token token, out double value)
    {
        if (!TryGetScalar(token, 1.0 / 255, 1.0 / 100, out value))
            return false;

        value = Math.Clamp(value, 0, 1);
        return true;
    }

    // A number or a percentage, each scaled; 'none' is 0
    private static bool TryGetScalar(Token token, double numberScale, double percentageScale, out double value)
    {
        value = token.Kind switch
        {
            TokenKind.Number => token.Value * numberScale,
            TokenKind.Percentage => token.Value * percentageScale,
            _ => 0,
        };
        return token.Kind is TokenKind.Number or TokenKind.Percentage or TokenKind.None;
    }

    // A number of degrees or an angle; 'none' is 0
    private static bool TryGetHue(Token token, out double value)
    {
        value = token.Kind is TokenKind.Number or TokenKind.Angle ? ColorValueMath.NormalizeHue(token.Value) : 0;
        return token.Kind is TokenKind.Number or TokenKind.Angle or TokenKind.None;
    }

    private static bool TryTokenize(ReadOnlySpan<char> text, Span<Token> tokens, out int count)
    {
        count = 0;
        var index = 0;
        while (true)
        {
            while (index < text.Length && IsWhiteSpace(text[index]))
            {
                index++;
            }

            if (index == text.Length)
                return true;

            if (count == tokens.Length)
                return false;

            var c = text[index];
            if (c is ',' or '/')
            {
                tokens[count++] = new Token(c == ',' ? TokenKind.Comma : TokenKind.Slash, 0);
                index++;
                continue;
            }

            if (char.IsAsciiLetter(c))
            {
                var length = CountLetters(text[index..]);
                if (!text.Slice(index, length).Equals("none", StringComparison.OrdinalIgnoreCase))
                    return false;

                tokens[count++] = new Token(TokenKind.None, 0);
                index += length;
                continue;
            }

            if (!TryReadNumber(text[index..], out var value, out var consumed))
                return false;

            index += consumed;
            if (index < text.Length && text[index] == '%')
            {
                tokens[count++] = new Token(TokenKind.Percentage, value);
                index++;
                continue;
            }

            if (index < text.Length && char.IsAsciiLetter(text[index]))
            {
                // CSS Values 4 section 7.1: deg, grad (400 per turn), rad and turn
                var length = CountLetters(text[index..]);
                var unit = text.Slice(index, length);
                double degrees;
                if (unit.Equals("deg", StringComparison.OrdinalIgnoreCase))
                {
                    degrees = value;
                }
                else if (unit.Equals("grad", StringComparison.OrdinalIgnoreCase))
                {
                    degrees = value * 360 / 400;
                }
                else if (unit.Equals("rad", StringComparison.OrdinalIgnoreCase))
                {
                    degrees = value * 180 / Math.PI;
                }
                else if (unit.Equals("turn", StringComparison.OrdinalIgnoreCase))
                {
                    degrees = value * 360;
                }
                else
                {
                    return false;
                }

                tokens[count++] = new Token(TokenKind.Angle, degrees);
                index += length;
                continue;
            }

            tokens[count++] = new Token(TokenKind.Number, value);
        }
    }

    // CSS Syntax 3 section 4.3.12: [+-]? (digits ('.' digits)? | '.' digits) ([eE] [+-]? digits)?
    private static bool TryReadNumber(ReadOnlySpan<char> text, out double value, out int consumed)
    {
        value = 0;
        var index = 0;
        if (index < text.Length && text[index] is '+' or '-')
        {
            index++;
        }

        var integerDigits = CountDigits(text[index..]);
        index += integerDigits;
        var fractionDigits = 0;
        if (index + 1 < text.Length && text[index] == '.' && char.IsAsciiDigit(text[index + 1]))
        {
            fractionDigits = CountDigits(text[(index + 1)..]);
            index += 1 + fractionDigits;
        }

        consumed = index;
        if (integerDigits == 0 && fractionDigits == 0)
            return false;

        if (index < text.Length && text[index] is 'e' or 'E')
        {
            var exponent = index + 1;
            if (exponent < text.Length && text[exponent] is '+' or '-')
            {
                exponent++;
            }

            var exponentDigits = exponent < text.Length ? CountDigits(text[exponent..]) : 0;
            if (exponentDigits > 0)
            {
                consumed = exponent + exponentDigits;
            }
        }

        return double.TryParse(text[..consumed], NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out value) && double.IsFinite(value);
    }

    // A loop: MemoryExtensions.IndexOfAnyExceptInRange allocates on each call for char in a build without optimizations
    private static int CountDigits(ReadOnlySpan<char> text)
    {
        var index = 0;
        while (index < text.Length && char.IsAsciiDigit(text[index]))
        {
            index++;
        }

        return index;
    }

    private static int CountLetters(ReadOnlySpan<char> text)
    {
        var index = 0;
        while (index < text.Length && char.IsAsciiLetter(text[index]))
        {
            index++;
        }

        return index;
    }

    private static bool IsWhiteSpace(char c) => c is ' ' or '\t' or '\n' or '\r' or '\f';

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Token(TokenKind Kind, double Value);
}
