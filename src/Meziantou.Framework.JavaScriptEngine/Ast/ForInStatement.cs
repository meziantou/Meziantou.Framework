namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>for (left in right) body</c></summary>
internal sealed class ForInStatement : Node
{
    /// <summary>A <see cref="VariableDeclaration"/> (one declarator, possibly with an initializer in sloppy code, Annex B) or an assignment target.</summary>
    public Node Left { get; set; } = null!;

    public Node Right { get; set; } = null!;

    public Node Body { get; set; } = null!;

    public override NodeType Type => NodeType.ForInStatement;
}
