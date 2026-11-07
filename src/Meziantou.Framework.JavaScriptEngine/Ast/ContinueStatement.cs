namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>continue label;</c></summary>
internal sealed class ContinueStatement : Node
{
    public Identifier? Label { get; set; }

    public override NodeType Type => NodeType.ContinueStatement;
}
