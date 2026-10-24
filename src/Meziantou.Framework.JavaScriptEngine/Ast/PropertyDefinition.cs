namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary>A field of a class: <c>key = value;</c>, or an auto-accessor: <c>accessor key = value;</c>.</summary>
internal sealed class PropertyDefinition : Node
{
    /// <summary>A property name, a <see cref="PrivateIdentifier"/>, or any expression when <see cref="Computed"/>.</summary>
    public Node Key { get; set; } = null!;

    public Node? Value { get; set; }

    public bool Static { get; set; }

    public bool Computed { get; set; }

    /// <summary>Gets or sets whether the field is an auto-accessor: <c>accessor key = value;</c>.</summary>
    public bool IsAccessor { get; set; }

    /// <summary>The decorators (<c>@expr</c>), in source order: identifiers, member expressions, calls and any parenthesized expression.</summary>
    public List<Node>? Decorators { get; set; }

    public override NodeType Type => NodeType.PropertyDefinition;
}
