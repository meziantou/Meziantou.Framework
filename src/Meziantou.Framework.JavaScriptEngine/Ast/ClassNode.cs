namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary>The common part of the class declarations and expressions.</summary>
internal sealed class ClassNode : Node
{
    public Identifier? Id { get; set; }

    public Node? SuperClass { get; set; }

    /// <summary>The decorators (<c>@expr</c>), in source order: identifiers, member expressions, calls and any parenthesized expression.</summary>
    public List<Node>? Decorators { get; set; }

    /// <summary><see cref="MethodDefinition"/>, <see cref="PropertyDefinition"/> and <see cref="StaticBlock"/>.</summary>
    public List<Node> Body { get; set; } = [];

    public override NodeType Type => NodeType.ClassNode;

    /// <summary>Gets whether the class or one of its elements has a decorator.</summary>
    public bool HasDecorators()
    {
        if (Decorators is not null)
            return true;

        foreach (var element in Body)
        {
            if (element is MethodDefinition { Decorators: not null } or PropertyDefinition { Decorators: not null })
                return true;
        }

        return false;
    }
}
