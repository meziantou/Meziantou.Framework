namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>yield argument</c> or <c>yield* argument</c>.</summary>
internal sealed class YieldExpression : Node
{
    public Node? Argument { get; set; }

    public bool Delegate { get; set; }

    public override NodeType Type => NodeType.YieldExpression;
}
