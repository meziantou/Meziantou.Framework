namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a statement prefixed by the <c>time</c> or <c>coproc</c> keyword.</summary>
public sealed class PosixPrefixedStatementSyntax : ShellStatementSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PosixPrefixedStatementSyntax(ShellSyntaxKind kind, ShellSyntaxToken keyword, ShellSyntaxToken? nameToken, ShellStatementSyntax statement)
        : base(
            kind,
            keyword?.ToFullString() + (nameToken?.ToFullString() ?? string.Empty) + statement?.ToFullString(),
            keyword?.FullSpan.Start ?? 0,
            nameToken is null ? [keyword!] : [keyword!, nameToken])
    {
        Keyword = keyword!;
        NameToken = nameToken;
        Statement = statement!;
        _childNodes = [statement!];
    }

    /// <summary>The <c>time</c> or <c>coproc</c> keyword.</summary>
    public ShellSyntaxToken Keyword { get; }

    /// <summary>The optional coprocess name, as in <c>coproc NAME { ... }</c>.</summary>
    public ShellSyntaxToken? NameToken { get; }

    public ShellStatementSyntax Statement { get; }
    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitPrefixedStatement(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitPrefixedStatement(this);
}
