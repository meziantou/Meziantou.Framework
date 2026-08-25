namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a <c>while</c> or <c>until</c> loop.</summary>
public sealed class PosixWhileStatementSyntax : ShellStatementSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PosixWhileStatementSyntax(
        ShellSyntaxKind kind,
        ShellSyntaxToken keyword,
        ShellStatementListSyntax condition,
        ShellSyntaxToken doKeyword,
        ShellStatementListSyntax body,
        ShellSyntaxToken doneKeyword)
        : base(
            kind,
            keyword?.ToFullString() + condition?.ToFullString() + doKeyword?.ToFullString() + body?.ToFullString() + doneKeyword?.ToFullString(),
            keyword?.FullSpan.Start ?? 0,
            [keyword!, doKeyword!, doneKeyword!])
    {
        Keyword = keyword!;
        Condition = condition!;
        DoKeyword = doKeyword!;
        Body = body!;
        DoneKeyword = doneKeyword!;
        _childNodes = [condition!, body!];
    }

    /// <summary>The <c>while</c> or <c>until</c> keyword.</summary>
    public ShellSyntaxToken Keyword { get; }

    public ShellStatementListSyntax Condition { get; }
    public ShellSyntaxToken DoKeyword { get; }
    public ShellStatementListSyntax Body { get; }
    public ShellSyntaxToken DoneKeyword { get; }
    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    /// <summary>Returns <see langword="true"/> for an <c>until</c> loop, which repeats while the condition fails.</summary>
    public bool IsUntil => Kind == ShellSyntaxKind.PosixUntilStatement;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitWhileStatement(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitWhileStatement(this);
}
