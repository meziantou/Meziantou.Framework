namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>null</c></summary>
internal sealed class NullLiteral : Node
{
    public override NodeType Type => NodeType.NullLiteral;
}
