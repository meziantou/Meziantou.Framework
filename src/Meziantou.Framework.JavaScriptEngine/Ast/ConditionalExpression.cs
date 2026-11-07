namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>test ? consequent : alternate</c></summary>
internal sealed class ConditionalExpression : Node
{
    public Node Test { get; set; } = null!;

    public Node Consequent { get; set; } = null!;

    public Node Alternate { get; set; } = null!;

    public override NodeType Type => NodeType.ConditionalExpression;
}
