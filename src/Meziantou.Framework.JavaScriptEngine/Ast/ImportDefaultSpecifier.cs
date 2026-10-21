namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>import local from 'source'</c></summary>
internal sealed class ImportDefaultSpecifier : Node
{
    public Identifier Local { get; set; } = null!;

    public override NodeType Type => NodeType.ImportDefaultSpecifier;
}
