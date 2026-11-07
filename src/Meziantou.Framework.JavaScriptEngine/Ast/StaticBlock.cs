namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>static { body }</c> in a class.</summary>
internal sealed class StaticBlock : Node
{
    public List<Node> Body { get; set; } = [];

    public override NodeType Type => NodeType.StaticBlock;
}
