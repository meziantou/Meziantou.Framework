namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>new callee(arguments)</c></summary>
internal sealed class NewExpression : Node
{
    public Node Callee { get; set; } = null!;

    public List<Node> Arguments { get; set; } = [];

    public override NodeType Type => NodeType.NewExpression;
}
