namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>import ... from 'source' with { attributes }</c></summary>
internal sealed class ImportDeclaration : Node
{
    /// <summary><see cref="ImportSpecifier"/>, <see cref="ImportDefaultSpecifier"/> and <see cref="ImportNamespaceSpecifier"/>.</summary>
    public List<Node> Specifiers { get; set; } = [];

    public StringLiteral Source { get; set; } = null!;

    public List<ImportAttribute> Attributes { get; set; } = [];

    /// <summary>Gets or sets whether the declaration is <c>import defer * as ns from</c> (https://tc39.es/proposal-defer-import-eval/).</summary>
    public bool IsDefer { get; set; }

    public override NodeType Type => NodeType.ImportDeclaration;
}
