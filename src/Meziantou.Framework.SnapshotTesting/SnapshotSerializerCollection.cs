namespace Meziantou.Framework.SnapshotTesting;

public sealed class SnapshotSerializerCollection : IEnumerable<ISnapshotSerializer>
{
    private readonly List<ISnapshotSerializer> _serializers;

    public SnapshotSerializerCollection()
    {
        _serializers = [];
    }

    internal SnapshotSerializerCollection(SnapshotSerializerCollection source)
    {
        _serializers = [.. source._serializers];
    }

    public int Count => _serializers.Count;

    /// <summary>Adds an untyped serializer. Untyped serializers are matched using <see cref="ISnapshotSerializer.TrySerialize"/>.</summary>
    public void Add(ISnapshotSerializer serializer)
    {
        ArgumentNullException.ThrowIfNull(serializer);
        _serializers.Add(serializer);
    }

    public bool Remove(ISnapshotSerializer serializer) => _serializers.Remove(serializer);
    public void Clear() => _serializers.Clear();

    public SerializedSnapshot Serialize(SnapshotType type, object? value)
    {
        for (var i = _serializers.Count - 1; i >= 0; i--)
        {
            var serializer = _serializers[i];
            if (!serializer.TrySerialize(type, value, out var result))
                continue;

            if (result is null)
                throw new InvalidOperationException($"Serializer '{serializer.GetType()}' returned a null snapshot.");

            return result;
        }

        throw new InvalidOperationException($"No suitable serializer found for '{type.DisplayName}' and value type '{value?.GetType()}'.");
    }

    public IEnumerator<ISnapshotSerializer> GetEnumerator() => _serializers.GetEnumerator();
    System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
}
