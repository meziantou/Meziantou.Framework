namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>var</c>, <c>let</c>, <c>const</c>, <c>using</c> or <c>await using</c> declarations.</summary>
internal sealed class VariableDeclaration : Node
{
    public VariableDeclarationKind Kind { get; set; }

    public List<VariableDeclarator> Declarations { get; set; } = [];

    public override NodeType Type => NodeType.VariableDeclaration;
}
