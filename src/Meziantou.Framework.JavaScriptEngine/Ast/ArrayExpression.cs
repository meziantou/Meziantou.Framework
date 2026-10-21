namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>[elements]</c>; a null element is a hole (elision).</summary>
internal sealed class ArrayExpression : Node
{
    public List<Node?> Elements { get; set; } = [];

    public override NodeType Type => NodeType.ArrayExpression;
}
