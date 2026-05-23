namespace Meziantou.Framework.SnapshotTesting;

internal static class GifImageLoader
{
    internal static bool TryLoad(ReadOnlySpan<byte> data, [NotNullWhen(true)] out Image? image)
    {
        image = null;
        if (!IsGifHeader(data) || data.Length < 13)
            return false;

        if (!TryReadUInt16(data, 6, out var logicalWidth) ||
            !TryReadUInt16(data, 8, out var logicalHeight) ||
            logicalWidth == 0 ||
            logicalHeight == 0)
        {
            return false;
        }

        var packedFields = data[10];
        var backgroundColorIndex = data[11];

        var offset = 13;
        Argb[]? globalColorTable = null;
        if (HasGlobalColorTable(packedFields))
        {
            if (!TryReadColorTable(data, packedFields, ref offset, out globalColorTable))
                return false;
        }

        var transparentColorIndex = -1;
        while (offset < data.Length)
        {
            var blockType = data[offset];
            offset++;

            switch (blockType)
            {
                case 0x3B: // Trailer
                    return false;
                case 0x21: // Extension block
                    if (!TryReadExtensionBlock(data, ref offset, ref transparentColorIndex))
                        return false;

                    break;
                case 0x2C: // Image descriptor
                    if (!TryReadImage(
                        data,
                        ref offset,
                        logicalWidth,
                        logicalHeight,
                        globalColorTable,
                        backgroundColorIndex,
                        transparentColorIndex,
                        out image))
                    {
                        return false;
                    }

                    return true;
                default:
                    return false;
            }
        }

        return false;
    }

    private static bool TryReadImage(
        ReadOnlySpan<byte> data,
        ref int offset,
        int logicalWidth,
        int logicalHeight,
        Argb[]? globalColorTable,
        byte backgroundColorIndex,
        int transparentColorIndex,
        [NotNullWhen(true)] out Image? image)
    {
        image = null;
        if (!TryReadUInt16(data, offset, out var imageLeft) ||
            !TryReadUInt16(data, offset + 2, out var imageTop) ||
            !TryReadUInt16(data, offset + 4, out var imageWidth) ||
            !TryReadUInt16(data, offset + 6, out var imageHeight))
        {
            return false;
        }

        if (imageWidth == 0 || imageHeight == 0)
            return false;

        if (offset + 9 > data.Length)
            return false;

        var packedFields = data[offset + 8];
        offset += 9;

        Argb[]? activeColorTable = globalColorTable;
        if (HasLocalColorTable(packedFields))
        {
            if (!TryReadColorTable(data, packedFields, ref offset, out activeColorTable))
                return false;
        }

        if (activeColorTable is null)
            return false;

        if (offset >= data.Length)
            return false;

        var lzwMinimumCodeSize = data[offset];
        offset++;

        if (!TryReadSubBlocks(data, ref offset, out var compressedData))
            return false;

        var expectedPixelCount = checked(imageWidth * imageHeight);
        if (!TryDecodeLzw(compressedData, lzwMinimumCodeSize, expectedPixelCount, out var colorIndexes))
            return false;

        var background = new Argb(0x00000000u);
        if (transparentColorIndex < 0 &&
            globalColorTable is not null &&
            backgroundColorIndex < globalColorTable.Length)
        {
            background = globalColorTable[backgroundColorIndex];
        }

        var pixels = new Argb[checked(logicalWidth * logicalHeight)];
        Array.Fill(pixels, background);

        var interlaced = (packedFields & 0b0100_0000) != 0;
        var rowOrder = interlaced ? BuildInterlaceRowOrder(imageHeight) : null;
        for (var sourceRow = 0; sourceRow < imageHeight; sourceRow++)
        {
            var imageRow = rowOrder is null ? sourceRow : rowOrder[sourceRow];
            var targetY = imageTop + imageRow;
            if (targetY < 0 || targetY >= logicalHeight)
                continue;

            var sourceRowOffset = sourceRow * imageWidth;
            var destinationRowOffset = targetY * logicalWidth;
            for (var x = 0; x < imageWidth; x++)
            {
                var targetX = imageLeft + x;
                if (targetX < 0 || targetX >= logicalWidth)
                    continue;

                var paletteIndex = colorIndexes[sourceRowOffset + x];
                if (paletteIndex >= activeColorTable.Length)
                    return false;

                if (paletteIndex == transparentColorIndex)
                    continue;

                pixels[destinationRowOffset + targetX] = activeColorTable[paletteIndex];
            }
        }

        image = Image.Create(logicalWidth, logicalHeight, pixels);
        return true;
    }

    private static int[] BuildInterlaceRowOrder(int height)
    {
        var rows = new int[height];
        var index = 0;

        AddRows(start: 0, step: 8);
        AddRows(start: 4, step: 8);
        AddRows(start: 2, step: 4);
        AddRows(start: 1, step: 2);
        return rows;

        void AddRows(int start, int step)
        {
            for (var row = start; row < height; row += step)
            {
                rows[index] = row;
                index++;
            }
        }
    }

    private static bool TryDecodeLzw(ReadOnlySpan<byte> compressedData, byte minimumCodeSize, int expectedPixelCount, [NotNullWhen(true)] out byte[]? colorIndexes)
    {
        colorIndexes = null;
        if (minimumCodeSize is < 2 or > 8)
            return false;

        var clearCode = 1 << minimumCodeSize;
        var endCode = clearCode + 1;
        var availableCode = endCode + 1;
        var codeSize = minimumCodeSize + 1;

        Span<short> prefix = stackalloc short[4096];
        Span<byte> suffix = stackalloc byte[4096];
        Span<byte> stack = stackalloc byte[4096];
        for (var i = 0; i < clearCode; i++)
        {
            prefix[i] = -1;
            suffix[i] = (byte)i;
        }

        var output = new byte[expectedPixelCount];
        var outputOffset = 0;

        var bitReader = new GifBitReader(compressedData);
        var previousCode = -1;
        while (bitReader.TryRead(codeSize, out var code))
        {
            if (code == clearCode)
            {
                availableCode = endCode + 1;
                codeSize = minimumCodeSize + 1;
                previousCode = -1;
                continue;
            }

            if (code == endCode)
            {
                colorIndexes = outputOffset == output.Length ? output : null;
                return outputOffset == output.Length;
            }

            if (code > availableCode || code >= 4096)
                return false;

            var currentCode = code;
            var stackLength = 0;
            byte firstPixel;
            if (code == availableCode)
            {
                if (previousCode < 0 || !TryExpandCode(previousCode, clearCode, prefix, suffix, stack, ref stackLength, out firstPixel))
                    return false;

                if (stackLength >= stack.Length)
                    return false;

                stack[stackLength] = firstPixel;
                stackLength++;
            }
            else
            {
                if (!TryExpandCode(code, clearCode, prefix, suffix, stack, ref stackLength, out firstPixel))
                    return false;
            }

            for (var i = stackLength - 1; i >= 0; i--)
            {
                if (outputOffset >= output.Length)
                    return false;

                output[outputOffset] = stack[i];
                outputOffset++;
            }

            if (previousCode >= 0)
            {
                if (availableCode < 4096)
                {
                    prefix[availableCode] = checked((short)previousCode);
                    suffix[availableCode] = firstPixel;
                    availableCode++;
                    if (availableCode == (1 << codeSize) && codeSize < 12)
                    {
                        codeSize++;
                    }
                }
            }

            previousCode = currentCode;
            if (outputOffset == output.Length)
            {
                colorIndexes = output;
                return true;
            }
        }

        return false;
    }

    private static bool TryExpandCode(
        int code,
        int clearCode,
        ReadOnlySpan<short> prefix,
        ReadOnlySpan<byte> suffix,
        Span<byte> stack,
        ref int stackLength,
        out byte firstPixel)
    {
        firstPixel = 0;
        if (code < 0 || code >= 4096)
            return false;

        var current = code;
        while (current >= clearCode)
        {
            if (current >= prefix.Length || stackLength >= stack.Length)
                return false;

            stack[stackLength] = suffix[current];
            stackLength++;

            current = prefix[current];
            if (current < 0)
                return false;
        }

        firstPixel = checked((byte)current);
        if (stackLength >= stack.Length)
            return false;

        stack[stackLength] = firstPixel;
        stackLength++;
        return true;
    }

    private static bool TryReadExtensionBlock(ReadOnlySpan<byte> data, ref int offset, ref int transparentColorIndex)
    {
        if (offset >= data.Length)
            return false;

        var extensionType = data[offset];
        offset++;
        if (extensionType != 0xF9)
            return TrySkipSubBlocks(data, ref offset);

        if (offset + 6 > data.Length)
            return false;

        var blockSize = data[offset];
        offset++;
        if (blockSize != 4)
            return false;

        var packedFields = data[offset];
        var hasTransparency = (packedFields & 0b0000_0001) != 0;
        transparentColorIndex = hasTransparency ? data[offset + 3] : -1;
        offset += 4;

        if (data[offset] != 0)
            return false;

        offset++;
        return true;
    }

    private static bool TryReadColorTable(ReadOnlySpan<byte> data, byte packedFields, ref int offset, [NotNullWhen(true)] out Argb[]? colorTable)
    {
        colorTable = null;
        var colorCount = 1 << ((packedFields & 0b0000_0111) + 1);
        var colorTableByteCount = checked(colorCount * 3);
        if (offset + colorTableByteCount > data.Length)
            return false;

        colorTable = new Argb[colorCount];
        for (var i = 0; i < colorCount; i++)
        {
            var colorOffset = offset + i * 3;
            colorTable[i] = new Argb(0xFF, data[colorOffset], data[colorOffset + 1], data[colorOffset + 2]);
        }

        offset += colorTableByteCount;
        return true;
    }

    private static bool TryReadSubBlocks(ReadOnlySpan<byte> data, ref int offset, [NotNullWhen(true)] out byte[]? buffer)
    {
        buffer = null;
        using var stream = new MemoryStream();
        while (offset < data.Length)
        {
            var blockSize = data[offset];
            offset++;
            if (blockSize == 0)
            {
                buffer = stream.ToArray();
                return true;
            }

            if (offset + blockSize > data.Length)
                return false;

            stream.Write(data.Slice(offset, blockSize));
            offset += blockSize;
        }

        return false;
    }

    private static bool TrySkipSubBlocks(ReadOnlySpan<byte> data, ref int offset)
    {
        while (offset < data.Length)
        {
            var blockSize = data[offset];
            offset++;
            if (blockSize == 0)
                return true;

            if (offset + blockSize > data.Length)
                return false;

            offset += blockSize;
        }

        return false;
    }

    private static bool TryReadUInt16(ReadOnlySpan<byte> data, int offset, out int value)
    {
        value = 0;
        if (offset + 2 > data.Length)
            return false;

        value = data[offset] | (data[offset + 1] << 8);
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

    private static bool HasGlobalColorTable(byte packedFields) => (packedFields & 0b1000_0000) != 0;
    private static bool HasLocalColorTable(byte packedFields) => (packedFields & 0b1000_0000) != 0;

    private ref struct GifBitReader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private int _offset;
        private int _bitsInBuffer;
        private uint _bitBuffer;

        public bool TryRead(int bitCount, out int value)
        {
            while (_bitsInBuffer < bitCount)
            {
                if (_offset >= _data.Length)
                {
                    value = 0;
                    return false;
                }

                _bitBuffer |= (uint)(_data[_offset] << _bitsInBuffer);
                _bitsInBuffer += 8;
                _offset++;
            }

            value = (int)(_bitBuffer & (uint)((1 << bitCount) - 1));
            _bitBuffer >>= bitCount;
            _bitsInBuffer -= bitCount;
            return true;
        }
    }
}
