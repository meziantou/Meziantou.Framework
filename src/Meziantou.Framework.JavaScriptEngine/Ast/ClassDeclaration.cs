namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary>A class declaration.</summary>
internal sealed class ClassDeclaration : Node
{
    public ClassNode Class { get; set; } = null!;

    public override NodeType Type => NodeType.ClassDeclaration;
}
