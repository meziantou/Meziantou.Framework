using System.Collections;

namespace Meziantou.AspNetCore.Tests;

/// <summary>A collection that throws when read, held by a middleware registration in an inspected namespace.</summary>
internal sealed class ThrowingCollection : IList
{
    public object? this[int index] { get => throw new InvalidOperationException("must not be read"); set => throw new InvalidOperationException(); }

    public int Count => 1;

    public bool IsFixedSize => true;

    public bool IsReadOnly => true;

    public bool IsSynchronized => false;

    public object SyncRoot => this;

    public int Add(object? value) => throw new NotSupportedException();

    public void Clear() => throw new NotSupportedException();

    public bool Contains(object? value) => false;

    public void CopyTo(Array array, int index) => throw new NotSupportedException();

    public IEnumerator GetEnumerator() => throw new InvalidOperationException("must not be enumerated");

    public int IndexOf(object? value) => -1;

    public void Insert(int index, object? value) => throw new NotSupportedException();

    public void Remove(object? value) => throw new NotSupportedException();

    public void RemoveAt(int index) => throw new NotSupportedException();
}
