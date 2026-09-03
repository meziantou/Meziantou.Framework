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

    /// <summary>Replaces <paramref name="oldSerializer"/> with <paramref name="newSerializer"/>, or appends it when the old one is no longer registered.</summary>
    /// <remarks>Replacing in place avoids the window where a clear-then-refill would expose a partial collection to a concurrent serialization.</remarks>
    internal void Replace(ISnapshotSerializer oldSerializer, ISnapshotSerializer newSerializer)
    {
        lock (_lock)
        {
            var index = Array.IndexOf(_serializers, oldSerializer);
            if (index < 0)
            {
                Volatile.Write(ref _serializers, [.. _serializers, newSerializer]);
            }
            else
            {
                ISnapshotSerializer[] updated = [.. _serializers];
                updated[index] = newSerializer;
                Volatile.Write(ref _serializers, updated);
            }
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
