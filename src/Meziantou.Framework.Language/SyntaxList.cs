using System.Collections;
using System.Runtime.InteropServices;
using Meziantou.Framework.Language.InternalSyntax;
using GreenList = Meziantou.Framework.Language.InternalSyntax.SyntaxList;

namespace Meziantou.Framework.Language;

/// <summary>A sequence of nodes of the same kind, held in one slot of their parent.</summary>
/// <typeparam name="TNode">The type of the nodes in the list.</typeparam>
/// <remarks><see langword="default"/> is the empty list.</remarks>
[StructLayout(LayoutKind.Auto)]
public readonly struct SyntaxList<TNode> : IReadOnlyList<TNode>, IEquatable<SyntaxList<TNode>>
    where TNode : SyntaxNode
{
    private readonly SyntaxNode? _node;

    internal SyntaxList(SyntaxNode? node) => _node = node;

    /// <summary>Creates a detached list holding a single node.</summary>
    public SyntaxList(TNode? node)
        : this((SyntaxNode?)node)
    {
    }

    /// <summary>Creates a detached list holding <paramref name="nodes"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="nodes"/> is <see langword="null"/>.</exception>
    public SyntaxList(IEnumerable<TNode> nodes)
        : this(SyntaxListBuilder.CreateNode(nodes))
    {
    }

    internal SyntaxNode? Node => _node;
    internal GreenNode? Green => _node?.Green;

    public int Count => _node is null ? 0 : _node.Green.IsList ? _node.Green.SlotCount : 1;

    public TNode this[int index]
    {
        get
        {
            if (_node is null || (uint)index >= (uint)Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            return (TNode)(_node.Green.IsList ? _node.GetNodeSlot(index)! : _node);
        }
    }

    public TextSpan FullSpan => _node?.FullSpan ?? default;
    public TextSpan Span => Count == 0 ? default : TextSpan.FromBounds(this[0].SpanStart, this[Count - 1].Span.End);

    public bool Any() => _node is not null;
    public TNode? First() => Count > 0 ? this[0] : throw new InvalidOperationException("The list is empty.");
    public TNode? Last() => Count > 0 ? this[Count - 1] : throw new InvalidOperationException("The list is empty.");
    public TNode? FirstOrDefault() => Count > 0 ? this[0] : null;
    public TNode? LastOrDefault() => Count > 0 ? this[Count - 1] : null;

    /// <summary>Returns the position of <paramref name="node"/> in this list, or -1.</summary>
    /// <remarks>
    /// Elements are matched by which one they are, not by the immutable node behind them: that node is shared, so
    /// two elements holding the same text are usually the same node and would both answer with the first position.
    /// </remarks>
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

    /// <summary>Returns a detached list with <paramref name="node"/> appended.</summary>
    public SyntaxList<TNode> Add(TNode node) => Insert(Count, node);

    /// <summary>Returns a detached list with <paramref name="nodes"/> appended.</summary>
    public SyntaxList<TNode> AddRange(IEnumerable<TNode> nodes) => InsertRange(Count, nodes);

    /// <summary>Returns a detached list with <paramref name="node"/> inserted at <paramref name="index"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="node"/> is <see langword="null"/>.</exception>
    public SyntaxList<TNode> Insert(int index, TNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return Detached(GreenNodeList.Insert(_node?.Green, index, [node.Green]));
    }

    /// <summary>Returns a detached list with <paramref name="nodes"/> inserted at <paramref name="index"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="nodes"/> is <see langword="null"/>.</exception>
    public SyntaxList<TNode> InsertRange(int index, IEnumerable<TNode> nodes)
    {
        ArgumentNullException.ThrowIfNull(nodes);

        return Detached(GreenNodeList.Insert(_node?.Green, index, SyntaxListBuilder.ToGreenArray(nodes)));
    }

    /// <summary>Returns a detached list without the node at <paramref name="index"/>.</summary>
    public SyntaxList<TNode> RemoveAt(int index) => Detached(GreenNodeList.RemoveAt(_node?.Green, index));

    /// <summary>Returns a detached list without <paramref name="node"/>, or this list when it does not contain it.</summary>
    public SyntaxList<TNode> Remove(TNode node)
    {
        var index = IndexOf(node);

        return index < 0 ? this : RemoveAt(index);
    }

    /// <summary>Returns a detached list with <paramref name="newNode"/> in place of <paramref name="nodeInList"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="newNode"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="nodeInList"/> is not in this list.</exception>
    public SyntaxList<TNode> Replace(TNode nodeInList, TNode newNode)
    {
        ArgumentNullException.ThrowIfNull(newNode);

        return ReplaceRange(nodeInList, [newNode]);
    }

    /// <summary>Returns a detached list with <paramref name="newNodes"/> in place of <paramref name="nodeInList"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="newNodes"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException"><paramref name="nodeInList"/> is not in this list.</exception>
    public SyntaxList<TNode> ReplaceRange(TNode nodeInList, IEnumerable<TNode> newNodes)
    {
        ArgumentNullException.ThrowIfNull(newNodes);

        var index = IndexOf(nodeInList);
        if (index < 0)
            throw new ArgumentException("The node is not part of this list.", nameof(nodeInList));

        return Detached(GreenNodeList.ReplaceRange(_node?.Green, index, count: 1, SyntaxListBuilder.ToGreenArray(newNodes)));
    }

    public Enumerator GetEnumerator() => new(this);
    IEnumerator<TNode> IEnumerable<TNode>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => _node?.ToString() ?? string.Empty;
    public string ToFullString() => _node?.ToFullString() ?? string.Empty;

    public bool Equals(SyntaxList<TNode> other) => ReferenceEquals(_node, other._node);
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is SyntaxList<TNode> other && Equals(other);
    public override int GetHashCode() => _node?.GetHashCode() ?? 0;
    public static bool operator ==(SyntaxList<TNode> left, SyntaxList<TNode> right) => left.Equals(right);
    public static bool operator !=(SyntaxList<TNode> left, SyntaxList<TNode> right) => !left.Equals(right);

    public static implicit operator SyntaxList<SyntaxNode>(SyntaxList<TNode> nodes) => new(nodes._node);

    private static SyntaxList<TNode> Detached(GreenNode? green) => new(green?.CreateRed());

    /// <summary>Walks a <see cref="SyntaxList{TNode}"/> without allocating.</summary>
    public struct Enumerator : IEnumerator<TNode>
    {
        private readonly SyntaxList<TNode> _list;
        private int _index;

        internal Enumerator(SyntaxList<TNode> list)
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
