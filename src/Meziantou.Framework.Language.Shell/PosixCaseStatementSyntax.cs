namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a <c>case ... in ... esac</c> statement.</summary>
public sealed class PosixCaseStatementSyntax : ShellStatementSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PosixCaseStatementSyntax(
        ShellSyntaxToken caseKeyword,
        ShellWordSyntax subject,
        ShellSyntaxToken inKeyword,
        IReadOnlyList<PosixCaseClauseSyntax>? clauses,
        ShellSyntaxToken esacKeyword)
        : base(
            ShellSyntaxKind.PosixCaseStatement,
            caseKeyword?.ToFullString() + subject?.ToFullString() + inKeyword?.ToFullString()
                + BuildFullText(clauses ?? []) + esacKeyword?.ToFullString(),
            caseKeyword?.FullSpan.Start ?? 0,
            [caseKeyword!, inKeyword!, esacKeyword!])
    {
        CaseKeyword = caseKeyword!;
        Subject = subject!;
        InKeyword = inKeyword!;
        Clauses = clauses ?? [];
        EsacKeyword = esacKeyword!;
        _childNodes = [subject!, .. Clauses];
    }

    public ShellSyntaxToken CaseKeyword { get; }

    /// <summary>The word the clauses are matched against.</summary>
    public ShellWordSyntax Subject { get; }

    public ShellSyntaxToken InKeyword { get; }
    public IReadOnlyList<PosixCaseClauseSyntax> Clauses { get; }
    public ShellSyntaxToken EsacKeyword { get; }
    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitCaseStatement(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitCaseStatement(this);
}
