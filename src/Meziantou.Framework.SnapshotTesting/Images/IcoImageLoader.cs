using System.Buffers.Binary;

namespace Meziantou.Framework.SnapshotTesting;

internal static class IcoImageLoader
{
    internal static bool TryExtractImages(ReadOnlySpan<byte> data, [NotNullWhen(true)] out List<Image>? images)
    {
        images = null;
        if (!TryReadUInt16(data, 0, out var reserved) ||
            !TryReadUInt16(data, 2, out var type) ||
            !TryReadUInt16(data, 4, out var count) ||
            reserved != 0 ||
            type != 1 ||
            count == 0)
        {
            return false;
        }

        var directorySize = checked(6 + count * 16);
        if (directorySize > data.Length)
            return false;

        var extractedImages = new List<Image>(count);
        for (var i = 0; i < count; i++)
        {
            var entryOffset = 6 + i * 16;
            var width = data[entryOffset] == 0 ? 256 : data[entryOffset];
            var height = data[entryOffset + 1] == 0 ? 256 : data[entryOffset + 1];

            if (!TryReadUInt32(data, entryOffset + 8, out var imageSize) ||
                !TryReadUInt32(data, entryOffset + 12, out var imageOffset))
            {
                return false;
            }

            if (imageOffset > int.MaxValue || imageSize > int.MaxValue)
                return false;

            var imageStart = (int)imageOffset;
            var imageLength = (int)imageSize;
            if (imageLength <= 0 || imageStart < directorySize || imageStart > data.Length - imageLength)
                return false;

            var imageData = data.Slice(imageStart, imageLength);
            if (PngImageLoader.IsPng(imageData))
            {
                extractedImages.Add(PngImageLoader.Load(imageData));
                continue;
            }

            if (!TryLoadBitmapImage(imageData, width, height, out var image))
                return false;

            extractedImages.Add(image);
        }

        if (extractedImages.Count == 0)
            return false;

        images = extractedImages;
        return true;
    }

    private static bool TryLoadBitmapImage(ReadOnlySpan<byte> data, int expectedWidth, int expectedHeight, [NotNullWhen(true)] out Image? image)
    {
        image = null;
        if (!TryReadInt32(data, 0, out var headerSize) ||
            headerSize < 40 ||
            headerSize > data.Length ||
            !TryReadInt32(data, 4, out var width) ||
            !TryReadInt32(data, 8, out var combinedHeight) ||
            !TryReadUInt16(data, 12, out var planes) ||
            !TryReadUInt16(data, 14, out var bitsPerPixel) ||
            !TryReadUInt32(data, 16, out var compression))
        {
            return false;
        }

        if (width <= 0 ||
            combinedHeight <= 0 ||
            combinedHeight % 2 != 0 ||
            planes != 1 ||
            compression != 0 ||
            bitsPerPixel is not (1 or 4 or 8 or 24 or 32))
        {
            return false;
        }

        var height = combinedHeight / 2;
        if (expectedWidth != width || expectedHeight != height)
            return false;

        if (!TryReadUInt32(data, 32, out var colorsUsedRaw))
            return false;

        var colorsUsed = checked((int)colorsUsedRaw);
        var paletteEntryCount = bitsPerPixel <= 8
            ? colorsUsed == 0 ? 1 << bitsPerPixel : colorsUsed
            : 0;
        if (paletteEntryCount < 0)
            return false;

        var offset = headerSize;
        if (offset + paletteEntryCount * 4 > data.Length)
            return false;

        Argb[]? palette = null;
        if (paletteEntryCount > 0)
        {
            palette = new Argb[paletteEntryCount];
            for (var i = 0; i < paletteEntryCount; i++)
            {
                var paletteOffset = offset + i * 4;
                var b = data[paletteOffset];
                var g = data[paletteOffset + 1];
                var r = data[paletteOffset + 2];
                palette[i] = new Argb(0xFF, r, g, b);
            }
        }

        offset += checked(paletteEntryCount * 4);

        var xorRowStride = checked(((width * bitsPerPixel + 31) / 32) * 4);
        var xorDataSize = checked(xorRowStride * height);
        if (offset + xorDataSize > data.Length)
            return false;

        var xorData = data.Slice(offset, xorDataSize);
        offset += xorDataSize;

        var andRowStride = checked(((width + 31) / 32) * 4);
        var andDataSize = checked(andRowStride * height);
        var hasAndMask = offset + andDataSize <= data.Length;
        var andMaskData = hasAndMask ? data.Slice(offset, andDataSize) : ReadOnlySpan<byte>.Empty;
        if (!hasAndMask && bitsPerPixel < 32)
            return false;

        var pixels = new Argb[checked(width * height)];
        for (var y = 0; y < height; y++)
        {
            var sourceRow = height - y - 1;
            var xorRow = xorData.Slice(sourceRow * xorRowStride, xorRowStride);
            var andRow = hasAndMask ? andMaskData.Slice(sourceRow * andRowStride, andRowStride) : ReadOnlySpan<byte>.Empty;
            for (var x = 0; x < width; x++)
            {
                if (!TryReadPixel(xorRow, palette, bitsPerPixel, x, out var pixel))
                    return false;

                if (hasAndMask && IsAndMaskTransparent(andRow, x))
                {
                    pixel = new Argb(0, 0, 0, 0);
                }

                pixels[y * width + x] = pixel;
            }
        }

        image = Image.Create(width, height, pixels);
        return true;
    }

    private static bool TryReadPixel(ReadOnlySpan<byte> rowData, Argb[]? palette, int bitsPerPixel, int x, out Argb pixel)
    {
        pixel = default;
        switch (bitsPerPixel)
        {
            case 32:
                {
                    var offset = x * 4;
                    if (offset + 4 > rowData.Length)
                        return false;

                    var b = rowData[offset];
                    var g = rowData[offset + 1];
                    var r = rowData[offset + 2];
                    var a = rowData[offset + 3];
                    pixel = new Argb(a, r, g, b);
                    return true;
                }

            case 24:
                {
                    var offset = x * 3;
                    if (offset + 3 > rowData.Length)
                        return false;

                    var b = rowData[offset];
                    var g = rowData[offset + 1];
                    var r = rowData[offset + 2];
                    pixel = new Argb(0xFF, r, g, b);
                    return true;
                }

            case 8:
                return TryReadIndexedPixel(rowData, palette, x, bitCount: 8, out pixel);
            case 4:
                return TryReadIndexedPixel(rowData, palette, x, bitCount: 4, out pixel);
            case 1:
                return TryReadIndexedPixel(rowData, palette, x, bitCount: 1, out pixel);
            default:
                return false;
        }
    }

    private static bool TryReadIndexedPixel(ReadOnlySpan<byte> rowData, Argb[]? palette, int x, int bitCount, out Argb pixel)
    {
        pixel = default;
        if (palette is null || palette.Length == 0)
            return false;

        int index;
        if (bitCount == 8)
        {
            if (x >= rowData.Length)
                return false;

            index = rowData[x];
        }
        else if (bitCount == 4)
        {
            var byteOffset = x / 2;
            if (byteOffset >= rowData.Length)
                return false;

            var value = rowData[byteOffset];
            index = (x & 1) == 0 ? value >> 4 : value & 0x0F;
        }
        else
        {
            var byteOffset = x / 8;
            if (byteOffset >= rowData.Length)
                return false;

            var value = rowData[byteOffset];
            index = (value & (0x80 >> (x & 7))) != 0 ? 1 : 0;
        }

        if (index < 0 || index >= palette.Length)
            return false;

        pixel = palette[index];
        return true;
    }

    private static bool IsAndMaskTransparent(ReadOnlySpan<byte> andRow, int x)
    {
        var byteOffset = x / 8;
        if (byteOffset >= andRow.Length)
            return false;

        var mask = (byte)(0x80 >> (x & 7));
        return (andRow[byteOffset] & mask) != 0;
    }

    private static bool TryReadUInt16(ReadOnlySpan<byte> data, int offset, out int value)
    {
        value = 0;
        if (offset + 2 > data.Length)
            return false;

        value = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..(offset + 2)]);
        return true;
    }

    private static bool TryReadInt32(ReadOnlySpan<byte> data, int offset, out int value)
    {
        value = 0;
        if (offset + 4 > data.Length)
            return false;

        value = BinaryPrimitives.ReadInt32LittleEndian(data[offset..(offset + 4)]);
        return true;
    }

    private static bool TryReadUInt32(ReadOnlySpan<byte> data, int offset, out uint value)
    {
        value = 0;
        if (offset + 4 > data.Length)
            return false;

        value = BinaryPrimitives.ReadUInt32LittleEndian(data[offset..(offset + 4)]);
        return true;
    }
}
