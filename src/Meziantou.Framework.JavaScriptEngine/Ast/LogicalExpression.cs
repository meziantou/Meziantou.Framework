namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>&amp;&amp;</c>, <c>||</c> and <c>??</c>.</summary>
internal sealed class LogicalExpression : Node
{
    public LogicalOperator Operator { get; set; }

    public Node Left { get; set; } = null!;

    public Node Right { get; set; } = null!;

    public override NodeType Type => NodeType.LogicalExpression;
}
