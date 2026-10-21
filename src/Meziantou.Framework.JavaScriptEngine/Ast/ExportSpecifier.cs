namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>local as exported</c> in an export declaration.</summary>
internal sealed class ExportSpecifier : Node
{
    /// <summary>An <see cref="Identifier"/>, or a <see cref="StringLiteral"/> when the export has a source.</summary>
    public Node Local { get; set; } = null!;

    /// <summary>An <see cref="Identifier"/> or a <see cref="StringLiteral"/>.</summary>
    public Node Exported { get; set; } = null!;

    public override NodeType Type => NodeType.ExportSpecifier;
}
