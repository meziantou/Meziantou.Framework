namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>...target</c> in a pattern or a parameter list.</summary>
internal sealed class RestElement : Node
{
    public Node Argument { get; set; } = null!;

    public override NodeType Type => NodeType.RestElement;
}
