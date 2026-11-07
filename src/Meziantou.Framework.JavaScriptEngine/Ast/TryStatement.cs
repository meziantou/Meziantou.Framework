namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>try block catch (param) handler finally finalizer</c></summary>
internal sealed class TryStatement : Node
{
    public BlockStatement Block { get; set; } = null!;

    public CatchClause? Handler { get; set; }

    public BlockStatement? Finalizer { get; set; }

    public override NodeType Type => NodeType.TryStatement;
}
