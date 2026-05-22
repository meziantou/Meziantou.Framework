using SixLabors.ImageSharp;

namespace Meziantou.Framework.SnapshotTesting.ImageSharp;

internal sealed class ImageSnapshotSerializer : ISnapshotSerializer
{
    public bool TrySerialize(SnapshotType type, object? value, [NotNullWhen(true)] out SerializedSnapshot? result)
    {
        if (value is not Image image)
        {
            result = null;
            return false;
        }

        using var ms = new MemoryStream();
        if (type == SnapshotType.Png)
        {
            image.SaveAsPng(ms);
        }
        else if (type == SnapshotType.Jpeg)
        {
            image.SaveAsJpeg(ms);
        }
        else if (type == SnapshotType.Bmp)
        {
            image.SaveAsBmp(ms);
        }
        else if (type == SnapshotType.Tiff)
        {
            image.SaveAsTiff(ms);
        }
        else if (type == SnapshotType.Webp)
        {
            image.SaveAsWebp(ms);
        }
        else
        {
            result = null;
            return false;
        }

        result = new SerializedSnapshot([new SnapshotData(type.FileExtension, ms.ToArray())]);
        return true;
    }
}
