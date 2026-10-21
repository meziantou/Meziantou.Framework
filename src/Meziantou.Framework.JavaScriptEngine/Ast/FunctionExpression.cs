namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary>A function, generator, async function or async generator expression, or the function of a method.</summary>
internal sealed class FunctionExpression : Node
{
    public FunctionNode Function { get; set; } = null!;

    public override NodeType Type => NodeType.FunctionExpression;
}
