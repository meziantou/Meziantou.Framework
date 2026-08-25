namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a variable assignment such as <c>NAME=value</c>.</summary>
public sealed class ShellAssignmentSyntax : ShellSyntaxNode
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public ShellAssignmentSyntax(ShellSyntaxToken nameToken, ShellSyntaxToken equalsToken, ShellWordSyntax? value)
        : base(
            ShellSyntaxKind.Assignment,
            BuildText(nameToken, equalsToken, value),
            nameToken?.FullSpan.Start ?? 0,
            [nameToken!, equalsToken!])
    {
        NameToken = nameToken!;
        EqualsToken = equalsToken!;
        Value = value;
        _childNodes = value is null ? [] : [value];
    }

    public ShellSyntaxToken NameToken { get; }
    public string Name => NameToken.ValueText;
    public ShellSyntaxToken EqualsToken { get; }

    /// <summary>The assigned word, or <see langword="null"/> for an empty assignment such as <c>NAME=</c>.</summary>
    public ShellWordSyntax? Value { get; }

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public ShellAssignmentSyntax WithValue(ShellWordSyntax? value)
    {
        if (ReferenceEquals(value, Value))
            return this;

        return new ShellAssignmentSyntax(NameToken, EqualsToken, value);
    }

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitAssignment(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitAssignment(this);

    private static string BuildText(ShellSyntaxToken nameToken, ShellSyntaxToken equalsToken, ShellWordSyntax? value)
    {
        ArgumentNullException.ThrowIfNull(nameToken);
        ArgumentNullException.ThrowIfNull(equalsToken);

        return nameToken.ToFullString() + equalsToken.ToFullString() + (value?.ToFullString() ?? string.Empty);
    }
}
