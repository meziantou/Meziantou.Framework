namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>with (object) body</c></summary>
internal sealed class WithStatement : Node
{
    public Node Object { get; set; } = null!;

    public Node Body { get; set; } = null!;

    public override NodeType Type => NodeType.WithStatement;
}
