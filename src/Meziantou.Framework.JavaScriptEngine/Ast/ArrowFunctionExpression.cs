namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>(params) =&gt; body</c> or <c>async (params) =&gt; body</c>.</summary>
internal sealed class ArrowFunctionExpression : Node
{
    public FunctionNode Function { get; set; } = null!;

    public override NodeType Type => NodeType.ArrowFunctionExpression;
}
