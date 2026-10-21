namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary>A property of an object literal or of an object pattern.</summary>
internal sealed class Property : Node
{
    /// <summary>An <see cref="Identifier"/>, a <see cref="StringLiteral"/>, a <see cref="NumericLiteral"/>, a <see cref="BigIntLiteral"/>, or any expression when <see cref="Computed"/>.</summary>
    public Node Key { get; set; } = null!;

    /// <summary>The value expression, the <see cref="FunctionNode"/> of a method or an accessor (in a <see cref="FunctionExpression"/>), or the target of an object pattern.</summary>
    public Node Value { get; set; } = null!;

    public PropertyKind Kind { get; set; }

    public bool Computed { get; set; }

    public bool Method { get; set; }

    public bool Shorthand { get; set; }

    public override NodeType Type => NodeType.Property;
}
