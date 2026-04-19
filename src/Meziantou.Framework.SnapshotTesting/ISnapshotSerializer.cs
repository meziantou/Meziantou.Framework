namespace Meziantou.Framework.SnapshotTesting;

public interface ISnapshotSerializer
{
    bool CanSerialize(SnapshotType type, object? value);

    IReadOnlyList<SnapshotData> Serialize(SnapshotType type, object? value);
}

internal sealed class ByteArraySnapshotSerializer : ISnapshotSerializer
{
    public static ISnapshotSerializer Instance { get; } = new ByteArraySnapshotSerializer();

    public bool CanSerialize(SnapshotType type, object? value) => value is byte[];
    public IReadOnlyList<SnapshotData> Serialize(SnapshotType type, object? value)
    {
        if (value is not byte[] byteArray)
            throw new ArgumentException("Value must be a byte array.", nameof(value));

        return [new SnapshotData(type.FileExtension, byteArray)];
    }
}

internal sealed class StreamSnapshotSerializer : ISnapshotSerializer
{
    public static ISnapshotSerializer Instance { get; } = new StreamSnapshotSerializer();

    public bool CanSerialize(SnapshotType type, object? value) => value is Stream;
    public IReadOnlyList<SnapshotData> Serialize(SnapshotType type, object? value)
    {
        if (value is not Stream stream)
            throw new ArgumentException("Value must be a stream.", nameof(value));

        var ms = new MemoryStream();
        stream.CopyTo(ms);
        var data = ms.ToArray();
        return [new SnapshotData(type.FileExtension, data)];
    }
}