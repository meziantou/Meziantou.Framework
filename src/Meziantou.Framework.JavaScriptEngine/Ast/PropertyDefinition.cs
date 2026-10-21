namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary>A field of a class: <c>key = value;</c>.</summary>
internal sealed class PropertyDefinition : Node
{
    /// <summary>A property name, a <see cref="PrivateIdentifier"/>, or any expression when <see cref="Computed"/>.</summary>
    public Node Key { get; set; } = null!;

    public Node? Value { get; set; }

    public bool Static { get; set; }

    public bool Computed { get; set; }

    public override NodeType Type => NodeType.PropertyDefinition;
}
