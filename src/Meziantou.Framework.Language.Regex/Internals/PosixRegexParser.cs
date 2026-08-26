namespace Meziantou.Framework.Language.Regex.Internals;

/// <summary>Parses POSIX basic and extended regular expressions.</summary>
/// <remarks>
/// <para>
/// POSIX is the Perl grammar with almost everything taken away, which the flavor features already express: no inline
/// options, no lookaround, no named groups, no Unicode categories, and, for basic expressions, no alternation and no
/// <c>+</c> or <c>?</c>.
/// </para>
/// <para>
/// What features cannot express is that a basic expression writes its groups and bounds escaped, so a bare <c>(</c>
/// or <c>{</c> is an ordinary character rather than the start of a construct. That is what this parser adds.
/// </para>
/// <para>
/// Their escaped counterparts are read as escapes rather than as grouping: <c>\(a\)</c> parses as an escape, a
/// literal, and an escape. The text round-trips and reports nothing, but the group is not in the tree as a group. A
/// consumer that needs the structure of a basic expression will have to wait for it.
/// </para>
/// </remarks>
internal sealed class PosixRegexParser : PerlStyleRegexParser
{
    public PosixRegexParser(string text, RegexParseOptions parseOptions)
        : base(text, parseOptions)
    {
    }

    private bool DelimitersAreEscaped => Flavor.HasFeature(RegexFlavorFeatures.EscapedGroupDelimiters);

    protected override RegexAtomSyntax ParseAtom(IReadOnlyList<RegexSyntaxTrivia> leadingTrivia)
    {
        if (!DelimitersAreEscaped)
            return base.ParseAtom(leadingTrivia);

        // In a basic expression the roles of "(" and "\(" are swapped, so a bare parenthesis is just a character and
        // an escaped one opens a group.
        if (Scanner.Current is '(' or ')' or '{' or '}')
        {
            var start = Scanner.Position;
            Scanner.Position++;

            return WithOptions(new RegexLiteralSyntax(Scanner.Token(RegexSyntaxKind.LiteralToken, start, leadingTrivia)));
        }

        return base.ParseAtom(leadingTrivia);
    }

    protected override bool IsQuantifierAt(int position)
    {
        if (!DelimitersAreEscaped)
            return base.IsQuantifierAt(position);

        return position < Text.Length && Text[position] == '*';
    }
}
