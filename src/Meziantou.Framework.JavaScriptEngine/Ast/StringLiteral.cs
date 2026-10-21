namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary>A string literal (<see cref="Value"/> has its escapes decoded; it may contain lone surrogates).</summary>
internal sealed class StringLiteral : Node
{
    public string Value { get; set; } = "";

    public override NodeType Type => NodeType.StringLiteral;
}
