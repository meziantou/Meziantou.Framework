namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>/pattern/flags</c></summary>
internal sealed class RegExpLiteral : Node
{
    public string Pattern { get; set; } = "";

    public string Flags { get; set; } = "";

    public override NodeType Type => NodeType.RegExpLiteral;
}
