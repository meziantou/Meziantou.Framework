namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>imported as local</c> in an import declaration.</summary>
internal sealed class ImportSpecifier : Node
{
    /// <summary>An <see cref="Identifier"/> or a <see cref="StringLiteral"/>.</summary>
    public Node Imported { get; set; } = null!;

    public Identifier Local { get; set; } = null!;

    public override NodeType Type => NodeType.ImportSpecifier;
}
