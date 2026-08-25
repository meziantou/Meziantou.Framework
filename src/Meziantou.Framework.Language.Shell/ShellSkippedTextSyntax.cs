namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents invalid or unrecognized shell text retained in the concrete syntax tree.</summary>
public sealed class ShellSkippedTextSyntax : ShellStatementSyntax
{
    public ShellSkippedTextSyntax(IReadOnlyList<ShellSyntaxToken> tokens, int fullStart)
        : base(ShellSyntaxKind.SkippedText, BuildFullText(tokens ?? []), fullStart, tokens)
    {
    }

    public string Text => ToFullString();

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitSkippedText(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitSkippedText(this);
}
