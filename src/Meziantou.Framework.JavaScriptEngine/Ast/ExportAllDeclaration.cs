namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>export * from 'source'</c> or <c>export * as exported from 'source'</c>.</summary>
internal sealed class ExportAllDeclaration : Node
{
    /// <summary>An <see cref="Identifier"/> or a <see cref="StringLiteral"/>.</summary>
    public Node? Exported { get; set; }

    public StringLiteral Source { get; set; } = null!;

    public List<ImportAttribute> Attributes { get; set; } = [];

    public override NodeType Type => NodeType.ExportAllDeclaration;
}
