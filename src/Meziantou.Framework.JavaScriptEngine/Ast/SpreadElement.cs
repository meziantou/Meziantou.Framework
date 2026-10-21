namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>...argument</c> in an array literal, an object literal or the arguments of a call.</summary>
internal sealed class SpreadElement : Node
{
    public Node Argument { get; set; } = null!;

    public override NodeType Type => NodeType.SpreadElement;
}
