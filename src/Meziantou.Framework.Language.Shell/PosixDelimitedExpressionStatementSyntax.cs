namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a <c>[[ ... ]]</c> conditional or a <c>(( ... ))</c> arithmetic command.</summary>
public sealed class PosixDelimitedExpressionStatementSyntax : ShellStatementSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PosixDelimitedExpressionStatementSyntax(ShellSyntaxKind kind, ShellSyntaxToken openToken, ShellRawExpressionSyntax expression, ShellSyntaxToken closeToken)
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

    /// <summary>The text between the delimiters, kept verbatim.</summary>
    public ShellRawExpressionSyntax Expression { get; }

    public ShellSyntaxToken CloseToken { get; }
    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    /// <summary>Returns <see langword="true"/> for <c>(( ... ))</c>.</summary>
    public bool IsArithmetic => Kind == ShellSyntaxKind.PosixArithmeticCommand;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitDelimitedExpressionStatement(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitDelimitedExpressionStatement(this);
}
