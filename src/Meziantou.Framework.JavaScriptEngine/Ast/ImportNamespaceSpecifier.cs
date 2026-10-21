namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>import * as local from 'source'</c></summary>
internal sealed class ImportNamespaceSpecifier : Node
{
    public Identifier Local { get; set; } = null!;

    public override NodeType Type => NodeType.ImportNamespaceSpecifier;
}
