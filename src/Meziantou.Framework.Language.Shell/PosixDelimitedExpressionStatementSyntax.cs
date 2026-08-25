namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a <c>[[ ... ]]</c> conditional or a <c>(( ... ))</c> arithmetic command.</summary>
public sealed class PosixDelimitedExpressionStatementSyntax : ShellStatementSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PosixDelimitedExpressionStatementSyntax(ShellSyntaxKind kind, ShellSyntaxToken openToken, ShellExpressionSyntax expression, ShellSyntaxToken closeToken)
        : base(
            kind,
            openToken?.ToFullString() + expression?.ToFullString() + closeToken?.ToFullString(),
            openToken?.FullSpan.Start ?? 0,
            [openToken!, closeToken!])
    {
        OpenToken = openToken!;
        Expression = expression!;
        CloseToken = closeToken!;
        _childNodes = [expression!];
    }

    public ShellSyntaxToken OpenToken { get; }

    /// <summary>
    /// The expression between the delimiters. Text that does not fit the grammar comes back as a
    /// <see cref="ShellRawExpressionSyntax"/> rather than being rejected.
    /// </summary>
    public ShellExpressionSyntax Expression { get; }

    public ShellSyntaxToken CloseToken { get; }
    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    /// <summary>Returns <see langword="true"/> for <c>(( ... ))</c>.</summary>
    public bool IsArithmetic => Kind == ShellSyntaxKind.PosixArithmeticCommand;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitDelimitedExpressionStatement(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitDelimitedExpressionStatement(this);
}
