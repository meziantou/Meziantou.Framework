namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents a zero-width assertion such as <c>^</c>, <c>$</c>, <c>\b</c>, or <c>\A</c>.</summary>
public sealed partial class RegexAnchorSyntax : RegexAtomSyntax
{
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
}
