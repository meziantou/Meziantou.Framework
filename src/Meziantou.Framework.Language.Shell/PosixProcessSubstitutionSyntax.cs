namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents process substitution, <c>&lt;(...)</c> or <c>&gt;(...)</c>.</summary>
public sealed class PosixProcessSubstitutionSyntax : ShellWordPartSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PosixProcessSubstitutionSyntax(ShellSyntaxToken openToken, ShellStatementListSyntax statements, ShellSyntaxToken closeToken)
        : base(
            ShellSyntaxKind.PosixProcessSubstitution,
            openToken?.ToFullString() + statements?.ToFullString() + closeToken?.ToFullString(),
            openToken?.FullSpan.Start ?? 0,
            [openToken!, closeToken!])
    {
        OpenToken = openToken!;
        Statements = statements!;
        CloseToken = closeToken!;
        _childNodes = [statements!];
    }

    /// <summary>The <c>&lt;(</c> or <c>&gt;(</c> token.</summary>
    public ShellSyntaxToken OpenToken { get; }

    public ShellStatementListSyntax Statements { get; }
    public ShellSyntaxToken CloseToken { get; }
    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    /// <summary>Returns <see langword="true"/> for <c>&lt;(...)</c>, which the command reads from.</summary>
    public bool IsInput => OpenToken.Kind == ShellSyntaxKind.LessThanOpenParenToken;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitProcessSubstitution(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitProcessSubstitution(this);
}
