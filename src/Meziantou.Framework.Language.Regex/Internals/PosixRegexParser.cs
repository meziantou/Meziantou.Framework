namespace Meziantou.Framework.Language.Regex.Internals;

/// <summary>Parses POSIX basic and extended regular expressions.</summary>
/// <remarks>
/// <para>
/// POSIX is the Perl grammar with almost everything taken away, which the flavor features already express: no inline
/// options, no lookaround, no named groups, and no Unicode categories.
/// </para>
/// <para>
/// What features cannot express is that a basic expression spells its delimiters with a backslash. <c>\(</c> opens a
/// group, <c>\{</c> opens a bound, <c>\|</c> separates branches, and the bare characters are ordinary text -- the
/// reverse of every other flavor. The grammar skeleton asks for each delimiter by length rather than matching a
/// character, so overriding those is all it takes.
/// </para>
/// <para>
/// The other basic-expression rule is positional: <c>^</c> is an anchor only where a branch starts, <c>$</c> only
/// where one ends, and a <c>*</c> with nothing before it is an ordinary character rather than a quantifier with
/// nothing to repeat.
/// </para>
/// </remarks>
internal sealed class PosixRegexParser : PerlStyleRegexParser
{
    public PosixRegexParser(string text, RegexParseOptions parseOptions)
        : base(text, parseOptions)
    {
    }

    private bool DelimitersAreEscaped => Flavor.HasFeature(RegexFlavorFeatures.EscapedGroupDelimiters);

    /// <summary>
    /// POSIX has no character escapes. The shorthand classes it does have -- <c>\w</c>, <c>\s</c>, <c>\b</c> and
    /// their negations -- are GNU extensions handled before this, and everything else after a backslash is just the
    /// character itself.
    /// </summary>
    protected override bool RecognizesPerlCharacterEscapes => false;

    protected override int AlternationSeparatorLength(int position) =>
        DelimitersAreEscaped ? EscapedLength(position, '|') : base.AlternationSeparatorLength(position);

    protected override int GroupOpenLength(int position) =>
        DelimitersAreEscaped ? EscapedLength(position, '(') : base.GroupOpenLength(position);

    protected override int GroupCloseLength(int position) =>
        DelimitersAreEscaped ? EscapedLength(position, ')') : base.GroupCloseLength(position);

    protected override int BoundOpenLength(int position) =>
        DelimitersAreEscaped ? EscapedLength(position, '{') : base.BoundOpenLength(position);

    protected override int BoundCloseLength(int position) =>
        DelimitersAreEscaped ? EscapedLength(position, '}') : base.BoundCloseLength(position);

    /// <summary>Returns 2 when <paramref name="expected"/> appears escaped at <paramref name="position"/>, else 0.</summary>
    private int EscapedLength(int position, char expected) =>
        position + 1 < Text.Length && Text[position] == '\\' && Text[position + 1] == expected ? 2 : 0;

    protected override bool IsQuantifierAt(int position)
    {
        if (!DelimitersAreEscaped)
            return base.IsQuantifierAt(position);

        // A basic expression has no bare "+" or "?"; GNU spells them escaped, and the bound is "\{…\}".
        if (BoundOpenLength(position) > 0)
            return IsWellFormedBoundAt(position);

        return SimpleQuantifierLength(position, out _) > 0;
    }

    protected override int SimpleQuantifierLength(int position, out char operatorCharacter)
    {
        if (!DelimitersAreEscaped)
            return base.SimpleQuantifierLength(position, out operatorCharacter);

        if (position < Text.Length && Text[position] == '*')
        {
            operatorCharacter = '*';

            return 1;
        }

        // GNU spells the other two escaped, because a basic expression has no bare "+" or "?".
        foreach (var candidate in (ReadOnlySpan<char>)['+', '?'])
        {
            if (EscapedLength(position, candidate) > 0)
            {
                operatorCharacter = candidate;

                return 2;
            }
        }

        operatorCharacter = '\0';

        return 0;
    }

    protected override RegexAtomSyntax ParseAtom(IReadOnlyList<RegexSyntaxTrivia> leadingTrivia)
    {
        if (!DelimitersAreEscaped)
            return base.ParseAtom(leadingTrivia);

        var start = Scanner.Position;

        // The delimiters are the escaped spellings, so the bare characters are ordinary text.
        if (Scanner.Current is '(' or ')' or '{' or '}' or '|' or '+' or '?')
            return ReadLiteral(leadingTrivia);

        // "*" is a quantifier only when something precedes it; at the start of a branch it matches an asterisk.
        if (Scanner.Current == '*' && IsAtSequenceStart)
            return ReadLiteral(leadingTrivia);

        // "^" asserts only where a branch begins, and "$" only where one ends. Elsewhere they are characters.
        if (Scanner.Current == '^' && !IsAtSequenceStart)
            return ReadLiteral(leadingTrivia);

        if (Scanner.Current == '$' && !IsAtBranchEnd(start + 1))
            return ReadLiteral(leadingTrivia);

        return base.ParseAtom(leadingTrivia);
    }

    /// <summary>Whether nothing but the end of a branch follows <paramref name="position"/>.</summary>
    private bool IsAtBranchEnd(int position) =>
        position >= Text.Length || AlternationSeparatorLength(position) > 0 || GroupCloseLength(position) > 0;

    private RegexLiteralSyntax ReadLiteral(IReadOnlyList<RegexSyntaxTrivia> leadingTrivia)
    {
        var start = Scanner.Position;
        Scanner.Position++;

        return WithOptions(new RegexLiteralSyntax(Scanner.Token(RegexSyntaxKind.LiteralToken, start, leadingTrivia)));
    }
}
