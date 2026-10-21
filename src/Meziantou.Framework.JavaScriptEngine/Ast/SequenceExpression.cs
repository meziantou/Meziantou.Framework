namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>a, b, c</c></summary>
internal sealed class SequenceExpression : Node
{
    public List<Node> Expressions { get; set; } = [];

    public override NodeType Type => NodeType.SequenceExpression;
}
