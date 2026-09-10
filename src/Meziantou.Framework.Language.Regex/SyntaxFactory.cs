using Meziantou.Framework.Language.InternalSyntax;
using Meziantou.Framework.Language.Regex.Internals;
using Green = Meziantou.Framework.Language.Regex.Syntax.InternalSyntax;

namespace Meziantou.Framework.Language.Regex;

/// <summary>Builds regular-expression syntax nodes programmatically.</summary>
/// <remarks>
/// A node built here carries no source position and no trivia, which is what makes it usable as a replacement:
/// replacing a node keeps the whitespace in front of it precisely because the replacement brought none of its own.
/// A node built here also reports <see cref="RegexPatternOptions.None"/>, because it was never read under any.
/// </remarks>
public static partial class SyntaxFactory
{
    /// <summary>Creates a token of the given kind and text.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static SyntaxToken Token(SyntaxKind kind, string text, string? valueText = null)
    {
        ArgumentNullException.ThrowIfNull(text);

        return new SyntaxToken(parent: null, Green.SyntaxFactory.Token(kind, text, leadingTrivia: null, valueText), position: 0, index: 0);
    }

    /// <summary>Creates a zero-width token standing in for one a pattern does not have.</summary>
    public static SyntaxToken MissingToken(SyntaxKind kind) => new(parent: null, Green.SyntaxFactory.MissingToken(kind), position: 0, index: 0);

    /// <summary>Creates trivia of the given kind and text.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public static SyntaxTrivia Trivia(SyntaxKind kind, string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return new SyntaxTrivia(default, Green.SyntaxFactory.Trivia(kind, text), position: 0, index: 0);
    }

    /// <summary>Creates a literal that matches <paramref name="value"/> exactly, escaping it where the dialect needs it.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="dialect"/> is <see langword="null"/>.</exception>
    public static RegexAtomSyntax Literal(char value, RegexDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(dialect);

        return NeedsEscape(value, dialect)
            ? CharacterEscape(Token(SyntaxKind.EscapeToken, $"\\{value}", value.ToString()), RegexPatternOptions.None)
            : Literal(Token(SyntaxKind.LiteralToken, value.ToString()), RegexPatternOptions.None);
    }

    /// <summary>Creates a sequence that matches <paramref name="value"/> literally, escaping every character that needs it.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> or <paramref name="dialect"/> is <see langword="null"/>.</exception>
    public static RegexSequenceSyntax LiteralText(string value, RegexDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(dialect);

        var terms = new List<RegexTermSyntax>(value.Length);
        foreach (var character in value)
        {
            terms.Add(Literal(character, dialect));
        }

        return Sequence(terms);
    }

    /// <summary>Creates <c>.</c>, which matches any character.</summary>
    public static RegexAnyCharacterSyntax AnyCharacter() => AnyCharacter(Token(SyntaxKind.DotToken, "."), RegexPatternOptions.None);

    /// <summary>Creates an anchor such as <c>^</c> or <c>\b</c>.</summary>
    public static RegexAnchorSyntax Anchor(RegexAnchorKind kind)
    {
        var text = kind switch
        {
            RegexAnchorKind.Caret => "^",
            RegexAnchorKind.Dollar => "$",
            RegexAnchorKind.StartOfInput => "\\A",
            RegexAnchorKind.EndOfInputBeforeFinalLineBreak => "\\Z",
            RegexAnchorKind.EndOfInput => "\\z",
            RegexAnchorKind.ContiguousMatch => "\\G",
            RegexAnchorKind.NonWordBoundary => "\\B",
            RegexAnchorKind.KeepOut => "\\K",
            _ => "\\b",
        };

        return Anchor(Token(SyntaxKind.AnchorToken, text), RegexPatternOptions.None);
    }

    /// <summary>Creates a shorthand class escape such as <c>\d</c>.</summary>
    public static RegexCharacterClassEscapeSyntax ClassEscape(char letter)
        => CharacterClassEscape(Token(SyntaxKind.ClassEscapeToken, $"\\{letter}"), RegexPatternOptions.None);

    /// <summary>Creates an alternation from its branches, putting a <c>|</c> between them.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="branches"/> is <see langword="null"/>.</exception>
    public static RegexAlternationSyntax Alternation(params IEnumerable<RegexSequenceSyntax> branches)
    {
        ArgumentNullException.ThrowIfNull(branches);

        var items = new List<SyntaxNodeOrToken>();
        foreach (var branch in branches)
        {
            if (items.Count > 0)
            {
                items.Add(Token(SyntaxKind.BarToken, "|"));
            }

            items.Add(branch);
        }

        return Alternation(new SeparatedSyntaxList<RegexSequenceSyntax>(new SyntaxNodeOrTokenList(items)), RegexPatternOptions.None);
    }

    /// <summary>Creates a sequence from its terms.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="terms"/> is <see langword="null"/>.</exception>
    public static RegexSequenceSyntax Sequence(params IEnumerable<RegexTermSyntax> terms)
    {
        ArgumentNullException.ThrowIfNull(terms);

        return Sequence(new SyntaxList<RegexTermSyntax>(terms), RegexPatternOptions.None);
    }

    /// <summary>Wraps <paramref name="alternation"/> in a capturing group.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="alternation"/> is <see langword="null"/>.</exception>
    public static RegexCapturingGroupSyntax Group(RegexAlternationSyntax alternation, int number = 0)
    {
        ArgumentNullException.ThrowIfNull(alternation);

        return CapturingGroup(
            Token(SyntaxKind.OpenParenToken, "("),
            alternation,
            Token(SyntaxKind.CloseParenToken, ")"),
            RegexPatternOptions.None,
            RegexPatternOptions.None,
            number);
    }

    /// <summary>Wraps <paramref name="alternation"/> in a non-capturing group, <c>(?:…)</c>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="alternation"/> is <see langword="null"/>.</exception>
    public static RegexNonCapturingGroupSyntax NonCapturingGroup(RegexAlternationSyntax alternation)
    {
        ArgumentNullException.ThrowIfNull(alternation);

        return NonCapturingGroup(
            Token(SyntaxKind.OpenParenToken, "("),
            Token(SyntaxKind.GroupKindToken, "?:"),
            alternation,
            Token(SyntaxKind.CloseParenToken, ")"),
            RegexPatternOptions.None,
            RegexPatternOptions.None);
    }

    /// <summary>Applies <c>*</c>, <c>+</c>, or <c>?</c> to <paramref name="atom"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="atom"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="quantifier"/> is not one of the three operators.</exception>
    public static RegexQuantifiedSyntax Quantified(RegexAtomSyntax atom, char quantifier, RegexQuantifierMode mode = RegexQuantifierMode.Greedy)
    {
        ArgumentNullException.ThrowIfNull(atom);

        // Anything else would be written into the node's text and reparse as a literal, so the node would not describe
        // the pattern it claims to.
        var kind = quantifier switch
        {
            '*' => SyntaxKind.AsteriskToken,
            '+' => SyntaxKind.PlusToken,
            '?' => SyntaxKind.QuestionToken,
            _ => throw new ArgumentOutOfRangeException(nameof(quantifier), quantifier, "A quantifier operator must be '*', '+', or '?'."),
        };

        var operatorNode = SimpleQuantifier(Token(kind, quantifier.ToString()), Modifier(mode), RegexPatternOptions.None);

        return Quantified(atom, operatorNode, RegexPatternOptions.None);
    }

    /// <summary>Applies a <c>{n,m}</c> bound to <paramref name="atom"/>. Pass <see langword="null"/> for an unbounded maximum.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="atom"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="min"/> is negative, or <paramref name="max"/> is below it.</exception>
    public static RegexQuantifiedSyntax Quantified(RegexAtomSyntax atom, int min, int? max, RegexQuantifierMode mode = RegexQuantifierMode.Greedy)
    {
        ArgumentNullException.ThrowIfNull(atom);
        ArgumentOutOfRangeException.ThrowIfNegative(min);

        var minToken = Token(SyntaxKind.NumberToken, FormatCount(min));
        var commaToken = default(SyntaxToken);
        var maxToken = default(SyntaxToken);
        if (max != min)
        {
            commaToken = Token(SyntaxKind.CommaToken, ",");
            if (max is { } bound)
            {
                ArgumentOutOfRangeException.ThrowIfLessThan(bound, min, nameof(max));
                maxToken = Token(SyntaxKind.NumberToken, FormatCount(bound));
            }
        }

        var quantifier = RangeQuantifier(
            Token(SyntaxKind.OpenBraceToken, "{"),
            minToken,
            commaToken,
            maxToken,
            Token(SyntaxKind.CloseBraceToken, "}"),
            Modifier(mode),
            RegexPatternOptions.None);

        return Quantified(atom, quantifier, RegexPatternOptions.None);
    }

    /// <summary>Creates a character class from its members.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="members"/> is <see langword="null"/>.</exception>
    public static RegexCharacterClassSyntax CharacterClass(bool negated, params IEnumerable<RegexSyntaxNode> members)
    {
        ArgumentNullException.ThrowIfNull(members);

        return CharacterClass(
            Token(SyntaxKind.OpenBracketToken, "["),
            negated ? Token(SyntaxKind.CaretToken, "^") : default,
            new SyntaxList<RegexSyntaxNode>(members),
            Token(SyntaxKind.CloseBracketToken, "]"),
            RegexPatternOptions.None);
    }

    /// <summary>Creates a range such as <c>a-z</c>, for use inside a character class.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="dialect"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="last"/> is below <paramref name="first"/>.</exception>
    public static RegexCharacterRangeSyntax CharacterRange(char first, char last, RegexDialect dialect)
    {
        ArgumentNullException.ThrowIfNull(dialect);
        ArgumentOutOfRangeException.ThrowIfLessThan(last, first);

        return CharacterRange(Literal(first, dialect), Token(SyntaxKind.HyphenToken, "-"), Literal(last, dialect), RegexPatternOptions.None);
    }

    /// <summary>Returns the immutable token behind <paramref name="token"/>, or a missing one when it is absent.</summary>
    internal static GreenNode Required(SyntaxToken token) => token.Node ?? Green.SyntaxFactory.MissingToken(SyntaxKind.None);

    private static SyntaxToken Modifier(RegexQuantifierMode mode) => mode switch
    {
        RegexQuantifierMode.Lazy => Token(SyntaxKind.QuestionToken, "?"),
        RegexQuantifierMode.Possessive => Token(SyntaxKind.PlusToken, "+"),
        _ => default,
    };

    private static string FormatCount(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>Returns whether a character has to be escaped to match itself in this dialect.</summary>
    /// <remarks>
    /// <para>
    /// Escaping is not a superset game: adding a backslash can <em>create</em> a construct. In POSIX basic expressions
    /// a bare <c>(</c> is already the literal and <c>\(</c> is what opens a group, so escaping it there would produce
    /// the opposite of what the caller asked for. The set therefore has to be chosen per dialect rather than by taking
    /// the union.
    /// </para>
    /// <para>
    /// The Perl-derived set is the engine's own table of characters that stop a run of ordinary text, plus <c>]</c>,
    /// <c>}</c>, and <c>-</c>, which are harmless outside a class but not inside one, and whitespace and <c>#</c>,
    /// which matter once extended mode is on.
    /// </para>
    /// </remarks>
    private static bool NeedsEscape(char value, RegexDialect dialect) => dialect.Family switch
    {
        // Basic expressions: the escaped forms are the constructs, so only the unescaped specials are escaped here.
        RegexDialectFamily.Posix when dialect.HasFeature(RegexDialectFeatures.EscapedGroupDelimiters) =>
            value is '.' or '*' or '[' or ']' or '^' or '$' or '\\',

        // Extended expressions have no backslash escapes beyond the specials themselves.
        RegexDialectFamily.Posix =>
            value is '.' or '*' or '+' or '?' or '[' or ']' or '(' or ')' or '{' or '}' or '|' or '^' or '$' or '\\',

        _ => RegexCharacterTables.IsSpecialOrSpace(value) || value is ']' or '}' or '-',
    };
}
