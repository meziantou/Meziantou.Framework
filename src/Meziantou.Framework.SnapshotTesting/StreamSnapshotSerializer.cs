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

        // Read from wherever the stream currently is, rather than rewinding it: seeking would change what a
        // caller who deliberately positioned the stream gets back.
        var content = SnapshotUpdateStrategy.ReadToEnd(stream, GetRemainingLength(stream));
        result = new SerializedSnapshot([new SnapshotData(type.FileExtension, content)]);
        return true;
    }

    private static long GetRemainingLength(Stream stream)
    {
        if (!stream.CanSeek)
            return -1;

        try
        {
            return Math.Max(0, stream.Length - stream.Position);
        }
        catch (NotSupportedException)
        {
            return -1;
        }
    }
}
