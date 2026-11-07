namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>++x</c>, <c>--x</c>, <c>x++</c> and <c>x--</c>.</summary>
internal sealed class UpdateExpression : Node
{
    public UpdateOperator Operator { get; set; }

    public bool Prefix { get; set; }

    public Node Argument { get; set; } = null!;

    public override NodeType Type => NodeType.UpdateExpression;
}
