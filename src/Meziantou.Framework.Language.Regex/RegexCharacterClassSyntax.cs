namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a character class, <c>[abc]</c> or <c>[^a-z]</c>.</summary>
public sealed class RegexCharacterClassSyntax : RegexAtomSyntax
{
    private readonly IReadOnlyList<RegexSyntaxNode> _childNodes;

    public RegexCharacterClassSyntax(
        RegexSyntaxToken openBracketToken,
        RegexSyntaxToken? caretToken,
        IReadOnlyList<RegexSyntaxNode>? members,
        RegexSyntaxToken closeBracketToken)
        : base(
            RegexSyntaxKind.CharacterClass,
            openBracketToken.ToFullString() + (caretToken?.ToFullString() ?? string.Empty) + BuildFullText(members ?? []) + closeBracketToken.ToFullString(),
            openBracketToken.FullSpan.Start,
            caretToken is null ? [openBracketToken, closeBracketToken] : [openBracketToken, caretToken, closeBracketToken])
    {
        OpenBracketToken = openBracketToken;
        CaretToken = caretToken;
        Members = members ?? [];
        CloseBracketToken = closeBracketToken;
        _childNodes = [.. Members];
    }

    public RegexSyntaxToken OpenBracketToken { get; }

    /// <summary>The <c>^</c> that negates the class, or <see langword="null"/> when the class is not negated.</summary>
    public RegexSyntaxToken? CaretToken { get; }

    /// <summary>The characters, ranges, escapes, and nested constructs the class contains.</summary>
    public IReadOnlyList<RegexSyntaxNode> Members { get; }

    /// <summary>The <c>]</c> that closes the class. It is missing when the pattern ends first.</summary>
    public RegexSyntaxToken CloseBracketToken { get; }

    public bool IsNegated => CaretToken is not null;

    public override IReadOnlyList<RegexSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(RegexSyntaxVisitor visitor) => visitor.VisitCharacterClass(this);
    public override TResult Accept<TResult>(RegexSyntaxVisitor<TResult> visitor) => visitor.VisitCharacterClass(this);
}
