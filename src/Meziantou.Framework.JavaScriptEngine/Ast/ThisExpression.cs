namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>this</c></summary>
internal sealed class ThisExpression : Node
{
    public override NodeType Type => NodeType.ThisExpression;
}
