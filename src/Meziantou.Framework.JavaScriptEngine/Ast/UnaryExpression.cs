namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>-x</c>, <c>+x</c>, <c>!x</c>, <c>~x</c>, <c>typeof x</c>, <c>void x</c> and <c>delete x</c>.</summary>
internal sealed class UnaryExpression : Node
{
    public UnaryOperator Operator { get; set; }

    public Node Argument { get; set; } = null!;

    public override NodeType Type => NodeType.UnaryExpression;
}
