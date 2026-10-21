namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>switch (discriminant) { cases }</c></summary>
internal sealed class SwitchStatement : Node
{
    public Node Discriminant { get; set; } = null!;

    public List<SwitchCase> Cases { get; set; } = [];

    public override NodeType Type => NodeType.SwitchStatement;
}
