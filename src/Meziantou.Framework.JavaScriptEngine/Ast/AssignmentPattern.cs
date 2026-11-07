namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>target = default</c> in a pattern or a parameter list.</summary>
internal sealed class AssignmentPattern : Node
{
    public Node Left { get; set; } = null!;

    public Node Right { get; set; } = null!;

    public override NodeType Type => NodeType.AssignmentPattern;
}
