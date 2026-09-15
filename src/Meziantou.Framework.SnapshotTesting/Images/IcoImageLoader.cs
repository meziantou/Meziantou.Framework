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

        var directorySize = 6 + count * 16;
        if (directorySize > data.Length)
            return false;

        var extractedImages = new List<Image>(count);
        for (var i = 0; i < count; i++)
        {
            // The width and height of the directory entry are only hints: like Windows and Pillow, the image
            // takes its size from the header of the embedded PNG or bitmap.
            var entryOffset = 6 + i * 16;
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
                if (!TryLoadPngImage(imageData, out var pngImage))
                    return false;

                extractedImages.Add(pngImage);
                continue;
            }

            if (!TryLoadBitmapImage(imageData, out var image))
                return false;

            extractedImages.Add(image);
        }

        if (extractedImages.Count == 0)
            return false;

        images = extractedImages;
        return true;
    }

    private static bool TryLoadPngImage(ReadOnlySpan<byte> data, [NotNullWhen(true)] out Image? image)
    {
        // Image.Load reports every way a malformed image can fail as one of these two exceptions
        try
        {
            image = Image.Load(data);
            return true;
        }
        catch (InvalidDataException)
        {
        }
        catch (NotSupportedException)
        {
        }

        image = null;
        return false;
    }

    private static bool TryLoadBitmapImage(ReadOnlySpan<byte> data, [NotNullWhen(true)] out Image? image)
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
        if (!ImageLimits.IsValidSize(width, height))
            return false;

        if (!TryReadUInt32(data, 32, out var colorsUsed))
            return false;

        var offset = headerSize;
        var paletteEntryCountLong = bitsPerPixel <= 8
            ? colorsUsed == 0 ? 1L << bitsPerPixel : colorsUsed
            : 0L;
        if (paletteEntryCountLong > (data.Length - offset) / 4)
            return false;

        var paletteEntryCount = (int)paletteEntryCountLong;

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

        offset += paletteEntryCount * 4;

        // The sizes are computed in 64 bits and checked against the data before they are narrowed
        var xorRowStrideLong = ((width * (long)bitsPerPixel + 31) / 32) * 4;
        if (xorRowStrideLong * height > data.Length - offset)
            return false;

        var xorRowStride = (int)xorRowStrideLong;
        var xorDataSize = xorRowStride * height;
        var xorData = data.Slice(offset, xorDataSize);
        offset += xorDataSize;

        var andRowStride = (int)(((width + 31L) / 32) * 4);
        var hasAndMask = (long)andRowStride * height <= data.Length - offset;
        var andMaskData = hasAndMask ? data.Slice(offset, andRowStride * height) : ReadOnlySpan<byte>.Empty;
        if (!hasAndMask && bitsPerPixel < 32)
            return false;

        // A 32-bit entry carries its own alpha channel, which Windows and Pillow use instead of the AND mask.
        // Only an entry whose alpha bytes are all zero was written without one and takes its opacity from the
        // mask; with no mask either, nothing says any pixel is transparent.
        var useAndMask = hasAndMask;
        var forceOpaque = false;
        if (bitsPerPixel == 32)
        {
            var hasAlpha = HasNonZeroAlpha(xorData, xorRowStride, width, height);
            useAndMask = hasAndMask && !hasAlpha;
            forceOpaque = !hasAlpha;
        }

        var pixels = new Argb[width * height];
        for (var y = 0; y < height; y++)
        {
            var sourceRow = height - y - 1;
            var xorRow = xorData.Slice(sourceRow * xorRowStride, xorRowStride);
            var andRow = useAndMask ? andMaskData.Slice(sourceRow * andRowStride, andRowStride) : ReadOnlySpan<byte>.Empty;
            for (var x = 0; x < width; x++)
            {
                if (!TryReadPixel(xorRow, palette, bitsPerPixel, x, out var pixel))
                    return false;

                if (useAndMask && IsAndMaskTransparent(andRow, x))
                {
                    pixel = new Argb(0, 0, 0, 0);
                }
                else if (forceOpaque)
                {
                    pixel = new Argb(0xFF, pixel.R, pixel.G, pixel.B);
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

    private static bool HasNonZeroAlpha(ReadOnlySpan<byte> xorData, int rowStride, int width, int height)
    {
        for (var y = 0; y < height; y++)
        {
            var row = xorData.Slice(y * rowStride, rowStride);
            for (var x = 0; x < width; x++)
            {
                if (row[x * 4 + 3] != 0)
                    return true;
            }
        }

        return false;
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
