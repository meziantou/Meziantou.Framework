namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>true</c> or <c>false</c></summary>
internal sealed class BooleanLiteral : Node
{
    public bool Value { get; set; }

    public override NodeType Type => NodeType.BooleanLiteral;
}
