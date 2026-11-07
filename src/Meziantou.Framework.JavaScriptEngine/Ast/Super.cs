namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>super</c>, only as the callee of a call or the object of a member expression.</summary>
internal sealed class Super : Node
{
    public override NodeType Type => NodeType.Super;
}
