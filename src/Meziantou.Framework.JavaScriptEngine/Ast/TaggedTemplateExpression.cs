namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>tag`template`</c></summary>
internal sealed class TaggedTemplateExpression : Node
{
    public Node Tag { get; set; } = null!;

    public TemplateLiteral Quasi { get; set; } = null!;

    public override NodeType Type => NodeType.TaggedTemplateExpression;
}
