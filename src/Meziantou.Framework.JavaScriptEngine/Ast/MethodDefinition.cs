namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary>A method, an accessor or the constructor of a class.</summary>
internal sealed class MethodDefinition : Node
{
    /// <summary>A property name, a <see cref="PrivateIdentifier"/>, or any expression when <see cref="Computed"/>.</summary>
    public Node Key { get; set; } = null!;

    public FunctionNode Value { get; set; } = null!;

    public MethodKind Kind { get; set; }

    public bool Static { get; set; }

    public bool Computed { get; set; }

    public override NodeType Type => NodeType.MethodDefinition;
}
