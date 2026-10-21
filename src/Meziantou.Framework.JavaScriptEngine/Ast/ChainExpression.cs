namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary>An optional chain (<c>a?.b.c</c>): the member expressions and calls that are short-circuited when an optional part is null or undefined.</summary>
internal sealed class ChainExpression : Node
{
    public Node Expression { get; set; } = null!;

    public override NodeType Type => NodeType.ChainExpression;
}
