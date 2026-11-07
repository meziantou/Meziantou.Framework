namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary>A block: <c>{ ... }</c>.</summary>
internal sealed class BlockStatement : Node
{
    public List<Node> Body { get; set; } = [];

    public override NodeType Type => NodeType.BlockStatement;
}
