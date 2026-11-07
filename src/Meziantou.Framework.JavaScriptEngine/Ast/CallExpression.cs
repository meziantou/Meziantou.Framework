namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>callee(arguments)</c>, <c>callee?.(arguments)</c> or <c>super(arguments)</c>.</summary>
internal sealed class CallExpression : Node
{
    public Node Callee { get; set; } = null!;

    public List<Node> Arguments { get; set; } = [];

    public bool Optional { get; set; }

    public override NodeType Type => NodeType.CallExpression;
}
