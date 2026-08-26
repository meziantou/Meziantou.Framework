namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents text the parser could not recognize, kept so the pattern still round-trips.</summary>
public sealed class RegexSkippedTextSyntax : RegexAtomSyntax
{
    public RegexSkippedTextSyntax(IReadOnlyList<RegexSyntaxToken>? tokens, int fullStart = 0)
        : base(
            RegexSyntaxKind.SkippedText,
            BuildFullText(tokens ?? []),
            tokens is { Count: > 0 } ? tokens[0].FullSpan.Start : fullStart,
            tokens ?? [])
    {
    }

    public string Text => ToFullString();

    public override void Accept(RegexSyntaxVisitor visitor) => visitor.VisitSkippedText(this);
    public override TResult Accept<TResult>(RegexSyntaxVisitor<TResult> visitor) => visitor.VisitSkippedText(this);
}
