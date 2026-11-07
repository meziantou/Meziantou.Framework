namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>export declaration</c>, <c>export { specifiers }</c> or <c>export { specifiers } from 'source'</c>.</summary>
internal sealed class ExportNamedDeclaration : Node
{
    public Node? Declaration { get; set; }

    public List<ExportSpecifier> Specifiers { get; set; } = [];

    public StringLiteral? Source { get; set; }

    public List<ImportAttribute> Attributes { get; set; } = [];

    public override NodeType Type => NodeType.ExportNamedDeclaration;
}
