namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>label: body</c></summary>
internal sealed class LabeledStatement : Node
{
    public Identifier Label { get; set; } = null!;

    public Node Body { get; set; } = null!;

    public override NodeType Type => NodeType.LabeledStatement;
}
