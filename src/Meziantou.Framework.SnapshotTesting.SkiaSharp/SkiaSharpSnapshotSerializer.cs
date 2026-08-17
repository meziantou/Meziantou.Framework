using SkiaSharp;

namespace Meziantou.Framework.SnapshotTesting.SkiaSharp;

internal sealed class SkiaSharpSnapshotSerializer : ISnapshotSerializer
{
    private const int Quality = 100;

    public bool TrySerialize(SnapshotType type, object? value, [NotNullWhen(true)] out SerializedSnapshot? result)
    {
        if (!TryGetEncodedImageFormat(type, out var format) || !TryGetImage(value, out var image, out var ownsImage))
        {
            result = null;
            return false;
        }

        try
        {
            using var data = image.Encode(format, Quality);
            if (data is null)
            {
                result = null;
                return false;
            }

            result = new SerializedSnapshot([new SnapshotData(type.FileExtension, data.ToArray())]);
            return true;
        }
        finally
        {
            if (ownsImage)
            {
                image.Dispose();
            }
        }
    }

    private static bool TryGetImage(object? value, [NotNullWhen(true)] out SKImage? image, out bool ownsImage)
    {
        switch (value)
        {
            case SKImage skImage:
                image = skImage;
                ownsImage = false;
                return true;

            case SKBitmap bitmap:
                image = SKImage.FromBitmap(bitmap);
                ownsImage = true;
                return image is not null;

            case SKPixmap pixmap:
                image = SKImage.FromPixelCopy(pixmap);
                ownsImage = true;
                return image is not null;

            case SKSurface surface:
                image = surface.Snapshot();
                ownsImage = true;
                return image is not null;

            default:
                image = null;
                ownsImage = false;
                return false;
        }
    }

    private static bool TryGetEncodedImageFormat(SnapshotType type, out SKEncodedImageFormat format)
    {
        if (type == SnapshotType.Png)
        {
            format = SKEncodedImageFormat.Png;
            return true;
        }

        if (type == SnapshotType.Jpeg)
        {
            format = SKEncodedImageFormat.Jpeg;
            return true;
        }

        if (type == SnapshotType.Webp)
        {
            format = SKEncodedImageFormat.Webp;
            return true;
        }

        format = default;
        return false;
    }
}
