namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary>
/// <c>import * as local from 'source'</c>, or <c>import { a, b } as local from 'source'</c> (a filtered namespace,
/// https://tc39.es/proposal-deferred-reexports/).
/// </summary>
internal sealed class ImportNamespaceSpecifier : Node
{
    public Identifier Local { get; set; } = null!;

    /// <summary>Gets or sets the names of a filtered namespace (<see cref="Identifier"/> or <see cref="StringLiteral"/>), or null for <c>*</c>.</summary>
    public List<Node>? Names { get; set; }

    public override NodeType Type => NodeType.ImportNamespaceSpecifier;
}
