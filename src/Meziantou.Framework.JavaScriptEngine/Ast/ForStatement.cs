namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>for (init; test; update) body</c></summary>
internal sealed class ForStatement : Node
{
    /// <summary>A <see cref="VariableDeclaration"/> or an expression.</summary>
    public Node? Init { get; set; }

    public Node? Test { get; set; }

    public Node? Update { get; set; }

    public Node Body { get; set; } = null!;

    public override NodeType Type => NodeType.ForStatement;
}
