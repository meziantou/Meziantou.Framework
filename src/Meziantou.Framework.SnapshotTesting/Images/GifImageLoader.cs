namespace Meziantou.Framework.SnapshotTesting;

internal static class GifImageLoader
{
    /// <summary>
    /// Decodes every frame of a GIF. Frames after the first usually contain only the pixels that changed, so
    /// each frame is composited over the canvas left by the previous one, honoring the disposal method of the
    /// graphic control extension.
    /// </summary>
    /// <remarks>
    /// The decoder is as lenient as browsers and Pillow with files that are slightly off the specification, so
    /// such a GIF still gets PNG frames instead of an opaque binary snapshot: data after the last frame (with
    /// or without a trailer) is ignored, a palette index beyond the color table is black, and the pixels a
    /// frame's LZW data does not reach keep the content of the canvas.
    /// </remarks>
    internal static bool TryExtractFrames(ReadOnlySpan<byte> data, [NotNullWhen(true)] out List<Image>? frames)
    {
        frames = null;
        if (!IsGifHeader(data))
            return false;

        if (!TryReadUInt16(data, 6, out var logicalWidth) ||
            !TryReadUInt16(data, 8, out var logicalHeight) ||
            !ImageLimits.IsValidSize(logicalWidth, logicalHeight))
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

        var backgroundColor = new Argb(0x00000000u);
        if (globalColorTable is not null && backgroundColorIndex < globalColorTable.Length)
        {
            backgroundColor = globalColorTable[backgroundColorIndex];
        }

        var canvas = new Argb[logicalWidth * logicalHeight];
        var canvasInitialized = false;
        var extractedFrames = new List<Image>();
        var transparentColorIndex = -1;
        var disposalMethod = DisposalMethod.None;

        while (offset < data.Length)
        {
            var blockType = data[offset];
            offset++;

            switch (blockType)
            {
                case 0x21: // Extension block
                    if (!TryReadExtensionBlock(data, ref offset, ref transparentColorIndex, ref disposalMethod))
                        return false;

                    break;

                case 0x2C: // Image descriptor
                    // A frame that declares a transparent color leaves the uncovered area transparent instead of
                    // painting the background color of the logical screen.
                    var frameBackground = transparentColorIndex >= 0 ? new Argb(0x00000000u) : backgroundColor;
                    if (!canvasInitialized)
                    {
                        Array.Fill(canvas, frameBackground);
                        canvasInitialized = true;
                    }

                    // Every frame is a full copy of the canvas, so a few bytes per frame could otherwise make a
                    // large logical screen allocate far more memory than any snapshot needs.
                    if ((extractedFrames.Count + 1L) * canvas.Length > ImageLimits.MaxPixelCount)
                        return false;

                    if (!TryReadImage(
                        data,
                        ref offset,
                        logicalWidth,
                        logicalHeight,
                        globalColorTable,
                        transparentColorIndex,
                        disposalMethod,
                        frameBackground,
                        canvas,
                        out var frame))
                    {
                        return false;
                    }

                    extractedFrames.Add(frame);
                    transparentColorIndex = -1;
                    disposalMethod = DisposalMethod.None;
                    break;

                default:
                    // The trailer (0x3B) ends the file. Like browsers, anything else that is not a block is
                    // extraneous data after the last frame and is handled as if the file had been terminated.
                    return TryGetFrames(extractedFrames, out frames);
            }
        }

        // The file ends without a trailer
        return TryGetFrames(extractedFrames, out frames);

        static bool TryGetFrames(List<Image> extractedFrames, [NotNullWhen(true)] out List<Image>? frames)
        {
            frames = extractedFrames.Count > 0 ? extractedFrames : null;
            return frames is not null;
        }
    }

    private static bool TryReadImage(
        ReadOnlySpan<byte> data,
        ref int offset,
        int logicalWidth,
        int logicalHeight,
        Argb[]? globalColorTable,
        int transparentColorIndex,
        DisposalMethod disposalMethod,
        Argb frameBackground,
        Argb[] canvas,
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

        if (!ImageLimits.IsValidSize(imageWidth, imageHeight))
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

        var previousCanvas = disposalMethod is DisposalMethod.RestoreToPrevious ? (Argb[])canvas.Clone() : null;

        var interlaced = (packedFields & 0b0100_0000) != 0;
        var raster = new FrameRaster(imageLeft, imageTop, imageWidth, imageHeight, interlaced, logicalWidth, logicalHeight, canvas, activeColorTable, transparentColorIndex);
        if (!TryDecodeLzw(compressedData, lzwMinimumCodeSize, in raster))
            return false;

        image = Image.Create(logicalWidth, logicalHeight, (Argb[])canvas.Clone());

        // The disposal method of a frame describes what the next frame is drawn over.
        switch (disposalMethod)
        {
            case DisposalMethod.RestoreToBackgroundColor:
                FillRectangle(canvas, logicalWidth, logicalHeight, imageLeft, imageTop, imageWidth, imageHeight, frameBackground);
                break;

            case DisposalMethod.RestoreToPrevious:
                if (previousCanvas is not null)
                {
                    Array.Copy(previousCanvas, canvas, canvas.Length);
                }

                break;
        }

        return true;
    }

    private static void FillRectangle(Argb[] canvas, int logicalWidth, int logicalHeight, int left, int top, int width, int height, Argb color)
    {
        // A frame can be far larger than the logical screen, so only the part of the rectangle on the canvas is visited
        var right = Math.Min(left + width, logicalWidth);
        var bottom = Math.Min(top + height, logicalHeight);
        for (var y = top; y < bottom; y++)
        {
            var rowOffset = y * logicalWidth;
            for (var x = left; x < right; x++)
            {
                canvas[rowOffset + x] = color;
            }
        }
    }

    /// <summary>
    /// Decodes the color indexes of a frame and draws them onto the canvas. Decoding stops at the end code, at
    /// the end of the data, at a code that is not in the table yet, or once no remaining pixel of the frame can
    /// land on the canvas; like browsers, the pixels the data does not reach keep what the canvas shows.
    /// </summary>
    /// <remarks>
    /// Nothing clips a frame to the logical screen, so a few bytes can describe a frame of hundreds of millions
    /// of pixels over a tiny canvas. The work is bounded by what can be drawn: the string of a code is only
    /// expanded when part of it is visible, and decoding ends after the last visible pixel.
    /// </remarks>
    private static bool TryDecodeLzw(ReadOnlySpan<byte> compressedData, byte minimumCodeSize, in FrameRaster raster)
    {
        const int MaxCodeCount = 4096;
        if (minimumCodeSize is < 2 or > 8)
            return false;

        var clearCode = 1 << minimumCodeSize;
        var endCode = clearCode + 1;
        var availableCode = endCode + 1;
        var codeSize = minimumCodeSize + 1;

        // Each string is its prefix string followed by one pixel. Its length and first pixel are kept too, so
        // a string that is not drawn never has to be expanded.
        Span<short> prefixes = stackalloc short[MaxCodeCount];
        Span<byte> suffixes = stackalloc byte[MaxCodeCount];
        Span<byte> firstPixels = stackalloc byte[MaxCodeCount];
        Span<short> lengths = stackalloc short[MaxCodeCount];
        for (var i = 0; i < clearCode; i++)
        {
            prefixes[i] = -1;
            suffixes[i] = (byte)i;
            firstPixels[i] = (byte)i;
            lengths[i] = 1;
        }

        var pixelLimit = raster.VisiblePixelEnd;
        var outputOffset = 0;
        var bitReader = new GifBitReader(compressedData);
        var previousCode = -1;
        while (outputOffset < pixelLimit && bitReader.TryRead(codeSize, out var code))
        {
            if (code == clearCode)
            {
                availableCode = endCode + 1;
                codeSize = minimumCodeSize + 1;
                previousCode = -1;
                continue;
            }

            if (code == endCode || code > availableCode || code >= MaxCodeCount)
                break;

            int stringLength;
            byte firstPixel;
            if (code == availableCode)
            {
                // The code is not in the table yet: it stands for the previous string followed by that
                // string's own first pixel.
                if (previousCode < 0)
                    break;

                stringLength = lengths[previousCode] + 1;
                firstPixel = firstPixels[previousCode];
            }
            else
            {
                stringLength = lengths[code];
                firstPixel = firstPixels[code];
            }

            if (raster.IsAnyPixelVisible(outputOffset, Math.Min(stringLength, pixelLimit - outputOffset)))
            {
                // Following the prefixes yields the pixels from the last one to the first
                var position = outputOffset + stringLength - 1;
                var current = code;
                if (code == availableCode)
                {
                    if (position < pixelLimit)
                    {
                        raster.Draw(position, firstPixel);
                    }

                    position--;
                    current = previousCode;
                }

                for (; position >= outputOffset; position--)
                {
                    if (position < pixelLimit)
                    {
                        raster.Draw(position, suffixes[current]);
                    }

                    current = prefixes[current];
                }
            }

            outputOffset += stringLength;

            if (previousCode >= 0 && availableCode < MaxCodeCount)
            {
                prefixes[availableCode] = (short)previousCode;
                suffixes[availableCode] = firstPixel;
                firstPixels[availableCode] = firstPixels[previousCode];
                lengths[availableCode] = (short)(lengths[previousCode] + 1);
                availableCode++;
                if (availableCode == (1 << codeSize) && codeSize < 12)
                {
                    codeSize++;
                }
            }

            previousCode = code;
        }

        return true;
    }

    private static bool TryReadExtensionBlock(ReadOnlySpan<byte> data, ref int offset, ref int transparentColorIndex, ref DisposalMethod disposalMethod)
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
        disposalMethod = (DisposalMethod)((packedFields & 0b0001_1100) >> 2);
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

    private enum DisposalMethod
    {
        None = 0,
        DoNotDispose = 1,
        RestoreToBackgroundColor = 2,
        RestoreToPrevious = 3,
    }

    /// <summary>Maps the pixels of a frame, in the order the LZW data codes them, onto the canvas.</summary>
    private readonly struct FrameRaster
    {
        private static readonly (int Start, int Step)[] InterlacePasses = [(0, 8), (4, 8), (2, 4), (1, 2)];

        private readonly int _left;
        private readonly int _top;
        private readonly int _width;
        private readonly int _height;
        private readonly bool _interlaced;
        private readonly int _logicalWidth;
        private readonly Argb[] _canvas;
        private readonly Argb[] _colorTable;
        private readonly int _transparentColorIndex;

        // The frame starts at a non-negative offset, so the columns and rows on the canvas are always the first ones
        private readonly int _visibleColumnCount;
        private readonly int _visibleRowCount;

        public FrameRaster(int left, int top, int width, int height, bool interlaced, int logicalWidth, int logicalHeight, Argb[] canvas, Argb[] colorTable, int transparentColorIndex)
        {
            _left = left;
            _top = top;
            _width = width;
            _height = height;
            _interlaced = interlaced;
            _logicalWidth = logicalWidth;
            _canvas = canvas;
            _colorTable = colorTable;
            _transparentColorIndex = transparentColorIndex;
            _visibleColumnCount = Math.Clamp(logicalWidth - left, 0, width);
            _visibleRowCount = Math.Clamp(logicalHeight - top, 0, height);
            VisiblePixelEnd = _visibleColumnCount == 0 || _visibleRowCount == 0 ? 0 : (GetLastVisibleSourceRow() * width) + _visibleColumnCount;
        }

        /// <summary>Gets the index, in coding order, just past the last pixel that lands on the canvas.</summary>
        public int VisiblePixelEnd { get; }

        public bool IsAnyPixelVisible(int start, int length)
        {
            var startRow = start / _width;
            var endRow = (start + length - 1) / _width;
            for (var sourceRow = startRow; sourceRow <= endRow; sourceRow++)
            {
                // The visible columns are the first ones, so only the column the range starts at in the row matters
                var firstColumn = sourceRow == startRow ? start - (startRow * _width) : 0;
                if (firstColumn < _visibleColumnCount && GetImageRow(sourceRow) < _visibleRowCount)
                    return true;
            }

            return false;
        }

        public void Draw(int index, byte paletteIndex)
        {
            var sourceRow = index / _width;
            var x = index - (sourceRow * _width);
            if (x >= _visibleColumnCount || paletteIndex == _transparentColorIndex)
                return;

            var imageRow = GetImageRow(sourceRow);
            if (imageRow >= _visibleRowCount)
                return;

            // An index beyond the color table is black, as in Pillow: the table behaves as if it were padded
            // with zeros up to 256 entries.
            _canvas[((_top + imageRow) * _logicalWidth) + _left + x] = paletteIndex < _colorTable.Length ? _colorTable[paletteIndex] : new Argb(0xFF, 0, 0, 0);
        }

        private int GetImageRow(int sourceRow)
        {
            if (!_interlaced)
                return sourceRow;

            foreach (var (start, step) in InterlacePasses)
            {
                var rowCount = GetPassRowCount(_height, start, step);
                if (sourceRow < rowCount)
                    return start + (sourceRow * step);

                sourceRow -= rowCount;
            }

            return _height;
        }

        private int GetLastVisibleSourceRow()
        {
            if (!_interlaced)
                return _visibleRowCount - 1;

            // The passes are coded one after the other, so the last visible row is in the last pass that has one
            var lastVisibleSourceRow = 0;
            var passOffset = 0;
            foreach (var (start, step) in InterlacePasses)
            {
                var visibleRowsInPass = GetPassRowCount(_visibleRowCount, start, step);
                if (visibleRowsInPass > 0)
                {
                    lastVisibleSourceRow = passOffset + visibleRowsInPass - 1;
                }

                passOffset += GetPassRowCount(_height, start, step);
            }

            return lastVisibleSourceRow;
        }

        private static int GetPassRowCount(int rowCount, int start, int step)
        {
            return rowCount > start ? (rowCount - start + step - 1) / step : 0;
        }
    }

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
