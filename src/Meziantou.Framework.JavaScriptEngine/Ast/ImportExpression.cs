namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>import(source, options)</c></summary>
internal sealed class ImportExpression : Node
{
    public Node Source { get; set; } = null!;

    public Node? Options { get; set; }

    public override NodeType Type => NodeType.ImportExpression;
}
