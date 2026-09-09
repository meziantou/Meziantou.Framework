using System.Collections;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language;

/// <summary>A sequence of nodes with a separator token between each pair, and optionally one after the last.</summary>
/// <typeparam name="TNode">The type of the nodes in the list.</typeparam>
/// <remarks>
/// <para>
/// Elements and separators share one underlying sequence, alternating: even positions hold nodes, odd positions hold
/// separators. A list that ends on a separator therefore has as many separators as elements, which is how a trailing
/// comma is represented without any extra machinery.
/// </para>
/// <para>
/// The alternation is the parser's responsibility. A parser that has nothing to put between two separators must
/// insert a missing node, or reading the list will fail on a separator where an element was expected.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly struct SeparatedSyntaxList<TNode> : IReadOnlyList<TNode>, IEquatable<SeparatedSyntaxList<TNode>>
    where TNode : SyntaxNode
{
    private readonly SyntaxNodeOrTokenList _list;

    internal SeparatedSyntaxList(SyntaxNodeOrTokenList list)
    {
        ValidateAlternation(list);

        _list = list;
        Count = (list.Count + 1) >> 1;
        SeparatorCount = list.Count >> 1;
    }

    /// <summary>Creates a detached list from <paramref name="nodesAndSeparators"/>, which must alternate.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="nodesAndSeparators"/> is <see langword="null"/>.</exception>
    public SeparatedSyntaxList(IEnumerable<SyntaxNodeOrToken> nodesAndSeparators)
        : this(new SyntaxNodeOrTokenList(nodesAndSeparators))
    {
    }

    internal SyntaxNode? Node => _list.Node;
    internal GreenNode? Green => _list.Green;

    /// <summary>Gets the number of nodes, not counting separators.</summary>
    public int Count { get; }

    /// <summary>Gets the number of separators, which equals <see cref="Count"/> when the list ends on one.</summary>
    public int SeparatorCount { get; }

    /// <summary>Gets a value indicating whether a separator follows the last node, as in <c>[1, 2, ]</c>.</summary>
    public bool HasTrailingSeparator => SeparatorCount == Count && Count > 0;

    public TNode this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            return (TNode)_list[index << 1].AsNode()!;
        }
    }

    /// <summary>Gets the separator that follows the node at <paramref name="index"/>.</summary>
    public SyntaxToken GetSeparator(int index)
    {
        if ((uint)index >= (uint)SeparatorCount)
            throw new ArgumentOutOfRangeException(nameof(index));

        return _list[(index << 1) + 1].AsToken();
    }

    /// <summary>Gets the separators of this list.</summary>
    public IEnumerable<SyntaxToken> GetSeparators()
    {
        for (var i = 0; i < SeparatorCount; i++)
        {
            yield return GetSeparator(i);
        }
    }

    /// <summary>Gets the nodes and the separators together, in source order.</summary>
    public SyntaxNodeOrTokenList GetWithSeparators() => _list;

    public TextSpan FullSpan => _list.FullSpan;
    public TextSpan Span => _list.Span;

    public bool Any() => Count > 0;
    public TNode? First() => Count > 0 ? this[0] : throw new InvalidOperationException("The list is empty.");
    public TNode? Last() => Count > 0 ? this[Count - 1] : throw new InvalidOperationException("The list is empty.");
    public TNode? FirstOrDefault() => Count > 0 ? this[0] : null;
    public TNode? LastOrDefault() => Count > 0 ? this[Count - 1] : null;

    public int IndexOf(TNode node)
    {
        for (var i = 0; i < Count; i++)
        {
            if (ReferenceEquals(this[i], node))
                return i;
        }

        return -1;
    }

    /// <summary>Finds the first node matching <paramref name="predicate"/>, or -1.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="predicate"/> is <see langword="null"/>.</exception>
    public int IndexOf(Func<TNode, bool> predicate)
    {
        ArgumentNullException.ThrowIfNull(predicate);

        for (var i = 0; i < Count; i++)
        {
            if (predicate(this[i]))
                return i;
        }

        return -1;
    }

    /// <summary>Returns a detached list with <paramref name="node"/> appended, adding a separator when one is needed.</summary>
    public SeparatedSyntaxList<TNode> Add(TNode node) => Insert(Count, node);

    /// <summary>Returns a detached list with <paramref name="nodes"/> appended.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="nodes"/> is <see langword="null"/>.</exception>
    public SeparatedSyntaxList<TNode> AddRange(IEnumerable<TNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        var result = this;
        foreach (var node in nodes)
        {
            result = result.Add(node);
        }

        return result;
    }

    /// <summary>
    /// Returns a detached list with <paramref name="node"/> at <paramref name="index"/>, adding the separator the
    /// alternation needs.
    /// </summary>
    /// <exception cref="ArgumentNullException"><paramref name="node"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">The language does not define a separator for its lists.</exception>
    public SeparatedSyntaxList<TNode> Insert(int index, TNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(index, Count, nameof(index));

        if (Count == 0)
            return new SeparatedSyntaxList<TNode>(new SyntaxNodeOrTokenList([node]));

        var separator = CreateSeparator(node);
        var items = new List<SyntaxNodeOrToken>(_list);

        if (index == Count)
        {
            // A separator already at the end becomes an interior one, so only add another when there is none.
            if (!HasTrailingSeparator)
            {
                items.Add(separator);
            }

            items.Add(node);
        }
        else
        {
            items.Insert(index << 1, separator);
            items.Insert(index << 1, node);
        }

        return new SeparatedSyntaxList<TNode>(new SyntaxNodeOrTokenList(items));
    }

    /// <summary>The separator to put between the elements when one is added.</summary>
    /// <remarks>
    /// A list that already holds one says what it looks like, spacing and all. One that does not -- a list of a
    /// single element, which is where this matters -- asks the node that owns the slot, because the same language
    /// separates different lists differently: a shell joins the commands of a pipeline with <c>|</c> and the
    /// statements of a list with <c>;</c>, and taking the element's language-wide default would quietly turn one
    /// into the other.
    /// </remarks>
    private SyntaxNodeOrToken CreateSeparator(TNode node)
    {
        if (SeparatorCount > 0)
            return GetSeparator(0);

        if (Node?.Parent is { } owner)
        {
            for (var slot = 0; slot < owner.Green.SlotCount; slot++)
            {
                if (ReferenceEquals(owner.Green.GetSlot(slot), Green) && owner.Green.CreateSeparator(slot) is { } fromSlot)
                    return new SyntaxToken(parent: null, fromSlot, position: 0, index: 0);
            }
        }

        if (node.Green.CreateSeparator() is not { } separator)
            throw new InvalidOperationException($"The language of '{node.GetType().Name}' does not define a separator for its lists; build the list with its separators instead.");

        return new SyntaxToken(parent: null, separator, position: 0, index: 0);
    }

    /// <summary>Returns a detached list without the node at <paramref name="index"/> and its separator.</summary>
    public SeparatedSyntaxList<TNode> RemoveAt(int index)
    {
        if ((uint)index >= (uint)Count)
            throw new ArgumentOutOfRangeException(nameof(index));

        var items = new List<SyntaxNodeOrToken>(_list);
        var slot = index << 1;

        // Take the separator that goes with the node: the one after it, or the one before it when it is the last.
        if (slot + 1 < items.Count)
        {
            items.RemoveAt(slot + 1);
        }
        else if (slot > 0)
        {
            items.RemoveAt(slot - 1);
            slot--;
        }

        items.RemoveAt(slot);

        return new SeparatedSyntaxList<TNode>(new SyntaxNodeOrTokenList(items));
    }

    /// <summary>Returns a detached list without <paramref name="node"/>, or this list when it does not contain it.</summary>
    public SeparatedSyntaxList<TNode> Remove(TNode node)
    {
        var index = IndexOf(node);

        return index < 0 ? this : RemoveAt(index);
    }

    /// <summary>Returns a detached list with <paramref name="newNode"/> in place of <paramref name="nodeInList"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="newNode"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="nodeInList"/> is not in this list.</exception>
    public SeparatedSyntaxList<TNode> Replace(TNode nodeInList, TNode newNode)
    {
        ArgumentNullException.ThrowIfNull(newNode);

        var index = IndexOf(nodeInList);
        if (index < 0)
            throw new ArgumentException("The node is not part of this list.", nameof(nodeInList));

        return new SeparatedSyntaxList<TNode>(_list.Replace(_list[index << 1], newNode));
    }

    /// <summary>Returns a detached list with <paramref name="newSeparator"/> in place of <paramref name="separatorToken"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="separatorToken"/> is not a separator of this list.</exception>
    public SeparatedSyntaxList<TNode> ReplaceSeparator(SyntaxToken separatorToken, SyntaxToken newSeparator)
    {
        for (var i = 0; i < SeparatorCount; i++)
        {
            if (GetSeparator(i) == separatorToken)
                return new SeparatedSyntaxList<TNode>(_list.Replace(_list[(i << 1) + 1], newSeparator));
        }

        throw new ArgumentException("The token is not a separator of this list.", nameof(separatorToken));
    }

    public Enumerator GetEnumerator() => new(this);
    IEnumerator<TNode> IEnumerable<TNode>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => _list.ToString();
    public string ToFullString() => _list.ToFullString();

    public bool Equals(SeparatedSyntaxList<TNode> other) => _list == other._list;
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is SeparatedSyntaxList<TNode> other && Equals(other);
    public override int GetHashCode() => _list.GetHashCode();
    public static bool operator ==(SeparatedSyntaxList<TNode> left, SeparatedSyntaxList<TNode> right) => left.Equals(right);
    public static bool operator !=(SeparatedSyntaxList<TNode> left, SeparatedSyntaxList<TNode> right) => !left.Equals(right);

    public static implicit operator SeparatedSyntaxList<SyntaxNode>(SeparatedSyntaxList<TNode> nodes) => new(nodes._list);

    [Conditional("DEBUG")]
    private static void ValidateAlternation(SyntaxNodeOrTokenList list)
    {
        for (var i = 0; i < list.Count; i++)
        {
            Debug.Assert(list[i].IsNode == (i % 2 == 0), "A separated list must alternate nodes and separators, starting with a node.");
        }
    }

    /// <summary>Walks a <see cref="SeparatedSyntaxList{TNode}"/> without allocating.</summary>
    public struct Enumerator : IEnumerator<TNode>
    {
        private readonly SeparatedSyntaxList<TNode> _list;
        private int _index;

        internal Enumerator(SeparatedSyntaxList<TNode> list)
        {
            _list = list;
            _index = -1;
        }

        public readonly TNode Current => _list[_index];
        readonly object IEnumerator.Current => Current;

        public bool MoveNext() => ++_index < _list.Count;
        public void Reset() => _index = -1;
        public readonly void Dispose() { }
    }
}
