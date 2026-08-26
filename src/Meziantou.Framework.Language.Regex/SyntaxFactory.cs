using Meziantou.Framework.Language.Regex.Internals;

namespace Meziantou.Framework.Language.Regex;

/// <summary>Builds regular-expression syntax nodes programmatically.</summary>
/// <remarks>
/// A node built here carries no source position and no trivia, which is what makes it usable as a replacement:
/// <see cref="RegexSyntaxNode.ReplaceNode"/> keeps the whitespace in front of the node it replaces precisely because
/// the replacement brought none of its own.
/// </remarks>
public static class SyntaxFactory
{
    /// <summary>Creates a literal that matches <paramref name="value"/> exactly, escaping it where the flavor needs it.</summary>
    public static RegexAtomSyntax Literal(char value, RegexFlavor flavor)
    {
        ArgumentNullException.ThrowIfNull(flavor);

        return NeedsEscape(value, flavor)
            ? new RegexCharacterEscapeSyntax(new RegexSyntaxToken(RegexSyntaxKind.EscapeToken, $"\\{value}", value.ToString()))
            : new RegexLiteralSyntax(new RegexSyntaxToken(RegexSyntaxKind.LiteralToken, value.ToString()));
    }

    /// <summary>Creates a sequence that matches <paramref name="value"/> literally, escaping every character that needs it.</summary>
    public static RegexSequenceSyntax LiteralText(string value, RegexFlavor flavor)
    {
        ArgumentNullException.ThrowIfNull(value);
        ArgumentNullException.ThrowIfNull(flavor);

        var terms = new List<RegexTermSyntax>(value.Length);
        foreach (var ch in value)
        {
            terms.Add(Literal(ch, flavor));
        }

        return new RegexSequenceSyntax(terms);
    }

    /// <summary>Creates <c>.</c>, which matches any character.</summary>
    public static RegexAnyCharacterSyntax AnyCharacter() => new(new RegexSyntaxToken(RegexSyntaxKind.DotToken, "."));

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

        return new RegexAnchorSyntax(new RegexSyntaxToken(RegexSyntaxKind.AnchorToken, text));
    }

    /// <summary>Creates a shorthand class escape such as <c>\d</c>.</summary>
    public static RegexCharacterClassEscapeSyntax ClassEscape(char letter) =>
        new(new RegexSyntaxToken(RegexSyntaxKind.ClassEscapeToken, $"\\{letter}"));

    /// <summary>Creates an alternation from its branches, inserting the <c>|</c> between them.</summary>
    public static RegexAlternationSyntax Alternation(params IEnumerable<RegexSequenceSyntax> branches)
    {
        ArgumentNullException.ThrowIfNull(branches);

        var list = branches.ToArray();
        var bars = new List<RegexSyntaxToken>(Math.Max(0, list.Length - 1));
        for (var index = 1; index < list.Length; index++)
        {
            bars.Add(new RegexSyntaxToken(RegexSyntaxKind.BarToken, "|"));
        }

        return new RegexAlternationSyntax(list, bars);
    }

    /// <summary>Creates a sequence from its terms.</summary>
    public static RegexSequenceSyntax Sequence(params IEnumerable<RegexTermSyntax> terms)
    {
        ArgumentNullException.ThrowIfNull(terms);

        return new RegexSequenceSyntax([.. terms]);
    }

    /// <summary>Wraps <paramref name="alternation"/> in a capturing group.</summary>
    public static RegexCapturingGroupSyntax Group(RegexAlternationSyntax alternation, int number = 0)
    {
        ArgumentNullException.ThrowIfNull(alternation);

        return new RegexCapturingGroupSyntax(
            new RegexSyntaxToken(RegexSyntaxKind.OpenParenToken, "("),
            alternation,
            new RegexSyntaxToken(RegexSyntaxKind.CloseParenToken, ")"),
            number);
    }

    /// <summary>Wraps <paramref name="alternation"/> in a non-capturing group, <c>(?:…)</c>.</summary>
    public static RegexNonCapturingGroupSyntax NonCapturingGroup(RegexAlternationSyntax alternation)
    {
        ArgumentNullException.ThrowIfNull(alternation);

        return new RegexNonCapturingGroupSyntax(
            new RegexSyntaxToken(RegexSyntaxKind.OpenParenToken, "("),
            new RegexSyntaxToken(RegexSyntaxKind.GroupKindToken, "?:"),
            alternation,
            new RegexSyntaxToken(RegexSyntaxKind.CloseParenToken, ")"));
    }

    /// <summary>Applies <c>*</c>, <c>+</c>, or <c>?</c> to <paramref name="atom"/>.</summary>
    public static RegexQuantifiedSyntax Quantified(RegexAtomSyntax atom, char quantifier, RegexQuantifierMode mode = RegexQuantifierMode.Greedy)
    {
        ArgumentNullException.ThrowIfNull(atom);

        var kind = quantifier switch
        {
            '+' => RegexSyntaxKind.PlusToken,
            '?' => RegexSyntaxKind.QuestionToken,
            _ => RegexSyntaxKind.AsteriskToken,
        };

        return new RegexQuantifiedSyntax(atom, new RegexSimpleQuantifierSyntax(new RegexSyntaxToken(kind, quantifier.ToString()), Modifier(mode)));
    }

    /// <summary>Applies a <c>{n,m}</c> bound to <paramref name="atom"/>. Pass <see langword="null"/> for an unbounded maximum.</summary>
    public static RegexQuantifiedSyntax Quantified(RegexAtomSyntax atom, int min, int? max, RegexQuantifierMode mode = RegexQuantifierMode.Greedy)
    {
        ArgumentNullException.ThrowIfNull(atom);
        ArgumentOutOfRangeException.ThrowIfNegative(min);

        var minToken = new RegexSyntaxToken(RegexSyntaxKind.NumberToken, FormatCount(min));
        RegexSyntaxToken? commaToken = null;
        RegexSyntaxToken? maxToken = null;
        if (max != min)
        {
            commaToken = new RegexSyntaxToken(RegexSyntaxKind.CommaToken, ",");
            if (max is { } bound)
            {
                ArgumentOutOfRangeException.ThrowIfLessThan(bound, min, nameof(max));
                maxToken = new RegexSyntaxToken(RegexSyntaxKind.NumberToken, FormatCount(bound));
            }
        }

        var quantifier = new RegexRangeQuantifierSyntax(
            new RegexSyntaxToken(RegexSyntaxKind.OpenBraceToken, "{"),
            minToken,
            commaToken,
            maxToken,
            new RegexSyntaxToken(RegexSyntaxKind.CloseBraceToken, "}"),
            Modifier(mode));

        return new RegexQuantifiedSyntax(atom, quantifier);
    }

    /// <summary>Creates a character class from its members.</summary>
    public static RegexCharacterClassSyntax CharacterClass(bool negated, params IEnumerable<RegexSyntaxNode> members)
    {
        ArgumentNullException.ThrowIfNull(members);

        return new RegexCharacterClassSyntax(
            new RegexSyntaxToken(RegexSyntaxKind.OpenBracketToken, "["),
            negated ? new RegexSyntaxToken(RegexSyntaxKind.CaretToken, "^") : null,
            [.. members],
            new RegexSyntaxToken(RegexSyntaxKind.CloseBracketToken, "]"));
    }

    /// <summary>Creates a range such as <c>a-z</c>, for use inside a character class.</summary>
    public static RegexCharacterRangeSyntax CharacterRange(char first, char last, RegexFlavor flavor)
    {
        ArgumentNullException.ThrowIfNull(flavor);
        ArgumentOutOfRangeException.ThrowIfLessThan(last, first);

        return new RegexCharacterRangeSyntax(
            Literal(first, flavor),
            new RegexSyntaxToken(RegexSyntaxKind.HyphenToken, "-"),
            Literal(last, flavor));
    }

    private static RegexSyntaxToken? Modifier(RegexQuantifierMode mode) => mode switch
    {
        RegexQuantifierMode.Lazy => new RegexSyntaxToken(RegexSyntaxKind.QuestionToken, "?"),
        RegexQuantifierMode.Possessive => new RegexSyntaxToken(RegexSyntaxKind.PlusToken, "+"),
        _ => null,
    };

    private static string FormatCount(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>Returns whether a character has to be escaped to match itself.</summary>
    /// <remarks>
    /// The set is the engine's own table of characters that stop a run of ordinary text, plus <c>]</c> and <c>}</c>,
    /// which are harmless on their own but are escaped anyway so the result reads the same in every flavor.
    /// </remarks>
    private static bool NeedsEscape(char value, RegexFlavor flavor) =>
        RegexCharacterTables.IsSpecialOrSpace(value) ||
        value is ']' or '}' or '-' ||
        (flavor.HasFeature(RegexFlavorFeatures.EscapedGroupDelimiters) && value is '(' or ')' or '{');
}
