using System.Buffers;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Markdown.Parsing;

/// <summary>The elements of a tree being built, in the order of the text: a node is followed by its descendants.</summary>
/// <remarks>The array is rented: <see cref="Dispose"/> returns it.</remarks>
internal sealed class TreeBuffer : IDisposable
{
    private BuildElement[] _items;

    public TreeBuffer(int capacity = 256)
    {
        _items = ArrayPool<BuildElement>.Shared.Rent(capacity);
    }

    public int Count { get; private set; }

    public BuildElement this[int index] => _items[index];

    /// <summary>Starts a node, whose descendants are the elements added until <see cref="EndNode"/>. Returns its index.</summary>
    public int StartNode(SyntaxKind kind)
    {
        Add(BuildElement.Node(kind, descendantCount: 0));
        return Count - 1;
    }

    public void EndNode(int index) => _items[index] = BuildElement.Node(_items[index].Kind, Count - index - 1);

    public void AddToken(SyntaxKind kind, int start, int end, int value = 0)
    {
        if (end > start)
        {
            Add(BuildElement.Token(kind, start, end, value));
        }
    }

    // A line of a code block is a token even when it is empty: it is still a line of the code
    public void AddLineToken(SyntaxKind kind, int start, int end, int value) => Add(BuildElement.Token(kind, start, end, value));

    /// <summary>Adds elements found before the tree is built, such as the tokens of a block, which can have nodes.</summary>
    public void AddRange(List<BuildElement>? elements)
    {
        if (elements is not null)
        {
            AddRange(CollectionsMarshal.AsSpan(elements));
        }
    }

    public void AddRange(ReadOnlySpan<BuildElement> elements)
    {
        EnsureCapacity(Count + elements.Length);
        elements.CopyTo(_items.AsSpan(Count));
        Count += elements.Length;
    }

    /// <summary>Removes the elements from <paramref name="count"/>.</summary>
    public void Truncate(int count) => Count = count;

    public void Dispose()
    {
        var items = _items;
        _items = [];
        Count = 0;
        ArrayPool<BuildElement>.Shared.Return(items);
    }

    private void Add(BuildElement element)
    {
        if (Count == _items.Length)
        {
            EnsureCapacity(Count + 1);
        }

        _items[Count++] = element;
    }

    private void EnsureCapacity(int capacity)
    {
        if (capacity <= _items.Length)
        {
            return;
        }

        var items = ArrayPool<BuildElement>.Shared.Rent(Math.Max(capacity, _items.Length * 2));
        _items.AsSpan(0, Count).CopyTo(items);
        ArrayPool<BuildElement>.Shared.Return(_items);
        _items = items;
    }
}
