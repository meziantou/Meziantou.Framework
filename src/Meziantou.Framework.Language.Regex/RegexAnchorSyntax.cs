namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a zero-width assertion such as <c>^</c>, <c>$</c>, <c>\b</c>, or <c>\A</c>.</summary>
public sealed class RegexAnchorSyntax : RegexAtomSyntax
{
    public RegexAnchorSyntax(RegexSyntaxToken anchorToken)
        : base(RegexSyntaxKind.Anchor, [anchorToken], Part(anchorToken))
    {
        AnchorToken = anchorToken;
    }

    public RegexSyntaxToken AnchorToken { get; }

    /// <summary>Which assertion this is.</summary>
    public RegexAnchorKind AnchorKind => AnchorToken.Text switch
    {
        "^" => RegexAnchorKind.Caret,
        "$" => RegexAnchorKind.Dollar,
        "\\A" => RegexAnchorKind.StartOfInput,
        "\\Z" => RegexAnchorKind.EndOfInputBeforeFinalLineBreak,
        "\\z" => RegexAnchorKind.EndOfInput,
        "\\G" => RegexAnchorKind.ContiguousMatch,
        "\\B" => RegexAnchorKind.NonWordBoundary,
        "\\K" => RegexAnchorKind.KeepOut,
        _ => RegexAnchorKind.WordBoundary,
    };

    public override void Accept(RegexSyntaxVisitor visitor) => visitor.VisitAnchor(this);
    public override TResult Accept<TResult>(RegexSyntaxVisitor<TResult> visitor) => visitor.VisitAnchor(this);
}
