namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>catch (param) body</c>; the parameter is optional.</summary>
internal sealed class CatchClause : Node
{
    /// <summary>A binding identifier or a binding pattern.</summary>
    public Node? Param { get; set; }

    public BlockStatement Body { get; set; } = null!;

    public override NodeType Type => NodeType.CatchClause;
}
