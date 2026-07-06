namespace Meziantou.Framework.Yaml;

/// <summary>
/// Implements an indexer through an IEnumerator&lt;T&gt;.
/// </summary>
public class FakeList<T>
{
    private readonly IEnumerator<T> _collection;
    private int _currentIndex = -1;

    /// <summary>
    /// Initializes a new instance of FakeList&lt;T&gt;.
    /// </summary>
    /// <param name="collection">The enumerator to use to implement the indexer.</param>
    public FakeList(IEnumerator<T> collection)
    {
        this._collection = collection;
    }

    /// <summary>
    /// Initializes a new instance of FakeList&lt;T&gt;.
    /// </summary>
    /// <param name="collection">The collection to use to implement the indexer.</param>
    public FakeList(IEnumerable<T> collection)
        : this(collection.GetEnumerator())
    {
    }

    /// <summary>Gets the element at the specified index.</summary>
    /// <remarks>
    /// If index is greater or equal than the last used index, this operation is O(index - lastIndex),
    /// else this operation is O(index).
    /// </remarks>
    public T this[int index]
    {
        get
        {
            if (index < _currentIndex)
            {
                _collection.Reset();
                _currentIndex = -1;
            }

            while (_currentIndex < index)
            {
                if (!_collection.MoveNext())
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }
                ++_currentIndex;
            }

            return _collection.Current;
        }
    }
}