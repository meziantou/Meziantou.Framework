namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents unquoted literal text inside a word.</summary>
public sealed class ShellLiteralWordPartSyntax : ShellWordPartSyntax
{
    public ShellLiteralWordPartSyntax(ShellSyntaxToken textToken)
        : base(ShellSyntaxKind.LiteralWordPart, GetFullText(textToken), textToken?.FullSpan.Start ?? 0, [textToken!])
    {
        TextToken = textToken!;
    }

    public ShellSyntaxToken TextToken { get; }

    /// <summary>The literal text with escape sequences resolved.</summary>
    public string Value => TextToken.ValueText;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitLiteralWordPart(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitLiteralWordPart(this);

    private static string GetFullText(ShellSyntaxToken textToken)
    {
        ArgumentNullException.ThrowIfNull(textToken);

        return textToken.ToFullString();
    }
}
