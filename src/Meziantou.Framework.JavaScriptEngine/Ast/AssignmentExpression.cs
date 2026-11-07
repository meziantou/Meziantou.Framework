namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>left = right</c> and the compound assignments; for <c>=</c>, the left side may be a pattern.</summary>
internal sealed class AssignmentExpression : Node
{
    public AssignmentOperator Operator { get; set; }

    public Node Left { get; set; } = null!;

    public Node Right { get; set; } = null!;

    public override NodeType Type => NodeType.AssignmentExpression;
}
