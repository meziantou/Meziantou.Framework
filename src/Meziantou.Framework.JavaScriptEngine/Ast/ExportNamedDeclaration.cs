namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary>
/// <c>export declaration</c>, <c>export { specifiers }</c>, <c>export { specifiers } from 'source'</c> or
/// <c>export defer { specifiers } from 'source'</c> (https://tc39.es/proposal-deferred-reexports/).
/// </summary>
internal sealed class ExportNamedDeclaration : Node
{
    /// <summary>Gets or sets whether the declaration is <c>export defer</c>.</summary>
    public bool IsDeferred { get; set; }

    public Node? Declaration { get; set; }

    public List<ExportSpecifier> Specifiers { get; set; } = [];

    public StringLiteral? Source { get; set; }

    public List<ImportAttribute> Attributes { get; set; } = [];

    public override NodeType Type => NodeType.ExportNamedDeclaration;
}
