namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary><c>{ properties }</c> as a binding or assignment target.</summary>
internal sealed class ObjectPattern : Node
{
    /// <summary><see cref="Property"/> (whose value is the target) and a final <see cref="RestElement"/>.</summary>
    public List<Node> Properties { get; set; } = [];

    public override NodeType Type => NodeType.ObjectPattern;
}
