using Meziantou.Framework.Imaging;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.SnapshotTesting.Imaging;

/// <summary>
/// Serializes <see cref="Image"/> values to the format of the snapshot type: PNG, JPEG, BMP, TIFF, WebP, GIF or ICO. PNG,
/// BMP, TIFF, WebP (lossless) and ICO keep the pixels exactly; GIF keeps at most 256 colors and binary transparency; JPEG
/// uses the highest quality. Metadata the format cannot hold is dropped, since the snapshot is about the pixels.
/// </summary>
internal sealed class ImageSnapshotSerializer : ISnapshotSerializer
{
    public bool TrySerialize(SnapshotType type, object? value, [NotNullWhen(true)] out SerializedSnapshot? result)
    {
        if (value is not Image image || CreateEncoder(type) is not { } encoder)
        {
            result = null;
            return false;
        }

        using var stream = new MemoryStream();
        image.Save(stream, encoder);
        result = new SerializedSnapshot([new SnapshotData(type.FileExtension, stream.ToArray())]);
        return true;
    }

    private static ImageEncoder? CreateEncoder(SnapshotType type)
    {
        if (type == SnapshotType.Png)
            return new PngEncoder { MetadataHandling = MetadataHandling.DiscardUnsupported };

        if (type == SnapshotType.Jpeg)
            return new JpegEncoder { Quality = 100, AllowBitDepthReduction = true, MetadataHandling = MetadataHandling.DiscardUnsupported };

        if (type == SnapshotType.Bmp)
            return new BmpEncoder { MetadataHandling = MetadataHandling.DiscardUnsupported };

        if (type == SnapshotType.Tiff)
            return new TiffEncoder { MetadataHandling = MetadataHandling.DiscardUnsupported };

        if (type == SnapshotType.Webp)
            return new WebPEncoder { Compression = WebPCompression.Lossless, MetadataHandling = MetadataHandling.DiscardUnsupported };

        if (type == SnapshotType.Gif)
            return new GifEncoder { MetadataHandling = MetadataHandling.DiscardUnsupported };

        if (type == SnapshotType.Ico)
            return new IcoEncoder { MetadataHandling = MetadataHandling.DiscardUnsupported };

        return null;
    }
}
