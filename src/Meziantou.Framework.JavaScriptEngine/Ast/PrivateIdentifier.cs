namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>#name</c> (<see cref="Name"/> is without the '#').</summary>
internal sealed class PrivateIdentifier : Node
{
    public string Name { get; set; } = "";

    public override NodeType Type => NodeType.PrivateIdentifier;
}
