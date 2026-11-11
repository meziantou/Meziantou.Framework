namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary>
/// <c>export * from 'source'</c>, <c>export * as exported from 'source'</c> or <c>export { a, b } as exported from 'source'</c>
/// (a filtered namespace), optionally deferred (<c>export defer * as exported from 'source'</c>,
/// https://tc39.es/proposal-deferred-reexports/).
/// </summary>
internal sealed class ExportAllDeclaration : Node
{
    /// <summary>An <see cref="Identifier"/> or a <see cref="StringLiteral"/>.</summary>
    public Node? Exported { get; set; }

    /// <summary>Gets or sets the names of a filtered namespace (<see cref="Identifier"/> or <see cref="StringLiteral"/>), or null for <c>*</c>.</summary>
    public List<Node>? Names { get; set; }

    /// <summary>Gets or sets whether the declaration is <c>export defer</c>.</summary>
    public bool IsDeferred { get; set; }

    public StringLiteral Source { get; set; } = null!;

    public List<ImportAttribute> Attributes { get; set; } = [];

    public override NodeType Type => NodeType.ExportAllDeclaration;
}
