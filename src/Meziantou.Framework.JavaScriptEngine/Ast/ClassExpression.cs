namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary>A class expression.</summary>
internal sealed class ClassExpression : Node
{
    public ClassNode Class { get; set; } = null!;

    public override NodeType Type => NodeType.ClassExpression;
}
