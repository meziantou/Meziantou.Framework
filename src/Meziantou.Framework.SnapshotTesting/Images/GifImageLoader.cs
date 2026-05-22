using System.Buffers.Binary;

namespace Meziantou.Framework.SnapshotTesting;

internal static class GifImageLoader
{
    private static readonly int[] InterlacedRowStarts = [0, 4, 2, 1];
    private static readonly int[] InterlacedRowSteps = [8, 8, 4, 2];

    internal static bool TryLoadSingleFrame(ReadOnlySpan<byte> data, [NotNullWhen(true)] out Image? image)
    {
        image = null;
        if (!IsGifHeader(data))
            return false;

        var logicalWidth = BinaryPrimitives.ReadUInt16LittleEndian(data[6..8]);
        var logicalHeight = BinaryPrimitives.ReadUInt16LittleEndian(data[8..10]);
        if (logicalWidth == 0 || logicalHeight == 0)
            return false;

        var packedFields = data[10];
        var hasGlobalColorTable = (packedFields & 0b1000_0000) != 0;
        var globalColorTableEntryCount = 1 << ((packedFields & 0b0000_0111) + 1);
        var backgroundColorIndex = data[11];
        var offset = 13;

        Argb[]? globalPalette = null;
        if (hasGlobalColorTable && !TryReadColorTable(data, ref offset, globalColorTableEntryCount, out globalPalette))
            return false;

        var transparentColorIndex = -1;
        while (offset < data.Length)
        {
            var blockType = data[offset];
            offset++;
            switch (blockType)
            {
                case 0x21:
                {
                    if (offset >= data.Length)
                        return false;

                    var extensionLabel = data[offset];
                    offset++;
                    if (extensionLabel == 0xF9)
                    {
                        if (!TryReadGraphicControlExtension(data, ref offset, out transparentColorIndex))
                            return false;
                    }
                    else
                    {
                        if (!TrySkipSubBlocks(data, ref offset))
                            return false;
                    }

                    break;
                }
                case 0x2C:
                {
                    if (offset + 9 > data.Length)
                        return false;

                    var left = BinaryPrimitives.ReadUInt16LittleEndian(data[offset..(offset + 2)]);
                    var top = BinaryPrimitives.ReadUInt16LittleEndian(data[(offset + 2)..(offset + 4)]);
                    var width = BinaryPrimitives.ReadUInt16LittleEndian(data[(offset + 4)..(offset + 6)]);
                    var height = BinaryPrimitives.ReadUInt16LittleEndian(data[(offset + 6)..(offset + 8)]);
                    if (width == 0 || height == 0)
                        return false;

                    var imagePackedFields = data[offset + 8];
                    var isInterlaced = (imagePackedFields & 0b0100_0000) != 0;
                    var hasLocalColorTable = (imagePackedFields & 0b1000_0000) != 0;
                    offset += 9;

                    Argb[]? localPalette = null;
                    if (hasLocalColorTable)
                    {
                        var localColorTableEntryCount = 1 << ((imagePackedFields & 0b0000_0111) + 1);
                        if (!TryReadColorTable(data, ref offset, localColorTableEntryCount, out localPalette))
                            return false;
                    }

                    var palette = localPalette ?? globalPalette;
                    if (palette is null)
                        return false;

                    if (offset >= data.Length)
                        return false;

                    var lzwMinimumCodeSize = data[offset];
                    offset++;
                    if (!TryReadSubBlocks(data, ref offset, out var compressedImageData))
                        return false;

                    var pixelCount = checked((int)width * height);
                    if (!TryDecodeLzwImageData(compressedImageData, lzwMinimumCodeSize, pixelCount, out var colorIndexes))
                        return false;

                    var pixels = new Argb[checked((int)logicalWidth * logicalHeight)];
                    if (globalPalette is not null && backgroundColorIndex < globalPalette.Length && backgroundColorIndex != transparentColorIndex)
                    {
                        Array.Fill(pixels, globalPalette[backgroundColorIndex]);
                    }

                    if (left > logicalWidth - width || top > logicalHeight - height)
                        return false;

                    var rowMapping = isInterlaced ? CreateInterlacedRowMapping(height) : null;
                    for (var sourceRow = 0; sourceRow < height; sourceRow++)
                    {
                        var targetRow = rowMapping is null ? sourceRow : rowMapping[sourceRow];
                        var destinationRow = checked((int)(top + targetRow) * logicalWidth + left);
                        var sourceRowOffset = checked((int)sourceRow * width);
                        for (var x = 0; x < width; x++)
                        {
                            var paletteIndex = colorIndexes[sourceRowOffset + x];
                            if (paletteIndex >= palette.Length)
                                return false;

                            if (paletteIndex == transparentColorIndex)
                                continue;

                            pixels[destinationRow + x] = palette[paletteIndex];
                        }
                    }

                    image = Image.Create(logicalWidth, logicalHeight, pixels);
                    return true;
                }
                case 0x3B:
                    return false;
                default:
                    return false;
            }
        }

        return false;
    }

    private static int[] CreateInterlacedRowMapping(int height)
    {
        var rows = new int[height];
        var index = 0;
        for (var pass = 0; pass < InterlacedRowStarts.Length; pass++)
        {
            for (var row = InterlacedRowStarts[pass]; row < height; row += InterlacedRowSteps[pass])
            {
                rows[index] = row;
                index++;
            }
        }

        return rows;
    }

    private static bool TryReadGraphicControlExtension(ReadOnlySpan<byte> data, ref int offset, out int transparentColorIndex)
    {
        transparentColorIndex = -1;
        if (offset >= data.Length)
            return false;

        var blockSize = data[offset];
        offset++;
        if (blockSize < 4 || offset + blockSize > data.Length)
            return false;

        var packedFields = data[offset];
        if ((packedFields & 0b0000_0001) != 0)
        {
            transparentColorIndex = data[offset + 3];
        }

        offset += blockSize;
        if (offset >= data.Length || data[offset] != 0)
            return false;

        offset++;
        return true;
    }

    private static bool TryReadColorTable(ReadOnlySpan<byte> data, ref int offset, int entryCount, [NotNullWhen(true)] out Argb[]? palette)
    {
        palette = null;
        if (entryCount <= 0)
            return false;

        var size = checked(entryCount * 3);
        if (offset + size > data.Length)
            return false;

        var table = new Argb[entryCount];
        for (var index = 0; index < entryCount; index++)
        {
            var tableOffset = offset + index * 3;
            var red = data[tableOffset];
            var green = data[tableOffset + 1];
            var blue = data[tableOffset + 2];
            table[index] = new Argb(0xFF, red, green, blue);
        }

        offset += size;
        palette = table;
        return true;
    }

    private static bool TryReadSubBlocks(ReadOnlySpan<byte> data, ref int offset, [NotNullWhen(true)] out byte[]? result)
    {
        result = null;
        using var stream = new MemoryStream();
        while (offset < data.Length)
        {
            var blockLength = data[offset];
            offset++;
            if (blockLength == 0)
            {
                result = stream.ToArray();
                return true;
            }

            if (offset + blockLength > data.Length)
                return false;

            stream.Write(data[offset..(offset + blockLength)]);
            offset += blockLength;
        }

        return false;
    }

    private static bool TrySkipSubBlocks(ReadOnlySpan<byte> data, ref int offset)
    {
        while (offset < data.Length)
        {
            var blockLength = data[offset];
            offset++;
            if (blockLength == 0)
                return true;

            if (offset + blockLength > data.Length)
                return false;

            offset += blockLength;
        }

        return false;
    }

    private static bool TryDecodeLzwImageData(ReadOnlySpan<byte> compressedData, byte minimumCodeSize, int expectedPixelCount, [NotNullWhen(true)] out byte[]? indexes)
    {
        indexes = null;
        if (minimumCodeSize is < 2 or > 8)
            return false;

        Span<ushort> prefix = stackalloc ushort[4096];
        Span<byte> suffix = stackalloc byte[4096];
        Span<byte> decodeStack = stackalloc byte[4096];

        var clearCode = 1 << minimumCodeSize;
        var endCode = clearCode + 1;
        var nextCode = endCode + 1;
        var codeSize = minimumCodeSize + 1;

        for (var code = 0; code < clearCode; code++)
        {
            suffix[code] = (byte)code;
        }

        var output = new byte[expectedPixelCount];
        var outputOffset = 0;
        var previousCode = -1;

        var bitBuffer = 0;
        var bitCount = 0;
        var sourceOffset = 0;
        while (TryReadLzwCode(compressedData, ref sourceOffset, ref bitBuffer, ref bitCount, codeSize, out var code))
        {
            if (code == clearCode)
            {
                codeSize = minimumCodeSize + 1;
                nextCode = endCode + 1;
                previousCode = -1;
                continue;
            }

            if (code == endCode)
            {
                if (outputOffset != expectedPixelCount)
                    return false;

                indexes = output;
                return true;
            }

            if (code > nextCode || code >= 4096)
                return false;

            var decodeStackLength = 0;
            var inputCode = code;
            if (code == nextCode)
            {
                if (previousCode < 0)
                    return false;

                decodeStack[decodeStackLength] = GetFirstDecodedSuffix(previousCode, clearCode, prefix, suffix);
                decodeStackLength++;
                code = previousCode;
            }

            while (code >= clearCode)
            {
                if (code >= 4096 || decodeStackLength >= decodeStack.Length)
                    return false;

                decodeStack[decodeStackLength] = suffix[code];
                decodeStackLength++;
                code = prefix[code];
            }

            if (decodeStackLength >= decodeStack.Length)
                return false;

            var firstSuffix = suffix[code];
            decodeStack[decodeStackLength] = firstSuffix;
            decodeStackLength++;

            for (var index = decodeStackLength - 1; index >= 0; index--)
            {
                if (outputOffset >= output.Length)
                    return false;

                output[outputOffset] = decodeStack[index];
                outputOffset++;
            }

            if (previousCode >= 0 && nextCode < 4096)
            {
                prefix[nextCode] = (ushort)previousCode;
                suffix[nextCode] = firstSuffix;
                nextCode++;
                if (nextCode == (1 << codeSize) && codeSize < 12)
                {
                    codeSize++;
                }
            }

            previousCode = inputCode;
        }

        return false;
    }

    private static byte GetFirstDecodedSuffix(int code, int clearCode, ReadOnlySpan<ushort> prefix, ReadOnlySpan<byte> suffix)
    {
        while (code >= clearCode)
        {
            code = prefix[code];
        }

        return suffix[code];
    }

    private static bool TryReadLzwCode(ReadOnlySpan<byte> data, ref int sourceOffset, ref int bitBuffer, ref int bitCount, int codeSize, out int code)
    {
        while (bitCount < codeSize)
        {
            if (sourceOffset >= data.Length)
            {
                code = 0;
                return false;
            }

            bitBuffer |= data[sourceOffset] << bitCount;
            sourceOffset++;
            bitCount += 8;
        }

        code = bitBuffer & ((1 << codeSize) - 1);
        bitBuffer >>= codeSize;
        bitCount -= codeSize;
        return true;
    }

    private static bool IsGifHeader(ReadOnlySpan<byte> source)
    {
        if (source.Length < 14)
            return false;

        if (source[0] != (byte)'G' || source[1] != (byte)'I' || source[2] != (byte)'F')
            return false;

        if (source[3] != (byte)'8')
            return false;

        return (source[4], source[5]) is ((byte)'7', (byte)'a') or ((byte)'9', (byte)'a');
    }
}
