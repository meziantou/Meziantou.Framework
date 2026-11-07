namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>{ properties }</c></summary>
internal sealed class ObjectExpression : Node
{
    /// <summary><see cref="Property"/> and <see cref="SpreadElement"/>.</summary>
    public List<Node> Properties { get; set; } = [];

    public override NodeType Type => NodeType.ObjectExpression;
}
