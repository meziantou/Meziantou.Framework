namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary>The common part of the class declarations and expressions.</summary>
internal sealed class ClassNode : Node
{
    public Identifier? Id { get; set; }

    public Node? SuperClass { get; set; }

    /// <summary><see cref="MethodDefinition"/>, <see cref="PropertyDefinition"/> and <see cref="StaticBlock"/>.</summary>
    public List<Node> Body { get; set; } = [];

    public override NodeType Type => NodeType.ClassNode;
}
