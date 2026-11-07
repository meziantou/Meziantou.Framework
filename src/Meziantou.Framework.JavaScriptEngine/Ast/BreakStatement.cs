namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>break label;</c></summary>
internal sealed class BreakStatement : Node
{
    public Identifier? Label { get; set; }

    public override NodeType Type => NodeType.BreakStatement;
}
