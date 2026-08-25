namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents an array assignment such as <c>files=(a b c)</c>.</summary>
public sealed class PosixArrayAssignmentSyntax : ShellSyntaxNode
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public PosixArrayAssignmentSyntax(
        ShellSyntaxToken nameToken,
        ShellSyntaxToken equalsToken,
        ShellSyntaxToken openParenToken,
        IReadOnlyList<ShellWordSyntax>? elements,
        ShellSyntaxToken closeParenToken)
        : base(
            ShellSyntaxKind.PosixArrayAssignment,
            nameToken?.ToFullString() + equalsToken?.ToFullString() + openParenToken?.ToFullString()
                + BuildFullText(elements ?? []) + closeParenToken?.ToFullString(),
            nameToken?.FullSpan.Start ?? 0,
            [nameToken!, equalsToken!, openParenToken!, closeParenToken!])
    {
        NameToken = nameToken!;
        EqualsToken = equalsToken!;
        OpenParenToken = openParenToken!;
        Elements = elements ?? [];
        CloseParenToken = closeParenToken!;
        _childNodes = [.. Elements];
    }

    public ShellSyntaxToken NameToken { get; }
    public string Name => NameToken.ValueText;
    public ShellSyntaxToken EqualsToken { get; }
    public ShellSyntaxToken OpenParenToken { get; }
    public IReadOnlyList<ShellWordSyntax> Elements { get; }
    public ShellSyntaxToken CloseParenToken { get; }
    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitArrayAssignment(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitArrayAssignment(this);
}
