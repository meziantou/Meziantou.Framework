namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>while (test) body</c></summary>
internal sealed class WhileStatement : Node
{
    public Node Test { get; set; } = null!;

    public Node Body { get; set; } = null!;

    public override NodeType Type => NodeType.WhileStatement;
}
