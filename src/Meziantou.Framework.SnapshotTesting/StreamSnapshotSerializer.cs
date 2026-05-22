namespace Meziantou.Framework.SnapshotTesting;

internal sealed class StreamSnapshotSerializer : ISnapshotSerializer
{
    public static ISnapshotSerializer Instance { get; } = new StreamSnapshotSerializer();

    public bool TrySerialize(SnapshotType type, object? value, [NotNullWhen(true)] out SerializedSnapshot? result)
    {
        if (value is not Stream stream)
        {
            result = null;
            return false;
        }

        var ms = new MemoryStream();
        stream.CopyTo(ms);
        var data = ms.ToArray();
        result = new SerializedSnapshot([new SnapshotData(type.FileExtension, data)]);
        return true;
    }
}
