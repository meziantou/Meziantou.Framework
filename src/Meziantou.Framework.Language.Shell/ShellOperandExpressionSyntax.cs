namespace Meziantou.Framework.Language.Shell;

/// <summary>
/// Represents a leaf of an expression: a number, a variable, or a word. The value is held as a
/// <see cref="ShellWordSyntax"/> so quoting, expansions, and globs keep the structure they have everywhere else.
/// </summary>
public sealed class ShellOperandExpressionSyntax : ShellExpressionSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public ShellOperandExpressionSyntax(ShellWordSyntax word)
        : base(ShellSyntaxKind.OperandExpression, word.ToFullString(), word.FullSpan.Start)
    {
        Word = word;
        _childNodes = [word];
    }

    public ShellWordSyntax Word { get; }

    /// <summary>The operand text with quoting resolved, or <see langword="null"/> when it needs runtime expansion.</summary>
    public string? Value => Word.Value;

    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitOperandExpression(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitOperandExpression(this);
}
