namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>case test: consequent</c>, or <c>default:</c> when <see cref="Test"/> is null.</summary>
internal sealed class SwitchCase : Node
{
    public Node? Test { get; set; }

    public List<Node> Consequent { get; set; } = [];

    public override NodeType Type => NodeType.SwitchCase;
}
