namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>new.target</c> or <c>import.meta</c>.</summary>
internal sealed class MetaProperty : Node
{
    public MetaPropertyKind Kind { get; set; }

    public override NodeType Type => NodeType.MetaProperty;
}
