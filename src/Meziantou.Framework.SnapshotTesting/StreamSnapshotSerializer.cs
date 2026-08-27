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

        // The caller usually hands over a stream they have just written to, so its position sits at the
        // end and copying from there would silently snapshot zero bytes.
        if (stream.CanSeek)
        {
            stream.Seek(0, SeekOrigin.Begin);
        }

        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        result = new SerializedSnapshot([new SnapshotData(type.FileExtension, ms.ToArray())]);
        return true;
    }
}
