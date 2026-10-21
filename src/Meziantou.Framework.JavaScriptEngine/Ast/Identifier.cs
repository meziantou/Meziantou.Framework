namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary>An identifier reference, a binding identifier or a label (<see cref="Name"/> has its escapes decoded).</summary>
internal sealed class Identifier : Node
{
    public string Name { get; set; } = "";

    public override NodeType Type => NodeType.Identifier;
}
