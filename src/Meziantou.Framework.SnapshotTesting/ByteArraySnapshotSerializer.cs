namespace Meziantou.Framework.SnapshotTesting;

internal sealed class ByteArraySnapshotSerializer : ISnapshotSerializer
{
    public static ISnapshotSerializer Instance { get; } = new ByteArraySnapshotSerializer();

    public bool CanSerialize(SnapshotType type, object? value) => value is byte[];
    public SerializedSnapshot Serialize(SnapshotType type, object? value)
    {
        if (value is not byte[] byteArray)
            throw new ArgumentException("Value must be a byte array.", nameof(value));

        return new SerializedSnapshot([new SnapshotData(type.FileExtension, byteArray)]);
    }
}
