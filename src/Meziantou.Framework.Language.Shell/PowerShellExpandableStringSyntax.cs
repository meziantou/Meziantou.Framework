namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a double-quoted or here-string literal, whose embedded expansions are kept as child nodes.</summary>
public sealed class PowerShellExpandableStringSyntax : ShellExpressionSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PowerShellExpandableStringSyntax(
        ShellSyntaxKind kind,
        ShellSyntaxToken openToken,
        IReadOnlyList<ShellSyntaxNode>? parts,
        ShellSyntaxToken closeToken)
        : base(
            kind,
            openToken.ToFullString() + BuildFullText(parts ?? []) + closeToken.ToFullString(),
            openToken.FullSpan.Start,
            [openToken, closeToken])
    {
        OpenToken = openToken;
        Parts = parts ?? [];
        CloseToken = closeToken;
        _childNodes = [.. (Parts as IEnumerable<ShellSyntaxNode>)];
    }

    /// <summary>The opening quote or here-string marker.</summary>
    public ShellSyntaxToken OpenToken { get; }

    /// <summary>The literal runs and embedded expansions.</summary>
    public IReadOnlyList<ShellSyntaxNode> Parts { get; }

    /// <summary>The closing quote or here-string marker.</summary>
    public ShellSyntaxToken CloseToken { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitExpandableString(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitExpandableString(this);
}
