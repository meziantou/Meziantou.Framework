using System.Diagnostics.CodeAnalysis;

namespace Meziantou.Framework.JavaScriptEngine.Ast;

/// <summary>A node of the syntax tree. <see cref="Start"/> and <see cref="End"/> are UTF-16 offsets in the source text.</summary>
/// <remarks>
/// The nodes are compared by reference. The compiler keeps its analysis in dictionaries keyed by node: the hash code is a
/// number given when the node is created, cheaper than the identity hash code of the runtime (stored in the object
/// header the first time it is requested).
/// </remarks>
internal abstract class Node
{
    // A race between threads only gives the same hash code to two nodes: it is not a correctness issue
    private static int s_nextHashCode;

    private readonly int _hashCode = s_nextHashCode++;

    /// <summary>Gets or sets the offset of the first character of the node.</summary>
    public int Start { get; set; }

    /// <summary>Gets or sets the offset after the last character of the node.</summary>
    public int End { get; set; }

    /// <summary>Gets the type of the node.</summary>
    public abstract NodeType Type { get; }

    public sealed override bool Equals([NotNullWhen(true)] object? obj) => ReferenceEquals(this, obj);

    public sealed override int GetHashCode() => _hashCode;
}
