using SixLabors.ImageSharp;

namespace Meziantou.Framework.SnapshotTesting.ImageSharp;

internal sealed class ImageSnapshotSerializer : ISnapshotSerializer
{
    public bool CanSerialize(SnapshotType type, object? value) => value is Image;
    public IReadOnlyList<SnapshotData> Serialize(SnapshotType type, object? value)
    {
        if (value is not Image image)
            throw new ArgumentException("Value must be an Image.", nameof(value));

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

        return [new SnapshotData(type.FileExtension, ms.ToArray())];
    }
}
