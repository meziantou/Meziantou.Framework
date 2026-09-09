using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Language.InternalSyntax;

/// <summary>
/// The immutable, position-independent half of a syntax tree.
/// </summary>
/// <remarks>
/// <para>
/// A green node stores its <em>width</em>, never its position, so the same instance can appear at any offset in any
/// number of trees. That is what makes structural sharing possible: an edit rebuilds only the spine from the changed
/// node up to the root, and every untouched subtree is carried over by reference.
/// </para>
/// <para>
/// Each concrete node exposes its children as numbered <em>slots</em>. A slot may be empty, and a slot may hold a
/// list, which the red layer flattens when it presents children. Constructors must call
/// <see cref="AdjustFlagsAndWidth"/> for every slot, in slot order: that is what accumulates
/// <see cref="FullWidth"/> and propagates the flags upward.
/// </para>
/// </remarks>
[DebuggerDisplay("{KindText,nq} {ToStringForDebugger(),nq}")]
internal abstract class GreenNode
{
    /// <summary>The kind every language shares for a list of children.</summary>
    internal const int ListKind = 1;

    private static readonly ConditionalWeakTable<GreenNode, SyntaxDiagnosticInfo[]> DiagnosticsTable = new();
    private static readonly ConditionalWeakTable<GreenNode, SyntaxAnnotation[]> AnnotationsTable = new();
    private static readonly SyntaxDiagnosticInfo[] NoDiagnostics = [];
    private static readonly SyntaxAnnotation[] NoAnnotations = [];

    private NodeFlags _flags;

    protected GreenNode(int rawKind) => RawKind = rawKind;

    protected GreenNode(int rawKind, int fullWidth)
        : this(rawKind) => FullWidth = fullWidth;

    protected GreenNode(int rawKind, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : this(rawKind)
    {
        if (diagnostics is { Length: > 0 })
        {
            _flags |= NodeFlags.ContainsDiagnostics;
            DiagnosticsTable.Add(this, diagnostics);
        }

        if (annotations is { Length: > 0 })
        {
            _flags |= NodeFlags.ContainsAnnotations;
            AnnotationsTable.Add(this, annotations);
        }
    }

    /// <summary>Properties a node inherits from its children.</summary>
    [Flags]
    internal enum NodeFlags : byte
    {
        None = 0,
        ContainsDiagnostics = 1 << 0,
        ContainsAnnotations = 1 << 1,
        ContainsSkippedText = 1 << 2,

        /// <summary>
        /// Set on any terminal that came from real source text. It is positive rather than negative so that
        /// <see cref="AdjustFlagsAndWidth"/> can OR it upward: a node is missing only when everything below it is.
        /// </summary>
        IsNotMissing = 1 << 3,

        InheritMask = ContainsDiagnostics | ContainsAnnotations | ContainsSkippedText | IsNotMissing,
    }

    public int RawKind { get; }

    /// <summary>Gets the name of <see cref="RawKind"/>, for diagnostics and the debugger.</summary>
    /// <remarks>
    /// Each language overrides this once, on the base class of its own nodes, to return the name of its kind enum
    /// member. The shared token, trivia, and list types are not tied to a language and keep the numeric default.
    /// </remarks>
    public virtual string KindText => RawKind.ToString(CultureInfo.InvariantCulture);

    public virtual bool IsToken => false;
    public virtual bool IsTrivia => false;
    public bool IsList => RawKind == ListKind;

    /// <summary>Gets the number of characters this node covers, including the trivia at both of its ends.</summary>
    public int FullWidth { get; protected set; }

    /// <summary>Gets the number of characters this node covers, excluding the trivia at both of its ends.</summary>
    public virtual int Width => FullWidth - GetLeadingTriviaWidth() - GetTrailingTriviaWidth();

    public virtual int GetLeadingTriviaWidth() => FullWidth == 0 ? 0 : GetFirstTerminal()?.GetLeadingTriviaWidth() ?? 0;
    public virtual int GetTrailingTriviaWidth() => FullWidth == 0 ? 0 : GetLastTerminal()?.GetTrailingTriviaWidth() ?? 0;

    public int SlotCount { get; protected set; }

    /// <summary>Gets the child in <paramref name="index"/>, which may be empty.</summary>
    internal abstract GreenNode? GetSlot(int index);

    internal GreenNode GetRequiredSlot(int index)
    {
        var slot = GetSlot(index);
        Debug.Assert(slot is not null, "The slot was expected to be occupied.");

        return slot;
    }

    /// <summary>Gets the offset of slot <paramref name="index"/>, relative to the full start of this node.</summary>
    internal virtual int GetSlotOffset(int index)
    {
        var offset = 0;
        for (var i = 0; i < index; i++)
        {
            offset += GetSlot(i)?.FullWidth ?? 0;
        }

        return offset;
    }

    /// <summary>Returns a copy of this node whose slots are <paramref name="slots"/>.</summary>
    /// <remarks>
    /// This is what lets the shared replace machinery rebuild any language's tree without knowing its node types.
    /// An implementation must consume the slots in the same order <see cref="GetSlot"/> produces them. A list whose
    /// last child was removed returns <see langword="null"/>, which the caller reads as an empty slot.
    /// </remarks>
    internal abstract GreenNode? WithSlots(ReadOnlySpan<GreenNode?> slots);

    public bool ContainsDiagnostics => (_flags & NodeFlags.ContainsDiagnostics) != NodeFlags.None;
    public bool ContainsAnnotations => (_flags & NodeFlags.ContainsAnnotations) != NodeFlags.None;
    public bool ContainsSkippedText => (_flags & NodeFlags.ContainsSkippedText) != NodeFlags.None;
    public bool IsMissing => (_flags & NodeFlags.IsNotMissing) == NodeFlags.None;

    /// <summary>Gets a value indicating whether the node is anonymous enough to be shared between trees and positions.</summary>
    internal bool IsCacheable => (_flags & NodeFlags.InheritMask) == NodeFlags.IsNotMissing;

    internal NodeFlags Flags => _flags;

    /// <summary>Accumulates a slot's width and inheritable flags into this node. Must be called for every slot, in slot order.</summary>
    protected void AdjustFlagsAndWidth(GreenNode? node)
    {
        if (node is null)
            return;

        _flags |= node._flags & NodeFlags.InheritMask;
        FullWidth += node.FullWidth;
    }

    protected void SetFlags(NodeFlags flags) => _flags |= flags;
    protected void ClearFlags(NodeFlags flags) => _flags &= ~flags;

    public SyntaxDiagnosticInfo[] GetDiagnostics()
        => ContainsDiagnostics && DiagnosticsTable.TryGetValue(this, out var diagnostics) ? diagnostics : NoDiagnostics;

    public SyntaxAnnotation[] GetAnnotations()
        => ContainsAnnotations && AnnotationsTable.TryGetValue(this, out var annotations) ? annotations : NoAnnotations;

    /// <summary>Returns a copy of this node carrying <paramref name="diagnostics"/> instead of its own.</summary>
    internal abstract GreenNode SetDiagnostics(SyntaxDiagnosticInfo[]? diagnostics);

    /// <summary>Returns a copy of this node carrying <paramref name="annotations"/> instead of its own.</summary>
    internal abstract GreenNode SetAnnotations(SyntaxAnnotation[]? annotations);

    internal GreenNode WithAdditionalDiagnostics(params SyntaxDiagnosticInfo[] diagnostics)
    {
        if (diagnostics.Length == 0)
            return this;

        return SetDiagnostics([.. GetDiagnostics(), .. diagnostics]);
    }

    internal GreenNode WithAdditionalAnnotations(params SyntaxAnnotation[] annotations)
    {
        if (annotations.Length == 0)
            return this;

        var existing = GetAnnotations();
        var added = annotations.Where(annotation => !existing.Contains(annotation)).ToArray();
        if (added.Length == 0)
            return this;

        return SetAnnotations([.. existing, .. added]);
    }

    internal GreenNode WithoutAnnotations(params SyntaxAnnotation[] annotations)
    {
        var existing = GetAnnotations();
        if (existing.Length == 0)
            return this;

        var remaining = existing.Where(annotation => !annotations.Contains(annotation)).ToArray();
        if (remaining.Length == existing.Length)
            return this;

        return SetAnnotations(remaining.Length == 0 ? null : remaining);
    }

    /// <summary>Returns the leftmost node with no children, which is this node when it is itself a terminal.</summary>
    public GreenNode? GetFirstTerminal()
    {
        var node = this;
        while (node.SlotCount > 0)
        {
            GreenNode? child = null;
            for (var i = 0; i < node.SlotCount; i++)
            {
                child = node.GetSlot(i);
                if (child is not null)
                    break;
            }

            if (child is null)
                return null;

            node = child;
        }

        return node;
    }

    /// <summary>Returns the rightmost node with no children, which is this node when it is itself a terminal.</summary>
    public GreenNode? GetLastTerminal()
    {
        var node = this;
        while (node.SlotCount > 0)
        {
            GreenNode? child = null;
            for (var i = node.SlotCount - 1; i >= 0; i--)
            {
                child = node.GetSlot(i);
                if (child is not null)
                    break;
            }

            if (child is null)
                return null;

            node = child;
        }

        return node;
    }

    /// <summary>Gets the source text of a terminal, or <see langword="null"/> when this node has children.</summary>
    internal virtual string? TerminalText => null;

    /// <summary>Gets the value a token carries, which is its text unless the language decoded something else.</summary>
    internal virtual object? GetValue() => null;

    /// <summary>
    /// Determines whether <paramref name="trivia"/> ends a line in this language.
    /// </summary>
    /// <remarks>
    /// Asked of a node, about one of the trivia below it, because a trivium is shared across languages and so does
    /// not know which kind number its own language uses for a line break. Each language overrides this once, on the
    /// base class of its own nodes.
    /// </remarks>
    internal virtual bool IsEndOfLineTrivia(GreenNode trivia) => false;

    /// <summary>
    /// Creates the token a language puts between the elements of a separated list, or <see langword="null"/> when it
    /// has none.
    /// </summary>
    /// <remarks>
    /// Adding an element to a separated list needs a separator to go with it, and only the language knows what that
    /// looks like. Each language overrides this once, on the base class of its own nodes.
    /// </remarks>
    internal virtual GreenNode? CreateSeparator() => null;

    public virtual void WriteTo(TextWriter writer) => WriteTo(writer, leading: true, trailing: true);

    /// <summary>Writes this node, optionally including the trivia at its outer edges.</summary>
    /// <remarks>The walk is iterative so a deeply nested document cannot overflow the stack.</remarks>
    protected internal virtual void WriteTo(TextWriter writer, bool leading, bool trailing)
    {
        Debug.Assert(SlotCount > 0, "A node with no slots must override WriteTo.");

        var stack = new Stack<(GreenNode Node, bool Leading, bool Trailing)>();
        stack.Push((this, leading, trailing));

        while (stack.TryPop(out var entry))
        {
            var node = entry.Node;
            if (node.SlotCount == 0)
            {
                node.WriteTo(writer, entry.Leading, entry.Trailing);
                continue;
            }

            var firstIndex = -1;
            var lastIndex = -1;
            for (var i = 0; i < node.SlotCount; i++)
            {
                if (node.GetSlot(i) is null)
                    continue;

                if (firstIndex < 0)
                {
                    firstIndex = i;
                }

                lastIndex = i;
            }

            // Pushed in reverse so the children are written left to right. Only the outermost child on each side
            // sees the caller's flags; trivia between children is always written.
            for (var i = lastIndex; i >= firstIndex; i--)
            {
                var child = node.GetSlot(i);
                if (child is null)
                    continue;

                stack.Push((child, entry.Leading || i != firstIndex, entry.Trailing || i != lastIndex));
            }
        }
    }

    /// <summary>Returns the text of this node without the trivia at its outer edges.</summary>
    public sealed override string ToString() => WriteToString(leading: false, trailing: false);

    /// <summary>Returns the text of this node including the trivia at its outer edges.</summary>
    public virtual string ToFullString() => WriteToString(leading: true, trailing: true);

    /// <summary>Creates the red node that presents this node at <paramref name="position"/> under <paramref name="parent"/>.</summary>
    internal abstract SyntaxNode CreateRed(SyntaxNode? parent, int position);

    internal SyntaxNode CreateRed() => CreateRed(parent: null, position: 0);

    /// <summary>
    /// Determines whether the two nodes have the same structure and text, ignoring trivia, diagnostics and annotations.
    /// </summary>
    /// <remarks>
    /// Trivia takes no part, so two nodes written with different whitespace or comments around the same tokens are
    /// equivalent. Use <see cref="SyntaxNode.IsIncrementallyIdenticalTo"/> to ask whether they are the very same node.
    /// </remarks>
    public bool IsEquivalentTo(GreenNode? other)
    {
        if (ReferenceEquals(this, other))
            return true;

        if (other is null)
            return false;

        var stack = new Stack<(GreenNode Left, GreenNode Right)>();
        stack.Push((this, other));

        while (stack.TryPop(out var pair))
        {
            var (left, right) = pair;
            if (ReferenceEquals(left, right))
                continue;

            if (left.RawKind != right.RawKind || left.SlotCount != right.SlotCount)
                return false;

            if (left.IsMissing != right.IsMissing)
                return false;

            if (!string.Equals(left.TerminalText, right.TerminalText, StringComparison.Ordinal))
                return false;

            for (var i = 0; i < left.SlotCount; i++)
            {
                var leftChild = left.GetSlot(i);
                var rightChild = right.GetSlot(i);
                if (leftChild is null || rightChild is null)
                {
                    if (leftChild is not null || rightChild is not null)
                        return false;

                    continue;
                }

                stack.Push((leftChild, rightChild));
            }
        }

        return true;
    }

    private string WriteToString(bool leading, bool trailing)
    {
        var builder = new StringBuilder(FullWidth);
        using var writer = new StringWriter(builder, CultureInfo.InvariantCulture);
        WriteTo(writer, leading, trailing);

        return builder.ToString();
    }

    private string ToStringForDebugger()
    {
        const int MaxLength = 40;
        var text = ToFullString();

        return text.Length <= MaxLength ? text : string.Concat(text.AsSpan(0, MaxLength), "...");
    }
}
