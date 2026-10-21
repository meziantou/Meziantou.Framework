namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>return argument;</c></summary>
internal sealed class ReturnStatement : Node
{
    public Node? Argument { get; set; }

    public override NodeType Type => NodeType.ReturnStatement;
}
