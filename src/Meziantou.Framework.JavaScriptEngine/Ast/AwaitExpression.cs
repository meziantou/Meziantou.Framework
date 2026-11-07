namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>await argument</c></summary>
internal sealed class AwaitExpression : Node
{
    public Node Argument { get; set; } = null!;

    public override NodeType Type => NodeType.AwaitExpression;
}
