namespace Meziantou.Framework.SnapshotTesting;

internal sealed class IcoSnapshotSerializer : ISnapshotSerializer
{
    public static ISnapshotSerializer Instance { get; } = new IcoSnapshotSerializer();

    public bool TrySerialize(SnapshotType type, object? value, [NotNullWhen(true)] out SerializedSnapshot? result)
    {
        if (type != SnapshotType.Ico || value is not byte[] icoData || !IcoImageLoader.TryExtractImages(icoData, out var images))
        {
            result = null;
            return false;
        }

        var snapshotData = new SnapshotData[images.Count];
        for (var i = 0; i < images.Count; i++)
        {
            snapshotData[i] = new SnapshotData(SnapshotType.Png.FileExtension, PngImageEncoder.Encode(images[i]));
        }

        result = new SerializedSnapshot(snapshotData);
        return true;
    }
}
