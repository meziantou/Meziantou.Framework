namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>do body while (test)</c></summary>
internal sealed class DoWhileStatement : Node
{
    public Node Body { get; set; } = null!;

    public Node Test { get; set; } = null!;

    public override NodeType Type => NodeType.DoWhileStatement;
}
