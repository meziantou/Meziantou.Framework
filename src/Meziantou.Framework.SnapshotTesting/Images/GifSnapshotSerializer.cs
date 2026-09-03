namespace Meziantou.Framework.SnapshotTesting;

internal sealed class GifSnapshotSerializer : ISnapshotSerializer
{
    public static ISnapshotSerializer Instance { get; } = new GifSnapshotSerializer();

    public bool TrySerialize(SnapshotType type, object? value, [NotNullWhen(true)] out SerializedSnapshot? result)
    {
        if (type != SnapshotType.Gif || value is not byte[] gifData || !GifImageLoader.TryExtractFrames(gifData, out var frames))
        {
            result = null;
            return false;
        }

        var snapshotData = new SnapshotData[frames.Count];
        for (var i = 0; i < frames.Count; i++)
        {
            snapshotData[i] = new SnapshotData(SnapshotType.Png.FileExtension, PngImageEncoder.Encode(frames[i]));
        }

        result = new SerializedSnapshot(snapshotData);
        return true;
    }
}
