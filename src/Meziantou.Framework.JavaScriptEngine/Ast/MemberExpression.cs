namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>object.property</c>, <c>object[property]</c>, <c>object?.property</c> or <c>object.#property</c>.</summary>
internal sealed class MemberExpression : Node
{
    /// <summary>An expression, or <see cref="Super"/>.</summary>
    public Node Object { get; set; } = null!;

    /// <summary>An <see cref="Identifier"/> or a <see cref="PrivateIdentifier"/>, or any expression when <see cref="Computed"/>.</summary>
    public Node Property { get; set; } = null!;

    public bool Computed { get; set; }

    public bool Optional { get; set; }

    public override NodeType Type => NodeType.MemberExpression;
}
