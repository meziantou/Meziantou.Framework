namespace Meziantou.Framework.SnapshotTesting;

internal sealed class StreamSnapshotSerializer : ISnapshotSerializer
{
    public static ISnapshotSerializer Instance { get; } = new StreamSnapshotSerializer();

    public bool CanSerialize(SnapshotType type, object? value) => value is Stream;
    public SerializedSnapshot Serialize(SnapshotType type, object? value)
    {
        if (value is not Stream stream)
            throw new ArgumentException("Value must be a stream.", nameof(value));

        var ms = new MemoryStream();
        stream.CopyTo(ms);
        var data = ms.ToArray();
        return new SerializedSnapshot([new SnapshotData(type.FileExtension, data)]);
    }
}
