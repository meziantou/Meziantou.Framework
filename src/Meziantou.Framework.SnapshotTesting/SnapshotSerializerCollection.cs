namespace Meziantou.Framework.SnapshotTesting;

public sealed class SnapshotSerializerCollection : IEnumerable<ISnapshotSerializer>
{
    private readonly Lock _lock = new();

    // Copy-on-write: readers take the current array without locking, writers publish a new one under the lock.
    // Serialization stays allocation-free while a registration on another thread cannot be observed halfway.
    private ISnapshotSerializer[] _serializers;

    public SnapshotSerializerCollection()
    {
        _serializers = [];
    }

    internal SnapshotSerializerCollection(SnapshotSerializerCollection source)
    {
        _serializers = source.Current;
    }

    private ISnapshotSerializer[] Current => Volatile.Read(ref _serializers);

    public int Count => Current.Length;

    /// <summary>Adds an untyped serializer. Untyped serializers are matched using <see cref="ISnapshotSerializer.TrySerialize"/>.</summary>
    public void Add(ISnapshotSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        lock (_lock)
        {
            Volatile.Write(ref _serializers, [.. _serializers, serializer]);
        }
    }

    public bool Remove(ISnapshotSerializer serializer)
    {
        lock (_lock)
        {
            var index = Array.IndexOf(_serializers, serializer);
            if (index < 0)
                return false;

            var updated = new ISnapshotSerializer[_serializers.Length - 1];
            _serializers.AsSpan(0, index).CopyTo(updated);
            _serializers.AsSpan(index + 1).CopyTo(updated.AsSpan(index));
            Volatile.Write(ref _serializers, updated);
            return true;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            Volatile.Write(ref _serializers, []);
        }
    }

    /// <summary>Replaces the first serializer of type <typeparamref name="T"/> with the one <paramref name="createReplacement"/> returns for it. Does nothing when no such serializer is registered.</summary>
    /// <remarks>
    /// The lookup and the replacement happen under the lock, so two concurrent updates both apply: each one starts from
    /// the result of the other instead of both replacing the same serializer, which would lose one of them. Replacing in
    /// place keeps the position of the serializer, so it cannot end up after, and shadow, the serializers registered
    /// after it.
    /// </remarks>
    internal void ReplaceFirst<T>(Func<T, ISnapshotSerializer> createReplacement)
        where T : class, ISnapshotSerializer
    {
        lock (_lock)
        {
            var existing = _serializers.OfType<T>().FirstOrDefault();
            if (existing is null)
                return;

            var replacement = createReplacement(existing);

            // The lock is reentrant, so the callback may have changed the collection.
            var index = Array.IndexOf(_serializers, existing);
            if (index < 0)
                return;

            ISnapshotSerializer[] updated = [.. _serializers];
            updated[index] = replacement;
            Volatile.Write(ref _serializers, updated);
        }
    }

    public SerializedSnapshot Serialize(SnapshotType type, object? value)
    {
        var serializers = Current;
        for (var i = serializers.Length - 1; i >= 0; i--)
        {
            var serializer = serializers[i];
            if (!serializer.TrySerialize(type, value, out var result))
                continue;

            if (result is null)
                throw new InvalidOperationException($"Serializer '{serializer.GetType()}' returned a null snapshot.");

            return result;
        }

        throw new InvalidOperationException($"No suitable serializer found for '{type.DisplayName}' and value type '{value?.GetType()}'.");
    }

    public IEnumerator<ISnapshotSerializer> GetEnumerator() => ((IEnumerable<ISnapshotSerializer>)Current).GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
