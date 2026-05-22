namespace Meziantou.Framework.SnapshotTesting;

internal sealed class ByteArraySnapshotSerializer : ISnapshotSerializer
{
    public static ISnapshotSerializer Instance { get; } = new ByteArraySnapshotSerializer();

    public bool TrySerialize(SnapshotType type, object? value, [NotNullWhen(true)] out SerializedSnapshot? result)
    {
        if (value is not byte[] byteArray)
        {
            result = null;
            return false;
        }

        result = new SerializedSnapshot([new SnapshotData(type.FileExtension, byteArray)]);
        return true;
    }
}
