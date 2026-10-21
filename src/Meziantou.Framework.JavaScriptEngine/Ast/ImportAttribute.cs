namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>key: 'value'</c> in the attributes of an import or an export.</summary>
internal sealed class ImportAttribute : Node
{
    /// <summary>An <see cref="Identifier"/> or a <see cref="StringLiteral"/>.</summary>
    public Node Key { get; set; } = null!;

    public StringLiteral Value { get; set; } = null!;

    public override NodeType Type => NodeType.ImportAttribute;
}
