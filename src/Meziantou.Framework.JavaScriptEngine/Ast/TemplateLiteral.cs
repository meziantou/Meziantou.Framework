namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>`quasi ${expression} quasi`</c></summary>
internal sealed class TemplateLiteral : Node
{
    public List<TemplateElement> Quasis { get; set; } = [];

    public List<Node> Expressions { get; set; } = [];

    public override NodeType Type => NodeType.TemplateLiteral;
}
