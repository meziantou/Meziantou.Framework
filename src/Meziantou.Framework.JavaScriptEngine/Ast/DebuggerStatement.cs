namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>debugger;</c></summary>
internal sealed class DebuggerStatement : Node
{
    public override NodeType Type => NodeType.DebuggerStatement;
}
