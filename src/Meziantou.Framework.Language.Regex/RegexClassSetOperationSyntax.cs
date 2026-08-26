namespace Meziantou.Framework.Language.Regex;

/// <summary>
/// Represents a JavaScript <c>v</c>-mode class set operation, such as the intersection in <c>[\w&amp;&amp;[a-z]]</c>.
/// </summary>
public sealed class RegexClassSetOperationSyntax : RegexSyntaxNode
{
    private readonly IReadOnlyList<RegexSyntaxNode> _childNodes;

    public RegexClassSetOperationSyntax(RegexSyntaxNode left, RegexSyntaxToken operatorToken, RegexSyntaxNode? right)
        : base(RegexSyntaxKind.ClassSetOperation, [operatorToken], Part(left), Part(operatorToken), Part(right))
    {
        Left = left;
        OperatorToken = operatorToken;
        Right = right;
        _childNodes = Children(left, right);
    }

    public RegexSyntaxNode Left { get; }

    /// <summary>The <c>&amp;&amp;</c>, <c>--</c>, or <c>||</c> operator.</summary>
    public RegexSyntaxToken OperatorToken { get; }

    /// <summary>The right operand, absent when the class ends before it.</summary>
    public RegexSyntaxNode? Right { get; }

    public override IReadOnlyList<RegexSyntaxNode> ChildNodes => _childNodes;

    public override void Accept(RegexSyntaxVisitor visitor) => visitor.VisitClassSetOperation(this);
    public override TResult Accept<TResult>(RegexSyntaxVisitor<TResult> visitor) => visitor.VisitClassSetOperation(this);
}
