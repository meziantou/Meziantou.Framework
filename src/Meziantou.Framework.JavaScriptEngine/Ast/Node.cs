namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary>A node of the syntax tree. <see cref="Start"/> and <see cref="End"/> are UTF-16 offsets in the source text.</summary>
internal abstract class Node
{
    /// <summary>Gets or sets the offset of the first character of the node.</summary>
    public int Start { get; set; }

    /// <summary>Gets or sets the offset after the last character of the node.</summary>
    public int End { get; set; }

    /// <summary>Gets the type of the node.</summary>
    public abstract NodeType Type { get; }
}
