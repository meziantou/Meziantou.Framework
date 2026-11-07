namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>[elements]</c> as a binding or assignment target; a null element is an elision.</summary>
internal sealed class ArrayPattern : Node
{
    public List<Node?> Elements { get; set; } = [];

    public override NodeType Type => NodeType.ArrayPattern;
}
