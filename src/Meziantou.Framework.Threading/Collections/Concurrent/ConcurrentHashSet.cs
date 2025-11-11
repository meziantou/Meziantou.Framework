// Copied from https://github.com/dotnet/roslyn/blob/246b8274deeb44885337bb0543357fdc0c165203/src/Compilers/Core/Portable/InternalUtilities/ConcurrentSet.cs
using System.Collections;
using System.Collections.Concurrent;

namespace Meziantou.Framework.Collections.Concurrent;

/// <summary>Represents a thread-safe set of unique values.</summary>
/// <typeparam name="T">The type of elements in the set.</typeparam>
/// <example>
/// <code><![CDATA[
/// var set = new ConcurrentHashSet<int>();
/// set.Add(1);
/// set.Add(2);
/// if (set.Contains(1))
/// {
///     Console.WriteLine("Set contains 1");
/// }
/// ]]></code>
/// </example>
public sealed class ConcurrentHashSet<T> : ICollection<T>, IReadOnlyCollection<T>
    where T : notnull
{
    private readonly ConcurrentDictionary<T, byte> _dictionary;

    /// <summary>Initializes a new instance of the <see cref="ConcurrentHashSet{T}"/> class.</summary>
    public ConcurrentHashSet()
    {
        _dictionary = new ConcurrentDictionary<T, byte>();
    }

    /// <summary>Initializes a new instance of the <see cref="ConcurrentHashSet{T}"/> class with the specified equality comparer.</summary>
    /// <param name="equalityComparer">The equality comparer to use when comparing values in the set.</param>
    public ConcurrentHashSet(IEqualityComparer<T> equalityComparer)
    {
        _dictionary = new ConcurrentDictionary<T, byte>(equalityComparer);
    }

    /// <summary>Gets the number of elements contained in the <see cref="ConcurrentHashSet{T}"/>.</summary>
    public int Count => _dictionary.Count;

    /// <summary>Gets a value that indicates whether the <see cref="ConcurrentHashSet{T}"/> is empty.</summary>
    public bool IsEmpty => _dictionary.IsEmpty;

    /// <summary>Gets a value indicating whether the <see cref="ConcurrentHashSet{T}"/> is read-only.</summary>
    public bool IsReadOnly => false;

    /// <summary>Determines whether the <see cref="ConcurrentHashSet{T}"/> contains the specified element.</summary>
    /// <param name="item">The element to locate in the <see cref="ConcurrentHashSet{T}"/>.</param>
    /// <returns><see langword="true"/> if the <see cref="ConcurrentHashSet{T}"/> contains the element; otherwise, <see langword="false"/>.</returns>
    public bool Contains(T item) => _dictionary.ContainsKey(item);

    /// <summary>Attempts to add the specified element to the <see cref="ConcurrentHashSet{T}"/>.</summary>
    /// <param name="value">The element to add to the set.</param>
    /// <returns><see langword="true"/> if the element was added to the <see cref="ConcurrentHashSet{T}"/> successfully; <see langword="false"/> if the element already exists.</returns>
    public bool Add(T value) => _dictionary.TryAdd(value, 0);

    /// <summary>Adds the specified values to the <see cref="ConcurrentHashSet{T}"/>.</summary>
    /// <param name="values">The values to add.</param>
    public void AddRange(params ReadOnlySpan<T> values)
    {
        foreach (var v in values)
        {
            Add(v);
        }
    }

    /// <summary>Adds the specified values to the <see cref="ConcurrentHashSet{T}"/>.</summary>
    /// <param name="values">The values to add.</param>
    public void AddRange(IEnumerable<T>? values)
    {
        if (values is not null)
        {
            foreach (var v in values)
            {
                Add(v);
            }
        }
    }

    /// <summary>Attempts to remove the specified element from the <see cref="ConcurrentHashSet{T}"/>.</summary>
    /// <param name="item">The element to remove.</param>
    /// <returns><see langword="true"/> if the element was removed successfully; otherwise, <see langword="false"/>.</returns>
    public bool Remove(T item) => _dictionary.TryRemove(item, out _);

    /// <summary>Removes all elements from the <see cref="ConcurrentHashSet{T}"/>.</summary>
    public void Clear() => _dictionary.Clear();

    /// <summary>Enumerates the elements of a <see cref="ConcurrentHashSet{T}"/>.</summary>
    public readonly struct KeyEnumerator : IEnumerator<T>
    {
        private readonly IEnumerator<KeyValuePair<T, byte>> _kvpEnumerator;

        internal KeyEnumerator(IEnumerable<KeyValuePair<T, byte>> data)
        {
            _kvpEnumerator = data.GetEnumerator();
        }

        public T Current => _kvpEnumerator.Current.Key;

        object IEnumerator.Current => Current;

        public bool MoveNext()
        {
            return _kvpEnumerator.MoveNext();
        }

        public void Reset()
        {
            _kvpEnumerator.Reset();
        }

        public void Dispose()
        {
            _kvpEnumerator.Dispose();
        }
    }

    /// <summary>Returns an enumerator that iterates through the <see cref="ConcurrentHashSet{T}"/>.</summary>
    /// <returns>An enumerator for the <see cref="ConcurrentHashSet{T}"/>.</returns>
    public KeyEnumerator GetEnumerator()
    {
        // PERF: Do not use dictionary.Keys here because that creates a snapshot
        // of the collection resulting in a List<T> allocation. Instead, use the
        // KeyValuePair enumerator and pick off the Key part.
        return new KeyEnumerator(_dictionary);
    }

    private IEnumerator<T> GetEnumeratorImpl()
    {
        // PERF: Do not use dictionary.Keys here because that creates a snapshot
        // of the collection resulting in a List<T> allocation. Instead, use the
        // KeyValuePair enumerator and pick off the Key part.
        foreach (var kvp in _dictionary)
        {
            yield return kvp.Key;
        }
    }

    IEnumerator<T> IEnumerable<T>.GetEnumerator() => GetEnumeratorImpl();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumeratorImpl();

    void ICollection<T>.Add(T item) => Add(item);

    /// <summary>Copies the elements of the <see cref="ConcurrentHashSet{T}"/> to an array, starting at the specified array index.</summary>
    /// <param name="array">The destination array.</param>
    /// <param name="arrayIndex">The zero-based index in the array at which copying begins.</param>
    public void CopyTo(T[] array, int arrayIndex)
    {
        // PERF: Do not use dictionary.Keys here because that creates a snapshot
        // of the collection resulting in a List<T> allocation.
        // Instead, enumerate the set and copy over the elements.
        foreach (var element in this)
        {
            array[arrayIndex++] = element;
        }
    }
}
