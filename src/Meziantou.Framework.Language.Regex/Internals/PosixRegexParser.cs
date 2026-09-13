using Meziantou.Framework.Language.InternalSyntax;
using Meziantou.Framework.Language.Regex.Internals;
using ScannedToken = Meziantou.Framework.Language.InternalSyntax.SyntaxToken;

namespace Meziantou.Framework.Language.Regex.Syntax.InternalSyntax;

/// <summary>Parses POSIX basic and extended regular expressions.</summary>
/// <remarks>
/// <para>
/// POSIX is the Perl grammar with almost everything taken away, which the dialect features already express: no inline
/// options, no lookaround, no named groups, and no Unicode categories.
/// </para>
/// <para>
/// What features cannot express is that a basic expression spells its delimiters with a backslash. <c>\(</c> opens a
/// group, <c>\{</c> opens a bound, <c>\|</c> separates branches, and the bare characters are ordinary text -- the
/// reverse of every other dialect. The grammar skeleton asks for each delimiter by length rather than matching a
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
    public PosixRegexParser(SourceText source, RegexParseOptions parseOptions)
        : base(source, parseOptions)
    {
    }

    private bool DelimitersAreEscaped => Dialect.HasFeature(RegexDialectFeatures.EscapedGroupDelimiters);

    /// <summary>
    /// POSIX has no character escapes. The shorthand classes it does have -- <c>\w</c>, <c>\s</c>, <c>\b</c> and
    /// their negations -- are GNU extensions handled before this, and everything else after a backslash is just the
    /// character itself.
    /// </summary>
    protected override bool RecognizesPerlCharacterEscapes => false;

    /// <summary>A backslash is an ordinary character inside a bracket expression, so <c>[\]]</c> is <c>[\]</c> then <c>]</c>.</summary>
    protected override bool BackslashIsLiteralInClass => true;

    protected override bool RejectsDashAfterRange => true;

    /// <summary>A class, as in <c>[[:alpha:]-z]</c>, cannot start a range.</summary>
    protected override bool ReportsShorthandClassAsRangeStart => true;

    protected override bool AllowsOmittedMinimumBound => true;

    /// <summary>The GNU implementation's <c>RE_DUP_MAX</c>.</summary>
    protected override long? MaxBoundValue => 32767;

    /// <summary>The GNU shorthand classes are <c>\w</c> and <c>\s</c> and their negations; <c>\d</c> is just a "d".</summary>
    protected override bool IsShorthandClassLetter(char letter) => letter is 's' or 'S' or 'w' or 'W';

    /// <summary>
    /// An extended expression may stack quantifiers freely. A basic one may stack only the GNU <c>\+</c> and <c>\?</c>:
    /// a <c>*</c> or a bound after another quantifier is an error there.
    /// </summary>
    protected override bool AllowsStackedQuantifier(RegexQuantifierSyntax quantifier) =>
        !DelimitersAreEscaped || quantifier is RegexSimpleQuantifierSyntax && !quantifier.ToString().Contains('*', StringComparison.Ordinal);

    /// <summary>
    /// Neither kind of expression can repeat an assertion. A basic one never reaches this for <c>*</c>, <c>\+</c>, or
    /// <c>\?</c>, which are ordinary characters after an assertion, so what is left there is a bound.
    /// </summary>
    protected override bool IsQuantifiable(RegexTermSyntax term) => term is not RegexAnchorSyntax;

    /// <summary>
    /// A backreference is a single digit, and it must name a group that has already been closed in the branch it is in.
    /// The GNU word anchors <c>\&lt;</c>, <c>\&gt;</c>, <c>\`</c>, and <c>\'</c> are assertions.
    /// </summary>
    protected override RegexAtomSyntax? TryParseDialectEscape(GreenNode? leadingTrivia)
    {
        var start = Scanner.Position;
        var next = Scanner.Peek();
        if (next is '<' or '>' or '`' or '\'')
        {
            Scanner.Position += 2;

            return new RegexAnchorSyntax(Scanner.Token(SyntaxKind.AnchorToken, start, leadingTrivia), Options);
        }

        if (next is < '1' or > '9')
            return null;

        Scanner.Position += 2;
        var number = next - '0';
        var token = Scanner.Token(SyntaxKind.BackreferenceToken, start, leadingTrivia, number.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (!IsCaptureCompleted(number))
        {
            AddDiagnostic(token.Span, RegexDiagnosticIds.UndefinedNumberedReference, CaptureTable.ContainsNumber(number)
                ? $"Group {next} is not closed before this backreference."
                : $"Reference to undefined group number {next}.");
        }

        return new RegexBackreferenceSyntax(token, Options);
    }

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

    /// <summary>
    /// A brace is always an interval, even a malformed one, which is then an error rather than ordinary text. The one
    /// exception is a basic expression's <c>*</c> right after a leading <c>^</c>, which is a character.
    /// </summary>
    protected override bool IsQuantifierAt(int position)
    {
        if (BoundOpenLength(position) > 0)
            return true;

        if (!DelimitersAreEscaped)
            return base.IsQuantifierAt(position);

        if (_lastAtomWasAnchor && position < Text.Length && (Text[position] == '*' || EscapedLength(position, '+') > 0 || EscapedLength(position, '?') > 0))
            return false;

        // A basic expression has no bare "+" or "?"; GNU spells them escaped.
        return SimpleQuantifierLength(position, out _) > 0;
    }

    /// <summary>Whether the atom just read is an assertion, after which a basic expression has nothing to repeat.</summary>
    private bool _lastAtomWasAnchor;

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

    protected override RegexAtomSyntax ParseAtom(GreenNode? leadingTrivia)
    {
        var afterAnchor = _lastAtomWasAnchor && !IsAtSequenceStart;
        var atom = ParseAtomCore(leadingTrivia, afterAnchor);
        _lastAtomWasAnchor = atom is RegexAnchorSyntax;

        return atom;
    }

    private RegexAtomSyntax ParseAtomCore(GreenNode? leadingTrivia, bool afterAnchor)
    {
        var start = Scanner.Position;

        if (!DelimitersAreEscaped)
        {
            // An unmatched ")" is an ordinary character in an extended expression.
            if (Scanner.Current == ')')
                return ReadLiteral(leadingTrivia);

            return base.ParseAtom(leadingTrivia);
        }

        // An unmatched "\)" is an error, unlike the bare ")" of an extended expression.
        if (GroupCloseLength(start) > 0)
        {
            Scanner.Position += GroupCloseLength(start);
            var unmatched = Scanner.Token(SyntaxKind.BadToken, start, leadingTrivia);
            AddDiagnostic(unmatched.Span, RegexDiagnosticIds.InsufficientOpeningParentheses, "Unmatched '\\)'.");

            return new RegexSkippedTextSyntax(unmatched, Options);
        }

        // An interval with nothing before it has nothing to repeat, even at the start of a branch.
        if (BoundOpenLength(start) > 0)
        {
            Scanner.Position += BoundOpenLength(start);
            var stray = Scanner.Token(SyntaxKind.BadToken, start, leadingTrivia);
            AddDiagnostic(stray.Span, RegexDiagnosticIds.QuantifierAfterNothing, "Quantifier '\\{' has nothing to repeat.");

            return new RegexSkippedTextSyntax(stray, Options);
        }

        // The delimiters are the escaped spellings, so the bare characters are ordinary text.
        if (Scanner.Current is '(' or ')' or '{' or '}' or '|' or '+' or '?')
            return ReadLiteral(leadingTrivia);

        // "*" is a quantifier only when something precedes it; at the start of a branch, or right after the "^" that
        // anchors it, it matches an asterisk.
        if (Scanner.Current == '*' && (IsAtSequenceStart || afterAnchor))
            return ReadLiteral(leadingTrivia);

        // The GNU "\+" and "\?" have nothing to repeat there either, and are then the characters themselves.
        if (Scanner.Current == '\\' && Scanner.Peek() is '+' or '?' && (IsAtSequenceStart || afterAnchor))
        {
            Scanner.Position += 2;

            return new RegexCharacterEscapeSyntax(Scanner.Token(SyntaxKind.EscapeToken, start, leadingTrivia, Text[start + 1].ToString()), Options);
        }

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

    private RegexLiteralSyntax ReadLiteral(GreenNode? leadingTrivia)
    {
        var start = Scanner.Position;
        Scanner.Position++;

        return new RegexLiteralSyntax(Scanner.Token(SyntaxKind.LiteralToken, start, leadingTrivia), Options);
    }
}
