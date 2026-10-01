using Meziantou.Framework.Language.Css.Internals;

namespace Meziantou.Framework.Language.Css;

/// <summary>Reads the CSS kind of the tokens, trivia, and nodes the shared syntax types hand back, and the values of CSS tokens.</summary>
/// <remarks>
/// The shared types carry a kind as a plain number because they are used by every language. These turn that number
/// back into the CSS kind it stands for.
/// </remarks>
public static class CssExtensions
{
    /// <summary>Gets the CSS kind of a node reached through the shared type rather than the CSS one.</summary>
    public static SyntaxKind Kind(this SyntaxNode? node) => (SyntaxKind)(node?.RawKind ?? 0);

    public static SyntaxKind Kind(this SyntaxToken token) => (SyntaxKind)token.RawKind;
    public static SyntaxKind Kind(this SyntaxTrivia trivia) => (SyntaxKind)trivia.RawKind;
    public static SyntaxKind Kind(this SyntaxNodeOrToken nodeOrToken) => (SyntaxKind)nodeOrToken.RawKind;

    public static bool IsKind(this SyntaxNode? node, SyntaxKind kind) => node?.RawKind == (int)kind;
    public static bool IsKind(this SyntaxToken token, SyntaxKind kind) => token.RawKind == (int)kind;
    public static bool IsKind(this SyntaxTrivia trivia, SyntaxKind kind) => trivia.RawKind == (int)kind;
    public static bool IsKind(this SyntaxNodeOrToken nodeOrToken, SyntaxKind kind) => nodeOrToken.RawKind == (int)kind;

    /// <summary>Determines whether the source actually had this optional token.</summary>
    /// <remarks>
    /// An optional slot the source left empty holds the default token, whose kind is <see cref="SyntaxKind.None"/>.
    /// That is not the same as a missing token, which the parser puts in a required slot to stand in for text that
    /// should have been there.
    /// </remarks>
    public static bool IsPresent(this SyntaxToken token) => token.RawKind != (int)SyntaxKind.None;

    /// <summary>Determines whether <paramref name="trivia"/> is a comment.</summary>
    public static bool IsComment(this SyntaxTrivia trivia) => SyntaxFacts.IsComment(trivia.Kind());

    /// <summary>Gets the number a number, percentage, or dimension token holds, or <see langword="null"/> for any other token.</summary>
    /// <remarks>A number too large for a <see cref="double"/> is infinite, as it is for a browser.</remarks>
    public static double? GetNumericValue(this SyntaxToken token) => SyntaxFacts.IsNumeric(token.Kind()) && token.Value is double value ? value : null;

    /// <summary>Determines whether a number, percentage, or dimension token is written as an integer, without a fraction or an exponent.</summary>
    public static bool IsInteger(this SyntaxToken token)
    {
        if (!SyntaxFacts.IsNumeric(token.Kind()))
            return false;

        CssIdentifier.ScanNumber(token.Text, out var isInteger);
        return isInteger;
    }

    /// <summary>Gets the unit of a dimension token, such as <c>px</c>, with its escapes resolved, or <see langword="null"/> for any other token.</summary>
    public static string? GetUnit(this SyntaxToken token)
    {
        if (!token.IsKind(SyntaxKind.DimensionToken))
            return null;

        return CssIdentifier.GetDimensionUnit(token.Text);
    }

    /// <summary>Determines whether a hash token is an ID: whether its name would read as an identifier, as in <c>#main</c> but not <c>#1a</c>.</summary>
    public static bool IsIdHash(this SyntaxToken token) => token.IsKind(SyntaxKind.HashToken) && CssIdentifier.WouldStartIdentSequence(token.Text, 1);

    /// <summary>Gets the range of code points a unicode-range token covers, or <see langword="null"/> for any other token.</summary>
    /// <remarks>The range is returned as written, even when it is not valid, such as when its end is before its start.</remarks>
    public static (int Start, int End)? GetUnicodeRange(this SyntaxToken token)
    {
        if (!token.IsKind(SyntaxKind.UnicodeRangeToken))
            return null;

        var text = token.Text.AsSpan(2);
        var dash = text.IndexOf('-');
        var first = dash < 0 ? text : text[..dash];
        if (first.Contains('?'))
            return (ParseHex(first, '0'), ParseHex(first, 'F'));

        var start = ParseHex(first, '0');
        return dash < 0 ? (start, start) : (start, ParseHex(text[(dash + 1)..], '0'));

        static int ParseHex(ReadOnlySpan<char> digits, char questionMark)
        {
            var value = 0;
            foreach (var c in digits)
            {
                value = (value * 16) + CssIdentifier.HexValue(c == '?' ? questionMark : c);
            }

            return value;
        }
    }

    /// <summary>Carries the annotations of <paramref name="original"/> onto a node built to replace it.</summary>
    /// <remarks>
    /// This is what makes an annotation survive an edit: every <c>Update</c> builds a fresh node and then copies the
    /// marks the old one carried.
    /// </remarks>
    internal static TNode WithAnnotationsFrom<TNode>(this TNode node, CssSyntaxNode original)
        where TNode : CssSyntaxNode
    {
        var annotations = original.GetAnnotations().ToArray();

        return annotations.Length == 0 ? node : node.WithAdditionalAnnotations(annotations);
    }
}
