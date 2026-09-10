using System.Collections;
using System.Runtime.InteropServices;
using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language;

/// <summary>A sequence of tokens held in one slot of their parent.</summary>
/// <remarks><see langword="default"/> is the empty list.</remarks>
[StructLayout(LayoutKind.Auto)]
public readonly struct SyntaxTokenList : IReadOnlyList<SyntaxToken>, IEquatable<SyntaxTokenList>
{
    private readonly SyntaxNode? _parent;
    private readonly GreenNode? _node;
    private readonly int _position;
    private readonly int _index;

    internal SyntaxTokenList(SyntaxNode? parent, GreenNode? node, int position, int index)
    {
        _parent = parent;
        _node = node;
        _position = position;
        _index = index;
    }

    /// <summary>Creates a detached list holding <paramref name="tokens"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="tokens"/> is <see langword="null"/>.</exception>
    public SyntaxTokenList(IEnumerable<SyntaxToken> tokens)
        : this(parent: null, ToGreen(tokens), position: 0, index: 0)
    {
    }

    internal GreenNode? Node => _node;

    public int Count => GreenNodeList.Count(_node);

    public SyntaxToken this[int index]
    {
        get
        {
            if (GreenNodeList.ElementAt(_node, index) is not { } green)
                throw new ArgumentOutOfRangeException(nameof(index));

            return new SyntaxToken(_parent, green, _position + GreenNodeList.OffsetAt(_node, index), _index + index);
        }
    }

    public TextSpan FullSpan => new(_position, _node?.FullWidth ?? 0);
    public TextSpan Span => Count == 0 ? default : TextSpan.FromBounds(this[0].SpanStart, this[Count - 1].Span.End);

    public bool Any() => _node is not null;
    public SyntaxToken First() => Count > 0 ? this[0] : throw new InvalidOperationException("The list is empty.");
    public SyntaxToken Last() => Count > 0 ? this[Count - 1] : throw new InvalidOperationException("The list is empty.");
    public SyntaxToken FirstOrDefault() => Count > 0 ? this[0] : default;
    public SyntaxToken LastOrDefault() => Count > 0 ? this[Count - 1] : default;

    /// <summary>Returns the position of <paramref name="token"/> in this list, or -1.</summary>
    /// <remarks>
    /// Tokens are matched by which one they are, not by the immutable token behind them: that token is shared, so
    /// two entries spelled the same are usually the same token and would both answer with the first position.
    /// </remarks>
    public int IndexOf(SyntaxToken token)
    {
        for (var i = 0; i < Count; i++)
        {
            if (this[i] == token)
                return i;
        }

        return -1;
    }

    /// <summary>Returns a detached list with <paramref name="token"/> appended.</summary>
    public SyntaxTokenList Add(SyntaxToken token) => Insert(Count, token);

    /// <summary>Returns a detached list with <paramref name="tokens"/> appended.</summary>
    public SyntaxTokenList AddRange(IEnumerable<SyntaxToken> tokens) => InsertRange(Count, tokens);

    /// <summary>Returns a detached list with <paramref name="token"/> inserted at <paramref name="index"/>.</summary>
    public SyntaxTokenList Insert(int index, SyntaxToken token) => Detached(GreenNodeList.Insert(_node, index, [token.Node]));

    /// <summary>Returns a detached list with <paramref name="tokens"/> inserted at <paramref name="index"/>.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="tokens"/> is <see langword="null"/>.</exception>
    public SyntaxTokenList InsertRange(int index, IEnumerable<SyntaxToken> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);

        return Detached(GreenNodeList.Insert(_node, index, ToGreenArray(tokens)));
    }

    /// <summary>Returns a detached list without the token at <paramref name="index"/>.</summary>
    public SyntaxTokenList RemoveAt(int index) => Detached(GreenNodeList.RemoveAt(_node, index));

    /// <summary>Returns a detached list without <paramref name="token"/>, or this list when it does not contain it.</summary>
    public SyntaxTokenList Remove(SyntaxToken token)
    {
        var index = IndexOf(token);

        return index < 0 ? this : RemoveAt(index);
    }

    /// <summary>Returns a detached list with <paramref name="newToken"/> in place of <paramref name="tokenInList"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="tokenInList"/> is not in this list.</exception>
    public SyntaxTokenList Replace(SyntaxToken tokenInList, SyntaxToken newToken)
    {
        var index = IndexOf(tokenInList);
        if (index < 0)
            throw new ArgumentException("The token is not part of this list.", nameof(tokenInList));

        return Detached(GreenNodeList.ReplaceRange(_node, index, count: 1, [newToken.Node]));
    }

    public Enumerator GetEnumerator() => new(this);
    IEnumerator<SyntaxToken> IEnumerable<SyntaxToken>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => _node?.ToString() ?? string.Empty;
    public string ToFullString() => _node?.ToFullString() ?? string.Empty;

    public bool Equals(SyntaxTokenList other)
        => ReferenceEquals(_parent, other._parent) && ReferenceEquals(_node, other._node) && _position == other._position && _index == other._index;

    public override bool Equals([NotNullWhen(true)] object? obj) => obj is SyntaxTokenList other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(_parent, _node, _position, _index);
    public static bool operator ==(SyntaxTokenList left, SyntaxTokenList right) => left.Equals(right);
    public static bool operator !=(SyntaxTokenList left, SyntaxTokenList right) => !left.Equals(right);

    internal static GreenNode? ToGreen(IEnumerable<SyntaxToken>? tokens) => tokens is null ? null : InternalSyntax.SyntaxList.List(ToGreenArray(tokens));

    private static GreenNode?[] ToGreenArray(IEnumerable<SyntaxToken> tokens)
    {
        if (tokens is SyntaxTokenList list)
            return GreenNodeList.ToArray(list._node);

        return [.. tokens.Select(token => token.Node)];
    }

    private static SyntaxTokenList Detached(GreenNode? node) => new(parent: null, node, position: 0, index: 0);

    /// <summary>Walks a <see cref="SyntaxTokenList"/> without allocating.</summary>
    public struct Enumerator : IEnumerator<SyntaxToken>
    {
        private readonly SyntaxTokenList _list;
        private int _index;

        internal Enumerator(SyntaxTokenList list)
        {
            _list = list;
            _index = -1;
        }

        public readonly SyntaxToken Current => _list[_index];
        readonly object IEnumerator.Current => Current;

        public bool MoveNext() => ++_index < _list.Count;
        public void Reset() => _index = -1;
        public readonly void Dispose() { }
    }
}
