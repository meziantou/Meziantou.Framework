using System.Collections;
using System.Runtime.InteropServices;
using Meziantou.Framework.Language.InternalSyntax;
using GreenList = Meziantou.Framework.Language.InternalSyntax.SyntaxList;

namespace Meziantou.Framework.Language;

/// <summary>A sequence that mixes nodes and tokens, as a separated list does.</summary>
/// <remarks><see langword="default"/> is the empty list.</remarks>
[StructLayout(LayoutKind.Auto)]
public readonly struct SyntaxNodeOrTokenList : IReadOnlyList<SyntaxNodeOrToken>, IEquatable<SyntaxNodeOrTokenList>
{
    private readonly SyntaxNode? _node;
    private readonly int _index;

    internal SyntaxNodeOrTokenList(SyntaxNode? node, int index)
    {
        _node = node;
        _index = index;
    }

    /// <summary>Creates a detached list holding <paramref name="nodesAndTokens"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="nodesAndTokens"/> is <see langword="null"/>.</exception>
    public SyntaxNodeOrTokenList(IEnumerable<SyntaxNodeOrToken> nodesAndTokens)
        : this(CreateNode(nodesAndTokens), index: 0)
    {
    }

    internal SyntaxNode? Node => _node;
    internal GreenNode? Green => _node?.Green;

    public int Count => _node is null ? 0 : _node.Green.IsList ? _node.Green.SlotCount : 1;

    public SyntaxNodeOrToken this[int index]
    {
        get
        {
            if (_node is null || (uint)index >= (uint)Count)
                throw new ArgumentOutOfRangeException(nameof(index));

            if (!_node.Green.IsList)
                return _node;

            var green = _node.Green.GetRequiredSlot(index);
            if (green.IsToken)
                return new SyntaxNodeOrToken(_node.Parent, green, _node.GetChildPosition(index), _index + index);

            return _node.GetNodeSlot(index)!;
        }
    }

    public TextSpan FullSpan => _node?.FullSpan ?? default;
    public TextSpan Span => Count == 0 ? default : TextSpan.FromBounds(this[0].SpanStart, this[Count - 1].Span.End);

    public bool Any() => _node is not null;
    public SyntaxNodeOrToken First() => Count > 0 ? this[0] : throw new InvalidOperationException("The list is empty.");
    public SyntaxNodeOrToken Last() => Count > 0 ? this[Count - 1] : throw new InvalidOperationException("The list is empty.");
    public SyntaxNodeOrToken FirstOrDefault() => Count > 0 ? this[0] : default;
    public SyntaxNodeOrToken LastOrDefault() => Count > 0 ? this[Count - 1] : default;

    /// <summary>Returns the position of <paramref name="nodeOrToken"/> in this list, or -1.</summary>
    /// <remarks>
    /// Items are matched by which one they are, not by the immutable node behind them: that node is shared, so two
    /// items holding the same text are usually the same node and would both answer with the first position.
    /// </remarks>
    public int IndexOf(SyntaxNodeOrToken nodeOrToken)
    {
        for (var i = 0; i < Count; i++)
        {
            if (this[i] == nodeOrToken)
                return i;
        }

        return -1;
    }

    /// <summary>Returns a detached list with <paramref name="nodeOrToken"/> appended.</summary>
    public SyntaxNodeOrTokenList Add(SyntaxNodeOrToken nodeOrToken) => Insert(Count, nodeOrToken);

    /// <summary>Returns a detached list with <paramref name="nodesAndTokens"/> appended.</summary>
    public SyntaxNodeOrTokenList AddRange(IEnumerable<SyntaxNodeOrToken> nodesAndTokens) => InsertRange(Count, nodesAndTokens);

    /// <summary>Returns a detached list with <paramref name="nodeOrToken"/> inserted at <paramref name="index"/>.</summary>
    public SyntaxNodeOrTokenList Insert(int index, SyntaxNodeOrToken nodeOrToken) => Detached(GreenNodeList.Insert(_node?.Green, index, [nodeOrToken.UnderlyingNode]));

    /// <summary>Returns a detached list with <paramref name="nodesAndTokens"/> inserted at <paramref name="index"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="nodesAndTokens"/> is <see langword="null"/>.</exception>
    public SyntaxNodeOrTokenList InsertRange(int index, IEnumerable<SyntaxNodeOrToken> nodesAndTokens)
    {
        ArgumentNullException.ThrowIfNull(nodesAndTokens);

        return Detached(GreenNodeList.Insert(_node?.Green, index, ToGreenArray(nodesAndTokens)));
    }

    /// <summary>Returns a detached list without the item at <paramref name="index"/>.</summary>
    public SyntaxNodeOrTokenList RemoveAt(int index) => Detached(GreenNodeList.RemoveAt(_node?.Green, index));

    /// <summary>Returns a detached list without <paramref name="nodeOrToken"/>, or this list when it does not contain it.</summary>
    public SyntaxNodeOrTokenList Remove(SyntaxNodeOrToken nodeOrToken)
    {
        var index = IndexOf(nodeOrToken);

        return index < 0 ? this : RemoveAt(index);
    }

    /// <summary>Returns a detached list with <paramref name="newNodeOrToken"/> in place of <paramref name="nodeOrTokenInList"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="nodeOrTokenInList"/> is not in this list.</exception>
    public SyntaxNodeOrTokenList Replace(SyntaxNodeOrToken nodeOrTokenInList, SyntaxNodeOrToken newNodeOrToken)
    {
        var index = IndexOf(nodeOrTokenInList);
        if (index < 0)
            throw new ArgumentException("The node or token is not part of this list.", nameof(nodeOrTokenInList));

        return Detached(GreenNodeList.ReplaceRange(_node?.Green, index, count: 1, [newNodeOrToken.UnderlyingNode]));
    }

    public Enumerator GetEnumerator() => new(this);
    IEnumerator<SyntaxNodeOrToken> IEnumerable<SyntaxNodeOrToken>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => _node?.ToString() ?? string.Empty;
    public string ToFullString() => _node?.ToFullString() ?? string.Empty;

    public bool Equals(SyntaxNodeOrTokenList other) => ReferenceEquals(_node, other._node) && _index == other._index;
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is SyntaxNodeOrTokenList other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(_node, _index);
    public static bool operator ==(SyntaxNodeOrTokenList left, SyntaxNodeOrTokenList right) => left.Equals(right);
    public static bool operator !=(SyntaxNodeOrTokenList left, SyntaxNodeOrTokenList right) => !left.Equals(right);

    internal static GreenNode?[] ToGreenArray(IEnumerable<SyntaxNodeOrToken> nodesAndTokens)
    {
        if (nodesAndTokens is SyntaxNodeOrTokenList list)
            return GreenNodeList.ToArray(list._node?.Green);

        return [.. nodesAndTokens.Select(item => item.UnderlyingNode)];
    }

    private static SyntaxNode? CreateNode(IEnumerable<SyntaxNodeOrToken> nodesAndTokens)
    {
        ArgumentNullException.ThrowIfNull(nodesAndTokens);

        return GreenList.ListNode(ToGreenArray(nodesAndTokens))?.CreateRed();
    }

    private static SyntaxNodeOrTokenList Detached(GreenNode? green)
    {
        if (green is null)
            return default;

        // A single token cannot be projected on its own, so a list of one keeps its list node.
        var node = green.IsToken ? GreenList.ListNode([green])! : green;

        return new SyntaxNodeOrTokenList(node.CreateRed(), index: 0);
    }

    /// <summary>Walks a <see cref="SyntaxNodeOrTokenList"/> without allocating.</summary>
    public struct Enumerator : IEnumerator<SyntaxNodeOrToken>
    {
        private readonly SyntaxNodeOrTokenList _list;
        private int _index;

        internal Enumerator(SyntaxNodeOrTokenList list)
        {
            _list = list;
            _index = -1;
        }

        public readonly SyntaxNodeOrToken Current => _list[_index];
        readonly object IEnumerator.Current => Current;

        public bool MoveNext() => ++_index < _list.Count;
        public void Reset() => _index = -1;
        public readonly void Dispose() { }
    }
}
