namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>if (test) consequent else alternate</c></summary>
internal sealed class IfStatement : Node
{
    public Node Test { get; set; } = null!;

    public Node Consequent { get; set; } = null!;

    public Node? Alternate { get; set; }

    public override NodeType Type => NodeType.IfStatement;
}
