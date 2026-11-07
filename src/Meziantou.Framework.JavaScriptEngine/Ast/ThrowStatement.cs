namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>throw argument;</c></summary>
internal sealed class ThrowStatement : Node
{
    public Node Argument { get; set; } = null!;

    public override NodeType Type => NodeType.ThrowStatement;
}
