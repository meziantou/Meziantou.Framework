using Meziantou.Framework.Imaging;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.SnapshotTesting.Imaging;

/// <summary>
/// Serializes <see cref="Image"/> values to the format of the snapshot type: PNG, JPEG, BMP, TIFF, WebP, GIF, ICO, CUR, ANI,
/// QOI, TGA, PNM, AVIF, HEIC, JPEG XL or JPEG 2000. Every format but GIF and JPEG is written without loss: GIF keeps at most
/// 256 colors and binary transparency, and JPEG uses the highest quality. 16-bit samples are kept by PNG, TIFF, PNM, JPEG XL
/// and JPEG 2000, reduced to 12 bits by AVIF and HEIC, and to 8 bits by the other formats. Metadata the format cannot hold
/// is dropped, since the snapshot is about the pixels.
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

        if (type == ImageSnapshotTypes.Cur)
            return new IcoEncoder { Kind = IconKind.Cursor, MetadataHandling = MetadataHandling.DiscardUnsupported };

        if (type == ImageSnapshotTypes.Ani)
            return new AniEncoder { MetadataHandling = MetadataHandling.DiscardUnsupported };

        if (type == ImageSnapshotTypes.Qoi)
            return new QoiEncoder { AllowBitDepthReduction = true, MetadataHandling = MetadataHandling.DiscardUnsupported };

        if (type == ImageSnapshotTypes.Tga)
            return new TgaEncoder { AllowBitDepthReduction = true, MetadataHandling = MetadataHandling.DiscardUnsupported };

        if (type == ImageSnapshotTypes.Pnm)
            return new PnmEncoder { MetadataHandling = MetadataHandling.DiscardUnsupported };

        if (type == SnapshotType.Avif)
            return new AvifEncoder { Compression = AvifCompression.Lossless, AllowBitDepthReduction = true, MetadataHandling = MetadataHandling.DiscardUnsupported };

        if (type == SnapshotType.Heic)
            return new HeicEncoder { Compression = HeicCompression.Lossless, AllowBitDepthReduction = true, MetadataHandling = MetadataHandling.DiscardUnsupported };

        if (type == SnapshotType.JpegXl)
            return new JpegXlEncoder { Compression = JpegXlCompression.Lossless, MetadataHandling = MetadataHandling.DiscardUnsupported };

        if (type == SnapshotType.Jpeg2000)
            return new Jpeg2000Encoder { Compression = Jpeg2000Compression.Lossless, MetadataHandling = MetadataHandling.DiscardUnsupported };

        return null;
    }
}
