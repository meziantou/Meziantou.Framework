namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>id = init</c> in a variable declaration.</summary>
internal sealed class VariableDeclarator : Node
{
    /// <summary>A binding identifier or a binding pattern.</summary>
    public Node Id { get; set; } = null!;

    public Node? Init { get; set; }

    public override NodeType Type => NodeType.VariableDeclarator;
}
