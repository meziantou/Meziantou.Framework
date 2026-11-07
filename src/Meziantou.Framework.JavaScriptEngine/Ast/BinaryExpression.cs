namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary>An arithmetic, bitwise, relational or equality operator, <c>in</c> and <c>instanceof</c>; the left operand of <c>in</c> may be a <see cref="PrivateIdentifier"/>.</summary>
internal sealed class BinaryExpression : Node
{
    public BinaryOperator Operator { get; set; }

    public Node Left { get; set; } = null!;

    public Node Right { get; set; } = null!;

    public override NodeType Type => NodeType.BinaryExpression;
}
