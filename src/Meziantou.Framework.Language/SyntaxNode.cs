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

    private SyntaxTree? _syntaxTree;

    internal GreenNode Green { get; }

    /// <summary>Gets the character offset of the start of this node, including its leading trivia.</summary>
    internal int Position { get; }

    internal int EndPosition => Position + Green.FullWidth;

    /// <summary>Gets the language-specific kind of this node as a plain integer.</summary>
    /// <remarks>Each language exposes this as its own <c>SyntaxKind</c> enum through a <c>Kind()</c> method.</remarks>
    public int RawKind => Green.RawKind;

    /// <summary>Gets the node this one is a child of, or <see langword="null"/> when it is the root.</summary>
    public SyntaxNode? Parent { get; }

    /// <summary>
    /// Gets the tree this node belongs to, or <see langword="null"/> when it was built rather than parsed.
    /// </summary>
    public SyntaxTree? SyntaxTree
    {
        get
        {
            var node = this;
            while (node._syntaxTree is null && node.Parent is not null)
            {
                node = node.Parent;
            }

            var tree = node._syntaxTree;
            if (tree is not null && !ReferenceEquals(this, node))
            {
                // Remembered on the way back down, so the next node asked does not repeat the walk.
                Interlocked.CompareExchange(ref _syntaxTree, tree, null);
            }

            return tree;
        }
    }

    internal void AttachToTree(SyntaxTree tree) => Interlocked.CompareExchange(ref _syntaxTree, tree, null);

    /// <summary>Gets the diagnostics at or below this node.</summary>
    public IEnumerable<Diagnostic> GetDiagnostics()
        => Syntax.SyntaxTreeDiagnostics.Enumerate(Green, Position, SyntaxTree?.GetText());

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

    /// <summary>Gets the place of the child in <paramref name="slot"/> among the children, counting a list slot as its own children.</summary>
    /// <remarks>
    /// This is the index a token needs to know where it sits, which is what lets a caller step from it to the next
    /// token without searching for it first.
    /// </remarks>
    internal int GetChildIndex(int slot)
    {
        var index = 0;
        for (var i = 0; i < slot; i++)
        {
            if (Green.GetSlot(i) is { } child)
            {
                index += child.IsList ? child.SlotCount : 1;
            }
        }

        return index;
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

    /// <summary>Gets the children of this node, with the lists among them flattened away.</summary>
    public ChildSyntaxList ChildNodesAndTokens() => new(this);

    /// <summary>Returns the first token below this node, or <see cref="SyntaxToken.None"/> when it has none.</summary>
    public SyntaxToken GetFirstToken()
    {
        var node = this;
        while (true)
        {
            SyntaxNode? next = null;
            foreach (var child in node.ChildNodesAndTokens())
            {
                if (child.AsToken(out var token))
                    return token;

                if (child.AsNode(out var childNode) && childNode.Green.GetFirstTerminal() is not null)
                {
                    next = childNode;
                    break;
                }
            }

            if (next is null)
                return default;

            node = next;
        }
    }

    /// <summary>Returns the last token below this node, or <see cref="SyntaxToken.None"/> when it has none.</summary>
    public SyntaxToken GetLastToken()
    {
        var node = this;
        while (true)
        {
            SyntaxNode? next = null;
            foreach (var child in node.ChildNodesAndTokens().Reverse())
            {
                if (child.AsToken(out var token))
                    return token;

                if (child.AsNode(out var childNode) && childNode.Green.GetLastTerminal() is not null)
                {
                    next = childNode;
                    break;
                }
            }

            if (next is null)
                return default;

            node = next;
        }
    }

    /// <summary>Gets the trivia in front of this node, which is the leading trivia of its first token.</summary>
    public SyntaxTriviaList GetLeadingTrivia() => GetFirstToken().LeadingTrivia;

    /// <summary>Gets the trivia after this node, which is the trailing trivia of its last token.</summary>
    public SyntaxTriviaList GetTrailingTrivia() => GetLastToken().TrailingTrivia;

    /// <summary>Gets the child nodes of this node, skipping the tokens among them.</summary>
    public IEnumerable<SyntaxNode> ChildNodes()
    {
        foreach (var child in ChildNodesAndTokens())
        {
            if (child.AsNode(out var node))
            {
                yield return node;
            }
        }
    }

    /// <summary>Gets the child tokens of this node, skipping the nodes among them.</summary>
    public IEnumerable<SyntaxToken> ChildTokens()
    {
        foreach (var child in ChildNodesAndTokens())
        {
            if (child.AsToken(out var token))
            {
                yield return token;
            }
        }
    }

    /// <summary>Gets every node below this one, parents before children.</summary>
    /// <param name="descendIntoChildren">Decides whether to look inside a node. Everything is looked inside when omitted.</param>
    public IEnumerable<SyntaxNode> DescendantNodes(Func<SyntaxNode, bool>? descendIntoChildren = null)
        => DescendantNodesCore(span: null, descendIntoChildren, includeSelf: false);

    /// <summary>Gets every node below this one that touches <paramref name="span"/>, parents before children.</summary>
    public IEnumerable<SyntaxNode> DescendantNodes(TextSpan span, Func<SyntaxNode, bool>? descendIntoChildren = null)
        => DescendantNodesCore(span, descendIntoChildren, includeSelf: false);

    /// <summary>Gets this node and every node below it, parents before children.</summary>
    public IEnumerable<SyntaxNode> DescendantNodesAndSelf(Func<SyntaxNode, bool>? descendIntoChildren = null)
        => DescendantNodesCore(span: null, descendIntoChildren, includeSelf: true);

    /// <summary>Gets this node and every node below it that touches <paramref name="span"/>, parents before children.</summary>
    public IEnumerable<SyntaxNode> DescendantNodesAndSelf(TextSpan span, Func<SyntaxNode, bool>? descendIntoChildren = null)
        => DescendantNodesCore(span, descendIntoChildren, includeSelf: true);

    /// <summary>Gets every node and token below this one, in source order.</summary>
    public IEnumerable<SyntaxNodeOrToken> DescendantNodesAndTokens(Func<SyntaxNode, bool>? descendIntoChildren = null)
        => DescendantNodesAndTokensCore(span: null, descendIntoChildren);

    /// <summary>Gets every node and token below this one that touches <paramref name="span"/>, in source order.</summary>
    public IEnumerable<SyntaxNodeOrToken> DescendantNodesAndTokens(TextSpan span, Func<SyntaxNode, bool>? descendIntoChildren = null)
        => DescendantNodesAndTokensCore(span, descendIntoChildren);

    /// <summary>Gets every token below this one, in source order.</summary>
    public IEnumerable<SyntaxToken> DescendantTokens(Func<SyntaxNode, bool>? descendIntoChildren = null)
        => DescendantTokensCore(span: null, descendIntoChildren);

    /// <summary>Gets every token below this one that touches <paramref name="span"/>, in source order.</summary>
    public IEnumerable<SyntaxToken> DescendantTokens(TextSpan span, Func<SyntaxNode, bool>? descendIntoChildren = null)
        => DescendantTokensCore(span, descendIntoChildren);

    /// <summary>Gets every trivia below this node, in source order.</summary>
    public IEnumerable<SyntaxTrivia> DescendantTrivia(Func<SyntaxNode, bool>? descendIntoChildren = null)
        => DescendantTriviaCore(span: null, descendIntoChildren);

    /// <summary>Gets every trivia below this node that touches <paramref name="span"/>, in source order.</summary>
    public IEnumerable<SyntaxTrivia> DescendantTrivia(TextSpan span, Func<SyntaxNode, bool>? descendIntoChildren = null)
        => DescendantTriviaCore(span, descendIntoChildren);

    /// <summary>Gets the nodes this one sits inside, closest first.</summary>
    public IEnumerable<SyntaxNode> Ancestors()
    {
        var parent = Parent;
        while (parent is not null)
        {
            yield return parent;
            parent = parent.Parent;
        }
    }

    /// <summary>Gets this node and the nodes it sits inside, closest first.</summary>
    public IEnumerable<SyntaxNode> AncestorsAndSelf()
    {
        SyntaxNode? node = this;
        while (node is not null)
        {
            yield return node;
            node = node.Parent;
        }
    }

    /// <summary>Finds the closest node of type <typeparamref name="TNode"/>, starting with this one.</summary>
    public TNode? FirstAncestorOrSelf<TNode>(Func<TNode, bool>? predicate = null)
        where TNode : SyntaxNode
    {
        foreach (var node in AncestorsAndSelf())
        {
            if (node is TNode match && (predicate is null || predicate(match)))
                return match;
        }

        return null;
    }

    /// <summary>Gets the child whose full span contains <paramref name="position"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="position"/> is outside this node.</exception>
    public SyntaxNodeOrToken ChildThatContainsPosition(int position)
    {
        if (!FullSpan.Contains(position))
            throw new ArgumentOutOfRangeException(nameof(position));

        return ChildSyntaxList.ChildThatContainsPosition(this, position, out _);
    }

    /// <summary>Finds the token at <paramref name="position"/>.</summary>
    /// <param name="position">A position inside this node, or its very end.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="position"/> is outside this node.</exception>
    public SyntaxToken FindToken(int position)
    {
        // The end of the text is where a zero-width end-of-file token sits, and it is the answer callers expect there.
        if (position == FullSpan.End)
            return GetLastToken();

        if (!FullSpan.Contains(position))
            throw new ArgumentOutOfRangeException(nameof(position));

        var node = this;
        while (true)
        {
            var child = ChildSyntaxList.ChildThatContainsPosition(node, position, out _);
            if (child.AsToken(out var token))
                return token;

            if (!child.AsNode(out var childNode))
                return default;

            node = childNode;
        }
    }

    /// <summary>Finds the trivia at <paramref name="position"/>, or nothing when the position falls on a token's own text.</summary>
    public SyntaxTrivia FindTrivia(int position)
    {
        var token = FindToken(position);
        foreach (var trivia in token.LeadingTrivia)
        {
            if (trivia.Span.Contains(position))
                return trivia;
        }

        foreach (var trivia in token.TrailingTrivia)
        {
            if (trivia.Span.Contains(position))
                return trivia;
        }

        return default;
    }

    /// <summary>Finds the smallest node whose full span covers <paramref name="span"/>.</summary>
    /// <param name="span">A range inside this node.</param>
    /// <param name="getInnermostNodeForTie">
    /// When several nested nodes cover exactly the same range, whether to return the innermost rather than the outermost.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="span"/> is not inside this node.</exception>
    public SyntaxNode FindNode(TextSpan span, bool getInnermostNodeForTie = false)
    {
        if (!FullSpan.Contains(span))
            throw new ArgumentOutOfRangeException(nameof(span));

        var node = FindToken(span.Start).Parent ?? this;
        while (node.Parent is not null && !node.FullSpan.Contains(span))
        {
            node = node.Parent;
        }

        if (getInnermostNodeForTie)
        {
            while (true)
            {
                SyntaxNode? only = null;
                foreach (var child in node.ChildNodes())
                {
                    if (child.FullSpan == node.FullSpan)
                    {
                        only = child;
                        break;
                    }
                }

                if (only is null)
                    break;

                node = only;
            }
        }

        return node;
    }

    /// <summary>Gets the annotations of this node itself, of the given kind.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="annotationKind"/> is <see langword="null"/>.</exception>
    public IEnumerable<SyntaxAnnotation> GetAnnotations(string annotationKind)
    {
        ArgumentNullException.ThrowIfNull(annotationKind);

        return Green.GetAnnotations().Where(annotation => string.Equals(annotation.Kind, annotationKind, StringComparison.Ordinal));
    }

    /// <summary>Gets all the annotations of this node itself.</summary>
    public IEnumerable<SyntaxAnnotation> GetAnnotations() => Green.GetAnnotations();

    /// <summary>Determines whether this node itself carries <paramref name="annotation"/>.</summary>
    public bool HasAnnotation(SyntaxAnnotation? annotation) => annotation is not null && Array.IndexOf(Green.GetAnnotations(), annotation) >= 0;

    /// <summary>Determines whether this node itself carries an annotation of the given kind.</summary>
    public bool HasAnnotations(string annotationKind) => GetAnnotations(annotationKind).Any();

    /// <summary>Gets everything at or below this node carrying <paramref name="annotation"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="annotation"/> is <see langword="null"/>.</exception>
    public IEnumerable<SyntaxNodeOrToken> GetAnnotatedNodesAndTokens(SyntaxAnnotation annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);

        return GetAnnotatedNodesAndTokensCore(annotations => Array.IndexOf(annotations, annotation) >= 0);
    }

    /// <summary>Gets everything at or below this node carrying an annotation of the given kind.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="annotationKind"/> is <see langword="null"/>.</exception>
    public IEnumerable<SyntaxNodeOrToken> GetAnnotatedNodesAndTokens(string annotationKind)
    {
        ArgumentNullException.ThrowIfNull(annotationKind);

        return GetAnnotatedNodesAndTokensCore(annotations => Array.Exists(annotations, annotation => string.Equals(annotation.Kind, annotationKind, StringComparison.Ordinal)));
    }

    /// <summary>Gets the nodes at or below this one carrying <paramref name="annotation"/>.</summary>
    public IEnumerable<SyntaxNode> GetAnnotatedNodes(SyntaxAnnotation annotation) => OnlyNodes(GetAnnotatedNodesAndTokens(annotation));

    /// <summary>Gets the nodes at or below this one carrying an annotation of the given kind.</summary>
    public IEnumerable<SyntaxNode> GetAnnotatedNodes(string annotationKind) => OnlyNodes(GetAnnotatedNodesAndTokens(annotationKind));

    /// <summary>Gets the tokens below this node carrying <paramref name="annotation"/>.</summary>
    public IEnumerable<SyntaxToken> GetAnnotatedTokens(SyntaxAnnotation annotation) => OnlyTokens(GetAnnotatedNodesAndTokens(annotation));

    /// <summary>Gets the tokens below this node carrying an annotation of the given kind.</summary>
    public IEnumerable<SyntaxToken> GetAnnotatedTokens(string annotationKind) => OnlyTokens(GetAnnotatedNodesAndTokens(annotationKind));

    /// <summary>Gets the trivia below this node carrying <paramref name="annotation"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="annotation"/> is <see langword="null"/>.</exception>
    public IEnumerable<SyntaxTrivia> GetAnnotatedTrivia(SyntaxAnnotation annotation)
    {
        ArgumentNullException.ThrowIfNull(annotation);

        return GetAnnotatedTriviaCore(trivia => trivia.HasAnnotation(annotation));
    }

    /// <summary>Gets the trivia below this node carrying an annotation of the given kind.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="annotationKind"/> is <see langword="null"/>.</exception>
    public IEnumerable<SyntaxTrivia> GetAnnotatedTrivia(string annotationKind)
    {
        ArgumentNullException.ThrowIfNull(annotationKind);

        return GetAnnotatedTriviaCore(trivia => trivia.HasAnnotations(annotationKind));
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

    private IEnumerable<SyntaxNode> DescendantNodesCore(TextSpan? span, Func<SyntaxNode, bool>? descendIntoChildren, bool includeSelf)
    {
        foreach (var item in DescendantNodesAndTokensCore(span, descendIntoChildren, includeSelf))
        {
            if (item.AsNode(out var node))
            {
                yield return node;
            }
        }
    }

    private IEnumerable<SyntaxToken> DescendantTokensCore(TextSpan? span, Func<SyntaxNode, bool>? descendIntoChildren)
    {
        foreach (var item in DescendantNodesAndTokensCore(span, descendIntoChildren, includeSelf: false))
        {
            if (item.AsToken(out var token))
            {
                yield return token;
            }
        }
    }

    private IEnumerable<SyntaxTrivia> DescendantTriviaCore(TextSpan? span, Func<SyntaxNode, bool>? descendIntoChildren)
    {
        foreach (var token in DescendantTokensCore(span, descendIntoChildren))
        {
            foreach (var trivia in token.LeadingTrivia)
            {
                if (span is null || trivia.Span.IntersectsWith(span.Value))
                {
                    yield return trivia;
                }
            }

            foreach (var trivia in token.TrailingTrivia)
            {
                if (span is null || trivia.Span.IntersectsWith(span.Value))
                {
                    yield return trivia;
                }
            }
        }
    }

    /// <remarks>
    /// The walk keeps its own stack rather than recursing, so the depth of the document does not become the depth of
    /// the call stack.
    /// </remarks>
    private IEnumerable<SyntaxNodeOrToken> DescendantNodesAndTokensCore(TextSpan? span, Func<SyntaxNode, bool>? descendIntoChildren, bool includeSelf = false)
    {
        if (includeSelf && Matches(this))
        {
            yield return this;
        }

        var stack = new Stack<ChildSyntaxList.Enumerator>();
        stack.Push(ChildNodesAndTokens().GetEnumerator());

        while (stack.Count > 0)
        {
            var enumerator = stack.Pop();
            if (!enumerator.MoveNext())
                continue;

            var child = enumerator.Current;
            stack.Push(enumerator);

            if (span is not null && !child.FullSpan.IntersectsWith(span.Value))
                continue;

            yield return child;

            if (child.AsNode(out var node) && (descendIntoChildren is null || descendIntoChildren(node)))
            {
                stack.Push(node.ChildNodesAndTokens().GetEnumerator());
            }
        }

        bool Matches(SyntaxNode node) => span is null || node.FullSpan.IntersectsWith(span.Value);
    }

    /// <remarks>
    /// A subtree that carries no annotation anywhere is skipped whole, which is what keeps the search proportional to
    /// the annotated part of the tree rather than to its size.
    /// </remarks>
    private IEnumerable<SyntaxNodeOrToken> GetAnnotatedNodesAndTokensCore(Func<SyntaxAnnotation[], bool> matches)
    {
        if (!ContainsAnnotations)
            yield break;

        var stack = new Stack<SyntaxNodeOrToken>();
        stack.Push(this);

        while (stack.TryPop(out var item))
        {
            if (!item.ContainsAnnotations)
                continue;

            if (item.UnderlyingNode is { } green && matches(green.GetAnnotations()))
            {
                yield return item;
            }

            if (item.AsNode(out var node))
            {
                foreach (var child in node.ChildNodesAndTokens().Reverse())
                {
                    stack.Push(child);
                }
            }
        }
    }

    private IEnumerable<SyntaxTrivia> GetAnnotatedTriviaCore(Func<SyntaxTrivia, bool> matches)
    {
        if (!ContainsAnnotations)
            yield break;

        foreach (var token in DescendantTokens(node => node.ContainsAnnotations))
        {
            if (!token.ContainsAnnotations)
                continue;

            foreach (var trivia in token.LeadingTrivia.Concat(token.TrailingTrivia))
            {
                if (matches(trivia))
                {
                    yield return trivia;
                }
            }
        }
    }

    private static IEnumerable<SyntaxNode> OnlyNodes(IEnumerable<SyntaxNodeOrToken> items)
    {
        foreach (var item in items)
        {
            if (item.AsNode(out var node))
            {
                yield return node;
            }
        }
    }

    private static IEnumerable<SyntaxToken> OnlyTokens(IEnumerable<SyntaxNodeOrToken> items)
    {
        foreach (var item in items)
        {
            if (item.AsToken(out var token))
            {
                yield return token;
            }
        }
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
