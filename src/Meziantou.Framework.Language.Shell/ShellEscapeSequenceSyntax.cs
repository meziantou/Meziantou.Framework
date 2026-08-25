namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents an escape sequence inside a word, such as <c>\$</c>.</summary>
public sealed class ShellEscapeSequenceSyntax : ShellWordPartSyntax
{
    public ShellEscapeSequenceSyntax(ShellSyntaxToken escapeToken)
        : base(ShellSyntaxKind.EscapeSequence, GetFullText(escapeToken), escapeToken?.FullSpan.Start ?? 0, [escapeToken!])
    {
        EscapeToken = escapeToken!;
    }

    public ShellSyntaxToken EscapeToken { get; }

    /// <summary>The character the escape sequence produces.</summary>
    public string Value => EscapeToken.ValueText;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitEscapeSequence(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitEscapeSequence(this);

    private static string GetFullText(ShellSyntaxToken escapeToken)
    {
        ArgumentNullException.ThrowIfNull(escapeToken);

        return escapeToken.ToFullString();
    }
}
