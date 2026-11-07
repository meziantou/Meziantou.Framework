namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary>A number literal.</summary>
internal sealed class NumericLiteral : Node
{
    public double Value { get; set; }

    public override NodeType Type => NodeType.NumericLiteral;
}
