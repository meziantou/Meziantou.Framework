using System.Buffers.Binary;

namespace Meziantou.Framework.SnapshotTesting;

internal sealed class IcoSnapshotSerializer : ISnapshotSerializer
{
    public static ISnapshotSerializer Instance { get; } = new IcoSnapshotSerializer();

    public bool TrySerialize(SnapshotType type, object? value, [NotNullWhen(true)] out SerializedSnapshot? result)
    {
        if (type != SnapshotType.Ico || value is not byte[] icoData || !TryExtractPngImages(icoData, out var pngImages))
        {
            result = null;
            return false;
        }

        var snapshotData = new SnapshotData[pngImages.Count];
        for (var i = 0; i < pngImages.Count; i++)
        {
            snapshotData[i] = new SnapshotData(".png", pngImages[i]);
        }

        result = new SerializedSnapshot(snapshotData);
        return true;
    }

    private static bool TryExtractPngImages(byte[] source, [NotNullWhen(true)] out List<byte[]>? pngImages)
    {
        pngImages = null;
        if (source.Length < 6)
            return false;

        var reserved = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(0, 2));
        var type = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(2, 2));
        var count = BinaryPrimitives.ReadUInt16LittleEndian(source.AsSpan(4, 2));
        if (reserved != 0 || type != 1 || count == 0)
            return false;

        var directorySize = checked(6 + count * 16);
        if (directorySize > source.Length)
            return false;

        var images = new List<byte[]>(count);
        for (var index = 0; index < count; index++)
        {
            var entryOffset = 6 + index * 16;
            if (!TryReadEntryData(source, entryOffset, out var entryData))
                return false;

            if (PngImageLoader.IsPng(entryData))
            {
                images.Add(entryData.ToArray());
                continue;
            }

            if (!TryLoadIconBitmap(entryData, out var image))
                return false;

            images.Add(PngImageEncoder.Encode(image));
        }

        pngImages = images;
        return true;
    }

    private static bool TryReadEntryData(byte[] source, int entryOffset, out ReadOnlySpan<byte> entryData)
    {
        entryData = default;

        var bytesInResource = BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(entryOffset + 8, 4));
        var imageOffset = BinaryPrimitives.ReadUInt32LittleEndian(source.AsSpan(entryOffset + 12, 4));
        if (bytesInResource == 0 || bytesInResource > int.MaxValue || imageOffset > int.MaxValue)
            return false;

        var bytesInResourceInt = (int)bytesInResource;
        var imageOffsetInt = (int)imageOffset;
        if (imageOffsetInt < 0 || bytesInResourceInt < 0 || imageOffsetInt > source.Length - bytesInResourceInt)
            return false;

        entryData = source.AsSpan(imageOffsetInt, bytesInResourceInt);
        return true;
    }

    private static bool TryLoadIconBitmap(ReadOnlySpan<byte> entryData, [NotNullWhen(true)] out Image? image)
    {
        image = null;
        if (entryData.Length < 40)
            return false;

        var headerSize = BinaryPrimitives.ReadInt32LittleEndian(entryData[0..4]);
        if (headerSize < 40 || headerSize > entryData.Length)
            return false;

        var width = BinaryPrimitives.ReadInt32LittleEndian(entryData[4..8]);
        var combinedHeight = BinaryPrimitives.ReadInt32LittleEndian(entryData[8..12]);
        var planes = BinaryPrimitives.ReadUInt16LittleEndian(entryData[12..14]);
        var bitsPerPixel = BinaryPrimitives.ReadUInt16LittleEndian(entryData[14..16]);
        var compression = BinaryPrimitives.ReadUInt32LittleEndian(entryData[16..20]);
        if (width <= 0 || combinedHeight <= 0 || planes != 1 || compression != 0)
            return false;

        if (combinedHeight % 2 != 0)
            return false;

        var height = combinedHeight / 2;
        if (height <= 0)
            return false;

        if (bitsPerPixel is not 24 and not 32)
            return false;

        var xorStride = checked(((width * bitsPerPixel + 31) / 32) * 4);
        var xorSize = checked(xorStride * height);
        var xorOffset = headerSize;
        if (xorOffset > entryData.Length - xorSize)
            return false;

        var andStride = checked(((width + 31) / 32) * 4);
        var andSize = checked(andStride * height);
        var hasMask = xorOffset + xorSize + andSize <= entryData.Length;
        var andOffset = xorOffset + xorSize;

        var pixels = new Argb[checked(width * height)];
        var bytesPerPixel = bitsPerPixel / 8;
        for (var y = 0; y < height; y++)
        {
            var sourceRow = height - y - 1;
            var xorRowOffset = xorOffset + sourceRow * xorStride;
            var destinationRowOffset = y * width;
            var andRowOffset = hasMask ? andOffset + sourceRow * andStride : -1;
            for (var x = 0; x < width; x++)
            {
                var pixelOffset = xorRowOffset + x * bytesPerPixel;
                var blue = entryData[pixelOffset];
                var green = entryData[pixelOffset + 1];
                var red = entryData[pixelOffset + 2];
                var alpha = bitsPerPixel == 32 ? entryData[pixelOffset + 3] : (byte)255;

                if (hasMask)
                {
                    var maskByte = entryData[andRowOffset + x / 8];
                    var isTransparent = (maskByte & (0x80 >> (x % 8))) != 0;
                    if (isTransparent)
                    {
                        alpha = 0;
                    }
                }

                pixels[destinationRowOffset + x] = new Argb(alpha, red, green, blue);
            }
        }

        image = Image.Create(width, height, pixels);
        return true;
    }
}
