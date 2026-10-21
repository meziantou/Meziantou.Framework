namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>export default declaration</c></summary>
internal sealed class ExportDefaultDeclaration : Node
{
    /// <summary>A <see cref="FunctionDeclaration"/> or a <see cref="ClassDeclaration"/> (whose name may be absent), or an expression.</summary>
    public Node Declaration { get; set; } = null!;

    public override NodeType Type => NodeType.ExportDefaultDeclaration;
}
