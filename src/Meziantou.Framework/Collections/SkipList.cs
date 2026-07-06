using System.Collections;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Collections;

/// <summary>
/// Represents a sorted skip list that stores unique values.
/// </summary>
/// <typeparam name="T">The type of elements in the skip list.</typeparam>
public sealed class SkipList<T> : ICollection<T>, ICollection, IReadOnlyCollection<T>
{
    private const int MaxLevel = 32;

    private readonly Node _head;
    private readonly IComparer<T> _comparer;
    private int _level = 1;
    private int _version;

    /// <summary>
    /// Initializes a new instance of the <see cref="SkipList{T}"/> class using <see cref="Comparer{T}.Default"/>.
    /// </summary>
    public SkipList()
        : this(comparer: null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SkipList{T}"/> class with a custom comparer.
    /// </summary>
    /// <param name="comparer">The comparer used to order elements.</param>
    public SkipList(IComparer<T>? comparer)
    {
        _head = new Node(default!, MaxLevel);
        _comparer = comparer ?? Comparer<T>.Default;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SkipList{T}"/> class that contains elements copied from the specified collection.
    /// </summary>
    /// <param name="collection">The collection whose elements are copied to the new skip list.</param>
    public SkipList(IEnumerable<T> collection)
        : this(collection, comparer: null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="SkipList{T}"/> class that contains elements copied from the specified collection and uses a custom comparer.
    /// </summary>
    /// <param name="collection">The collection whose elements are copied to the new skip list.</param>
    /// <param name="comparer">The comparer used to order elements.</param>
    public SkipList(IEnumerable<T> collection, IComparer<T>? comparer)
        : this(comparer)
    {
        ArgumentNullException.ThrowIfNull(collection);

        foreach (var item in collection)
        {
            Add(item);
        }
    }

    bool ICollection<T>.IsReadOnly => false;

    bool ICollection.IsSynchronized => false;

    object ICollection.SyncRoot => this;

    /// <summary>
    /// Gets the number of elements contained in the skip list.
    /// </summary>
    public int Count { get; private set; }

    /// <summary>
    /// Gets the comparer used to order elements.
    /// </summary>
    public IComparer<T> Comparer => _comparer;

    /// <summary>
    /// Adds an element to the skip list if it does not already exist.
    /// </summary>
    /// <param name="item">The element to add.</param>
    /// <returns><see langword="true"/> if the element was added; otherwise, <see langword="false"/>.</returns>
    public bool Add(T item)
    {
        var update = new Node[MaxLevel];
        var current = FindGreaterOrEqual(item, update);
        if (current is not null && _comparer.Compare(current.Value, item) is 0)
        {
            return false;
        }

        var newLevel = GetRandomLevel();
        if (newLevel > _level)
        {
            for (var i = _level; i < newLevel; i++)
            {
                update[i] = _head;
            }

            _level = newLevel;
        }

        var newNode = new Node(item, newLevel);
        for (var i = 0; i < newLevel; i++)
        {
            newNode.Next[i] = update[i].Next[i];
            update[i].Next[i] = newNode;
        }

        Count++;
        _version++;

        return true;
    }

    void ICollection<T>.Add(T item)
    {
        Add(item);
    }

    /// <summary>
    /// Removes the specified element from the skip list.
    /// </summary>
    /// <param name="item">The element to remove.</param>
    /// <returns><see langword="true"/> if the element was removed; otherwise, <see langword="false"/>.</returns>
    public bool Remove(T item)
    {
        var update = new Node[MaxLevel];
        var current = FindGreaterOrEqual(item, update);
        if (current is null || _comparer.Compare(current.Value, item) is not 0)
        {
            return false;
        }

        for (var i = 0; i < _level; i++)
        {
            if (ReferenceEquals(update[i].Next[i], current))
            {
                update[i].Next[i] = current.Next[i];
            }
        }

        if (RuntimeHelpers.IsReferenceOrContainsReferences<T>())
        {
            Array.Clear(current.Next, 0, current.Next.Length);
        }

        while (_level > 1 && _head.Next[_level - 1] is null)
        {
            _level--;
        }

        Count--;
        _version++;

        return true;
    }

    /// <summary>
    /// Determines whether the skip list contains a specific element.
    /// </summary>
    /// <param name="item">The element to locate.</param>
    /// <returns><see langword="true"/> if the element is found; otherwise, <see langword="false"/>.</returns>
    public bool Contains(T item)
    {
        var current = FindGreaterOrEqual(item, update: null);
        return current is not null && _comparer.Compare(current.Value, item) is 0;
    }

    /// <summary>
    /// Searches for an element equal to the specified value and returns the stored value when found.
    /// </summary>
    /// <param name="equalValue">The value to search for.</param>
    /// <param name="actualValue">When this method returns, contains the matching value from the skip list, if found.</param>
    /// <returns><see langword="true"/> if a matching element was found; otherwise, <see langword="false"/>.</returns>
    public bool TryGetValue(T equalValue, [MaybeNullWhen(false)] out T actualValue)
    {
        var current = FindGreaterOrEqual(equalValue, update: null);
        if (current is not null && _comparer.Compare(current.Value, equalValue) is 0)
        {
            actualValue = current.Value;
            return true;
        }

        actualValue = default;
        return false;
    }

    /// <summary>
    /// Removes all elements from the skip list.
    /// </summary>
    public void Clear()
    {
        if (Count > 0)
        {
            Array.Clear(_head.Next, 0, _head.Next.Length);
            Count = 0;
            _level = 1;
        }

        _version++;
    }

    /// <summary>
    /// Copies the elements of the skip list to an array, starting at the specified index.
    /// </summary>
    /// <param name="array">The destination array.</param>
    /// <param name="arrayIndex">The zero-based index in <paramref name="array"/> at which copying begins.</param>
    public void CopyTo(T[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);
        ArgumentOutOfRangeException.ThrowIfNegative(arrayIndex);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(arrayIndex, array.Length);
        if (array.Rank is not 1)
            throw new ArgumentException("Array must be single-dimensional", nameof(array));

        if (array.Length - arrayIndex < Count)
            throw new ArgumentException("Destination array is not long enough to copy all the items in the collection.", nameof(array));

        var node = _head.Next[0];
        while (node is not null)
        {
            array[arrayIndex++] = node.Value;
            node = node.Next[0];
        }
    }

    void ICollection.CopyTo(Array array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);
        ArgumentOutOfRangeException.ThrowIfNegative(arrayIndex);

        if (array.Rank != 1)
            throw new ArgumentException("Only single dimensional arrays are supported for the requested action.", nameof(array));

        if (array.GetLowerBound(0) != 0)
            throw new ArgumentException("The lower bound of target array must be zero.", nameof(array));

        ArgumentOutOfRangeException.ThrowIfGreaterThan(arrayIndex, array.Length);

        if (array.Length - arrayIndex < Count)
            throw new ArgumentException("Destination array is not long enough to copy all the items in the collection.", nameof(array));

        if (array is T[] typedArray)
        {
            CopyTo(typedArray, arrayIndex);
            return;
        }

        if (array is object?[] objects)
        {
            try
            {
                var node = _head.Next[0];
                while (node is not null)
                {
                    objects[arrayIndex++] = node.Value;
                    node = node.Next[0];
                }
            }
            catch (ArrayTypeMismatchException)
            {
                throw new ArgumentException("Target array type is not compatible with the type of items in the collection.", nameof(array));
            }

            return;
        }

        throw new ArgumentException("Target array type is not compatible with the type of items in the collection.", nameof(array));
    }

    /// <summary>
    /// Returns an enumerator that iterates through the skip list.
    /// </summary>
    /// <returns>An enumerator for the skip list.</returns>
    public Enumerator GetEnumerator() => new(this);

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => new Enumerator(this);

    IEnumerator IEnumerable.GetEnumerator() => new Enumerator(this);

    private Node? FindGreaterOrEqual(T item, Node[]? update)
    {
        var current = _head;
        for (var level = _level - 1; level >= 0; level--)
        {
            while (current.Next[level] is { } next && _comparer.Compare(next.Value, item) < 0)
            {
                current = next;
            }

            if (update is not null)
            {
                update[level] = current;
            }
        }

        return current.Next[0];
    }

    private static int GetRandomLevel()
    {
        var level = 1;
#pragma warning disable CA5394 // Do not use insecure randomness
        while (level < MaxLevel && Random.Shared.Next(2) is 0)
#pragma warning restore CA5394
        {
            level++;
        }

        return level;
    }

    private sealed class Node(T value, int level)
    {
        public T Value { get; } = value;

        public Node?[] Next { get; } = new Node?[level];
    }

    /// <summary>
    /// Enumerates the elements of a <see cref="SkipList{T}"/>.
    /// </summary>
    public struct Enumerator : IEnumerator<T>, IEnumerator
    {
        private readonly SkipList<T> _list;
        private Node? _currentNode;
        private int _index;
        private readonly int _version;

        internal Enumerator(SkipList<T> list)
        {
            _list = list;
            _currentNode = null;
            _index = 0;
            _version = list._version;
            Current = default!;
        }

        /// <summary>
        /// Releases all resources used by the enumerator.
        /// </summary>
        public readonly void Dispose()
        {
        }

        /// <summary>
        /// Advances the enumerator to the next element of the skip list.
        /// </summary>
        /// <returns><see langword="true"/> if the enumerator was successfully advanced; otherwise, <see langword="false"/>.</returns>
        public bool MoveNext()
        {
            var localList = _list;
            if (_version == localList._version && ((uint)_index < (uint)localList.Count))
            {
                _currentNode = _index is 0 ? localList._head.Next[0] : _currentNode!.Next[0];
                Current = _currentNode!.Value;
                _index++;
                return true;
            }

            return MoveNextRare();
        }

        private bool MoveNextRare()
        {
            if (_version != _list._version)
                ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumFailedVersion();

            _index = _list.Count + 1;
            Current = default!;
            return false;
        }

        /// <summary>
        /// Gets the element in the skip list at the current position of the enumerator.
        /// </summary>
        public T Current { get; private set; }

        readonly object? IEnumerator.Current
        {
            get
            {
                if (_index is 0 || _index == _list.Count + 1)
                    ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumOpCantHappen();

                return Current;
            }
        }

        void IEnumerator.Reset()
        {
            if (_version != _list._version)
            {
                ThrowHelper.ThrowInvalidOperationException_InvalidOperation_EnumFailedVersion();
            }

            _currentNode = null;
            _index = 0;
            Current = default!;
        }
    }
}
