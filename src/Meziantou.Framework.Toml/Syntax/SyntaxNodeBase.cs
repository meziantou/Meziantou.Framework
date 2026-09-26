using System.Diagnostics;

namespace Meziantou.Framework.Toml.Syntax;

/// <summary>
/// Base class for <see cref="SyntaxNode"/> and <see cref="SyntaxTrivia"/>
/// </summary>
[DebuggerDisplay("{ToDebuggerDisplay(),nq}")]
public abstract class SyntaxNodeBase
{
    /// <summary>
    /// The text source span, read-write, manually updated from children.
    /// </summary>
#pragma warning disable CA1051 // Public fields are part of the syntax API
    public SourceSpan Span;
#pragma warning restore CA1051

    /// <summary>
    /// Allow to visit this instance with the specified visitor.
    /// </summary>
    /// <param name="visitor">The visitor</param>
    public abstract void Accept(SyntaxVisitor visitor);

    /// <summary>
    /// Gets the parent of this node.
    /// </summary>
    public SyntaxNode? Parent { get; internal set; }

    /// <summary>
    /// Returns a debugger-friendly display string for this node.
    /// </summary>
    /// <returns>A display string used by the debugger.</returns>
    protected virtual string ToDebuggerDisplay() => $"{GetType().Name}";
}
