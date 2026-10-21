namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>for (left of right) body</c>, or <c>for await (left of right) body</c>.</summary>
internal sealed class ForOfStatement : Node
{
    /// <summary>A <see cref="VariableDeclaration"/> or an assignment target.</summary>
    public Node Left { get; set; } = null!;

    public Node Right { get; set; } = null!;

    public Node Body { get; set; } = null!;

    public bool IsAwait { get; set; }

    public override NodeType Type => NodeType.ForOfStatement;
}
