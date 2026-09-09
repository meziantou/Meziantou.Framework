using System.Collections;
using System.Runtime.InteropServices;
using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language;

/// <summary>The trivia on one side of a token.</summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct SyntaxTriviaList : IReadOnlyList<SyntaxTrivia>, IEquatable<SyntaxTriviaList>
{
    private readonly SyntaxToken _token;
    private readonly GreenNode? _node;
    private readonly int _position;
    private readonly int _index;

    internal SyntaxTriviaList(SyntaxToken token, GreenNode? node, int position, int index)
    {
        _token = token;
        _node = node;
        _position = position;
        _index = index;
    }

    internal GreenNode? Node => _node;

    public int Count => GreenNodeList.Count(_node);

    public SyntaxTrivia this[int index]
    {
        get
        {
            if (GreenNodeList.ElementAt(_node, index) is not { } green)
                throw new ArgumentOutOfRangeException(nameof(index));

            return new SyntaxTrivia(_token, green, _position + GreenNodeList.OffsetAt(_node, index), _index + index);
        }
    }

    public TextSpan FullSpan => new(_position, _node?.FullWidth ?? 0);
    public TextSpan Span => FullSpan;

    public bool Any() => _node is not null;
    public SyntaxTrivia First() => Count > 0 ? this[0] : throw new InvalidOperationException("The list is empty.");
    public SyntaxTrivia Last() => Count > 0 ? this[Count - 1] : throw new InvalidOperationException("The list is empty.");
    public SyntaxTrivia FirstOrDefault() => Count > 0 ? this[0] : default;
    public SyntaxTrivia LastOrDefault() => Count > 0 ? this[Count - 1] : default;

    /// <summary>Returns the position of <paramref name="trivia"/> in this list, or -1.</summary>
    /// <remarks>
    /// Trivia is matched by which entry it is, not by the immutable trivium behind it: a run of spaces is interned,
    /// so two entries holding the same text are usually the same trivium and would both answer with the first.
    /// </remarks>
    public int IndexOf(SyntaxTrivia trivia)
    {
        for (var i = 0; i < Count; i++)
        {
            if (this[i] == trivia)
                return i;
        }

        return -1;
    }

    /// <summary>Returns a detached list with <paramref name="trivia"/> appended.</summary>
    public SyntaxTriviaList Add(SyntaxTrivia trivia) => Insert(Count, trivia);

    /// <summary>Returns a detached list with <paramref name="trivia"/> appended.</summary>
    public SyntaxTriviaList AddRange(IEnumerable<SyntaxTrivia> trivia) => InsertRange(Count, trivia);

    /// <summary>Returns a detached list with <paramref name="trivia"/> inserted at <paramref name="index"/>.</summary>
    public SyntaxTriviaList Insert(int index, SyntaxTrivia trivia) => Detached(GreenNodeList.Insert(_node, index, [trivia.UnderlyingNode]));

    /// <summary>Returns a detached list with <paramref name="trivia"/> inserted at <paramref name="index"/>.</summary>
    public SyntaxTriviaList InsertRange(int index, IEnumerable<SyntaxTrivia> trivia)
    {
        ArgumentNullException.ThrowIfNull(trivia);

        return Detached(GreenNodeList.Insert(_node, index, ToGreenArray(trivia)));
    }

    /// <summary>Returns a detached list without the trivia at <paramref name="index"/>.</summary>
    public SyntaxTriviaList RemoveAt(int index) => Detached(GreenNodeList.RemoveAt(_node, index));

    /// <summary>Returns a detached list without <paramref name="trivia"/>, or this list when it does not contain it.</summary>
    public SyntaxTriviaList Remove(SyntaxTrivia trivia)
    {
        var index = IndexOf(trivia);

        return index < 0 ? this : RemoveAt(index);
    }

    /// <summary>Returns a detached list with <paramref name="newTrivia"/> in place of <paramref name="triviaInList"/>.</summary>
    /// <exception cref="ArgumentException"><paramref name="triviaInList"/> is not in this list.</exception>
    public SyntaxTriviaList Replace(SyntaxTrivia triviaInList, SyntaxTrivia newTrivia)
    {
        var index = IndexOf(triviaInList);
        if (index < 0)
            throw new ArgumentException("The trivia is not part of this list.", nameof(triviaInList));

        return Detached(GreenNodeList.ReplaceRange(_node, index, count: 1, [newTrivia.UnderlyingNode]));
    }

    public Enumerator GetEnumerator() => new(this);
    IEnumerator<SyntaxTrivia> IEnumerable<SyntaxTrivia>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public override string ToString() => _node?.ToString() ?? string.Empty;
    public string ToFullString() => _node?.ToFullString() ?? string.Empty;

    public bool Equals(SyntaxTriviaList other) => _token == other._token && ReferenceEquals(_node, other._node) && _position == other._position && _index == other._index;
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is SyntaxTriviaList other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(_token, _node, _position, _index);
    public static bool operator ==(SyntaxTriviaList left, SyntaxTriviaList right) => left.Equals(right);
    public static bool operator !=(SyntaxTriviaList left, SyntaxTriviaList right) => !left.Equals(right);

    internal static GreenNode? ToGreen(IEnumerable<SyntaxTrivia>? trivia) => trivia is null ? null : SyntaxList.List(ToGreenArray(trivia));

    private static GreenNode?[] ToGreenArray(IEnumerable<SyntaxTrivia> trivia)
    {
        if (trivia is SyntaxTriviaList list)
            return GreenNodeList.ToArray(list._node);

        return [.. trivia.Select(item => item.UnderlyingNode)];
    }

    private static SyntaxTriviaList Detached(GreenNode? node) => new(default, node, position: 0, index: 0);

    /// <summary>Walks a <see cref="SyntaxTriviaList"/> without allocating.</summary>
    public struct Enumerator : IEnumerator<SyntaxTrivia>
    {
        private readonly SyntaxTriviaList _list;
        private int _index;

        internal Enumerator(SyntaxTriviaList list)
        {
            _list = list;
            _index = -1;
        }

        public readonly SyntaxTrivia Current => _list[_index];
        readonly object IEnumerator.Current => Current;

        public bool MoveNext() => ++_index < _list.Count;
        public void Reset() => _index = -1;
        public readonly void Dispose() { }
    }
}
