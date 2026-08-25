namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a hashtable literal, <c>@{ key = value }</c>.</summary>
public sealed class PowerShellHashLiteralSyntax : ShellExpressionSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PowerShellHashLiteralSyntax(
        ShellSyntaxToken openToken,
        IReadOnlyList<PowerShellHashEntrySyntax>? entries,
        ShellSyntaxToken closeBraceToken)
        : base(
            ShellSyntaxKind.PowerShellHashLiteral,
            openToken.ToFullString() + BuildFullText(entries ?? []) + closeBraceToken.ToFullString(),
            openToken.FullSpan.Start,
            [openToken, closeBraceToken])
    {
        OpenToken = openToken;
        Entries = entries ?? [];
        CloseBraceToken = closeBraceToken;
        _childNodes = [.. (Entries as IEnumerable<ShellSyntaxNode>)];
    }

    /// <summary>The <c>@{</c> token.</summary>
    public ShellSyntaxToken OpenToken { get; }

    /// <summary>The entries.</summary>
    public IReadOnlyList<PowerShellHashEntrySyntax> Entries { get; }

    /// <summary>The closing brace.</summary>
    public ShellSyntaxToken CloseBraceToken { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitHashLiteral(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitHashLiteral(this);
}
