namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary>A function, generator, async function or async generator declaration.</summary>
internal sealed class FunctionDeclaration : Node
{
    public FunctionNode Function { get; set; } = null!;

    public override NodeType Type => NodeType.FunctionDeclaration;
}
