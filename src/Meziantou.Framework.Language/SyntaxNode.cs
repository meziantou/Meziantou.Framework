using System.Diagnostics;
using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language;

/// <summary>
/// A node in a syntax tree: a position, a parent, and a view onto the shared immutable node that holds the structure.
/// </summary>
/// <remarks>
/// <para>
/// Nodes are created on demand as a tree is walked, and each one is created at most once per tree, so reference
/// identity is stable: asking a node for the same child twice returns the same instance.
/// </para>
/// <para>
/// This type cannot be derived from outside the assemblies that make up the language framework. Every syntax tree is
/// produced by a parser that knows how to build the underlying immutable nodes.
/// </para>
/// </remarks>
[DebuggerDisplay("{GetDebuggerDisplay(),nq}")]
public abstract class SyntaxNode
{
    [SuppressMessage("Design", "MA0017:Abstract types should not have public or internal constructors", Justification = "The constructor is internal on purpose: it takes an internal type, so no assembly outside the language framework can derive from this type.")]
    internal SyntaxNode(GreenNode green, SyntaxNode? parent, int position)
    {
        Debug.Assert(position >= 0, "A node cannot start before the text.");

        Green = green;
        Parent = parent;
        Position = position;
    }

    internal GreenNode Green { get; }

    /// <summary>Gets the character offset of the start of this node, including its leading trivia.</summary>
    internal int Position { get; }

    internal int EndPosition => Position + Green.FullWidth;

    /// <summary>Gets the language-specific kind of this node as a plain integer.</summary>
    /// <remarks>Each language exposes this as its own <c>SyntaxKind</c> enum through a <c>Kind()</c> method.</remarks>
    public int RawKind => Green.RawKind;

    /// <summary>Gets the name of the language that produced this node.</summary>
    public string Language => Green.Language;

    /// <summary>Gets the node this one is a child of, or <see langword="null"/> when it is the root.</summary>
    public SyntaxNode? Parent { get; }

    /// <summary>Gets the range this node covers, including the trivia at its outer edges.</summary>
    public TextSpan FullSpan => new(Position, Green.FullWidth);

    /// <summary>Gets the range this node covers, excluding the trivia at its outer edges.</summary>
    public TextSpan Span => new(SpanStart, Green.Width);

    /// <summary>Gets the character offset of the start of this node, excluding its leading trivia.</summary>
    public int SpanStart => Position + Green.GetLeadingTriviaWidth();

    /// <summary>Gets a value indicating whether this node or anything below it carries a diagnostic.</summary>
    public bool ContainsDiagnostics => Green.ContainsDiagnostics;

    /// <summary>Gets a value indicating whether this node or anything below it carries an annotation.</summary>
    public bool ContainsAnnotations => Green.ContainsAnnotations;

    /// <summary>Gets a value indicating whether this node or anything below it came from text the parser could not use.</summary>
    public bool ContainsSkippedText => Green.ContainsSkippedText;

    /// <summary>Gets a value indicating whether the parser inserted this node rather than reading it from the text.</summary>
    public bool IsMissing => Green.IsMissing;

    internal int SlotCount => Green.SlotCount;

    /// <summary>Gets the child node in <paramref name="index"/>, creating it if this is the first request.</summary>
    internal abstract SyntaxNode? GetNodeSlot(int index);

    /// <summary>Gets the child node in <paramref name="index"/> only if it has already been created.</summary>
    internal abstract SyntaxNode? GetCachedSlot(int index);

    /// <summary>Returns the child in slot 0, creating it once and caching it in <paramref name="field"/>.</summary>
    protected T? GetRedAtZero<T>(ref T? field)
        where T : SyntaxNode
        => GetRedCore(ref field, slot: 0, Position);

    /// <summary>Returns the child in <paramref name="slot"/>, creating it once and caching it in <paramref name="field"/>.</summary>
    protected T? GetRed<T>(ref T? field, int slot)
        where T : SyntaxNode
        => field ?? GetRedCore(ref field, slot, GetChildPosition(slot));

    /// <summary>
    /// Returns an element of a list, creating it once and caching it in <paramref name="element"/>.
    /// </summary>
    /// <remarks>
    /// A list is transparent: its elements report the list's own parent, so consumers never see the list node itself.
    /// </remarks>
    internal SyntaxNode GetRedElement(ref SyntaxNode? element, int slot)
    {
        var result = element;
        if (result is null)
        {
            var green = Green.GetRequiredSlot(slot);
            Interlocked.CompareExchange(ref element, green.CreateRed(Parent, GetChildPosition(slot)), null);

            // The value that won the race, not the one this thread created, so every caller sees the same instance.
            result = element;
        }

        return result;
    }

    /// <summary>Gets the position of the child in <paramref name="index"/>.</summary>
    /// <remarks>
    /// Already-created siblings answer this without re-summing widths, so walking a long list stays linear rather
    /// than quadratic.
    /// </remarks>
    internal virtual int GetChildPosition(int index)
    {
        var offset = 0;
        while (index > 0)
        {
            index--;
            if (GetCachedSlot(index) is { } sibling)
                return sibling.EndPosition + offset;

            offset += Green.GetSlot(index)?.FullWidth ?? 0;
        }

        return Position + offset;
    }

    /// <summary>Returns the text of this node, excluding the trivia at its outer edges.</summary>
    public override string ToString() => Green.ToString();

    /// <summary>Returns the text of this node, including the trivia at its outer edges.</summary>
    public virtual string ToFullString() => Green.ToFullString();

    /// <summary>Writes the text of this node, including the trivia at its outer edges.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <see langword="null"/>.</exception>
    public void WriteTo(TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);

        Green.WriteTo(writer);
    }

    /// <summary>Determines whether the two nodes have the same structure and text, ignoring their positions.</summary>
    public bool IsEquivalentTo([NotNullWhen(true)] SyntaxNode? other) => other is not null && Green.IsEquivalentTo(other.Green);

    /// <summary>
    /// Determines whether the two nodes are backed by the very same immutable node, which happens when one tree was
    /// derived from the other and this part of it was left untouched.
    /// </summary>
    public bool IsIncrementallyIdenticalTo([NotNullWhen(true)] SyntaxNode? other) => other is not null && ReferenceEquals(Green, other.Green);

    /// <summary>Determines whether <paramref name="node"/> is this node or sits below it.</summary>
    public bool Contains(SyntaxNode? node)
    {
        while (node is not null)
        {
            if (ReferenceEquals(node, this))
                return true;

            node = node.Parent;
        }

        return false;
    }

    private T? GetRedCore<T>(ref T? field, int slot, int position)
        where T : SyntaxNode
    {
        var result = field;
        if (result is null)
        {
            if (Green.GetSlot(slot) is not { } green)
                return null;

            Interlocked.CompareExchange(ref field, (T)green.CreateRed(this, position), null);

            // The value that won the race, not the one this thread created, so every caller sees the same instance.
            result = field;
        }

        return result;
    }

    private string GetDebuggerDisplay() => $"{GetType().Name} {Green.KindText} {Span}";
}
