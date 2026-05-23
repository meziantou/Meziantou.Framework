using System.Buffers.Binary;

namespace Meziantou.Framework.SnapshotTesting;

internal static class TiffImageLoader
{
    private const ushort ByteOrderLittleEndian = 0x4949;
    private const ushort ByteOrderBigEndian = 0x4D4D;
    private const ushort ClassicTiffMagic = 42;
    private const ushort BigTiffMagic = 43;

    private const ushort TagImageWidth = 256;
    private const ushort TagImageLength = 257;
    private const ushort TagBitsPerSample = 258;
    private const ushort TagCompression = 259;
    private const ushort TagPhotometricInterpretation = 262;
    private const ushort TagStripOffsets = 273;
    private const ushort TagOrientation = 274;
    private const ushort TagSamplesPerPixel = 277;
    private const ushort TagRowsPerStrip = 278;
    private const ushort TagStripByteCounts = 279;
    private const ushort TagPlanarConfiguration = 284;
    private const ushort TagPredictor = 317;

    private const ushort CompressionNone = 1;
    private const ushort CompressionLzw = 5;
    private const ushort CompressionPackBits = 32773;

    private const ushort PhotometricWhiteIsZero = 0;
    private const ushort PhotometricBlackIsZero = 1;
    private const ushort PhotometricRgb = 2;

    private const ushort OrientationTopLeft = 1;
    private const ushort PlanarConfigurationChunky = 1;
    private const ushort PredictorNone = 1;
    private const ushort PredictorHorizontalDifferencing = 2;

    internal static bool IsTiff(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4)
            return false;

        return data[0] == (byte)'I' && data[1] == (byte)'I' && data[3] == 0 && data[2] is (byte)ClassicTiffMagic or (byte)BigTiffMagic ||
               data[0] == (byte)'M' && data[1] == (byte)'M' && data[2] == 0 && data[3] is (byte)ClassicTiffMagic or (byte)BigTiffMagic;
    }

    internal static Image Load(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8)
            throw new InvalidDataException("The TIFF data is too small.");

        var byteOrderMark = BinaryPrimitives.ReadUInt16BigEndian(data);
        var isLittleEndian = byteOrderMark switch
        {
            ByteOrderLittleEndian => true,
            ByteOrderBigEndian => false,
            _ => throw new InvalidDataException("The TIFF byte order is invalid."),
        };

        var version = ReadUInt16(data, 2, isLittleEndian);
        if (version == BigTiffMagic)
            throw new NotSupportedException("BigTIFF is not supported.");

        if (version != ClassicTiffMagic)
            throw new InvalidDataException("The TIFF header is invalid.");

        var firstIfdOffset = checked((int)ReadUInt32(data, 4, isLittleEndian));
        if (firstIfdOffset <= 0)
            throw new InvalidDataException("The TIFF image file directory offset is invalid.");

        var entries = ReadImageFileDirectory(data, firstIfdOffset, isLittleEndian);

        var width = checked((int)ReadRequiredSingleValue(entries, data, TagImageWidth, isLittleEndian));
        var height = checked((int)ReadRequiredSingleValue(entries, data, TagImageLength, isLittleEndian));
        if (width <= 0 || height <= 0)
            throw new NotSupportedException("Unsupported TIFF dimensions.");

        var photometric = checked((ushort)ReadRequiredSingleValue(entries, data, TagPhotometricInterpretation, isLittleEndian));
        var compression = checked((ushort)ReadOptionalSingleValue(entries, data, TagCompression, isLittleEndian, CompressionNone));
        var orientation = checked((ushort)ReadOptionalSingleValue(entries, data, TagOrientation, isLittleEndian, OrientationTopLeft));
        if (orientation != OrientationTopLeft)
            throw new NotSupportedException("Only TIFF orientation TopLeft is supported.");

        var planarConfiguration = checked((ushort)ReadOptionalSingleValue(entries, data, TagPlanarConfiguration, isLittleEndian, PlanarConfigurationChunky));
        if (planarConfiguration != PlanarConfigurationChunky)
            throw new NotSupportedException("Only chunky TIFF planar configuration is supported.");

        var samplesPerPixel = checked((int)ReadOptionalSingleValue(entries, data, TagSamplesPerPixel, isLittleEndian, defaultValue: 1));
        if (samplesPerPixel <= 0)
            throw new NotSupportedException("Unsupported TIFF samples per pixel.");

        var predictor = checked((ushort)ReadOptionalSingleValue(entries, data, TagPredictor, isLittleEndian, PredictorNone));
        if (predictor is not PredictorNone and not PredictorHorizontalDifferencing)
            throw new NotSupportedException("Unsupported TIFF predictor.");

        var bitsPerSampleValues = ReadBitsPerSample(entries, data, isLittleEndian, samplesPerPixel);
        foreach (var bitsPerSample in bitsPerSampleValues)
        {
            if (bitsPerSample != 8)
                throw new NotSupportedException("Only 8-bit TIFF samples are supported.");
        }

        var strips = ReadStripData(entries, data, isLittleEndian);
        var rowsPerStrip = checked((int)ReadOptionalSingleValue(entries, data, TagRowsPerStrip, isLittleEndian, (uint)height));
        if (rowsPerStrip <= 0)
            throw new NotSupportedException("Unsupported TIFF rows per strip.");

        var bytesPerRow = checked(width * samplesPerPixel);
        var pixels = new Argb[checked(width * height)];
        var rowIndex = 0;
        for (var stripIndex = 0; stripIndex < strips.Length && rowIndex < height; stripIndex++)
        {
            var remainingRows = height - rowIndex;
            var rowsInStrip = Math.Min(rowsPerStrip, remainingRows);
            var expectedByteCount = checked(rowsInStrip * bytesPerRow);
            var stripBytes = ExtractStrip(data, strips[stripIndex], expectedByteCount, compression);

            if (predictor == PredictorHorizontalDifferencing)
                ApplyHorizontalPredictor(stripBytes, bytesPerRow, samplesPerPixel);

            DecodeStripPixels(
                stripBytes,
                pixels,
                rowIndex,
                rowsInStrip,
                width,
                samplesPerPixel,
                photometric);

            rowIndex += rowsInStrip;
        }

        if (rowIndex != height)
            throw new InvalidDataException("The TIFF data is truncated.");

        return Image.Create(width, height, pixels);
    }

    private static ushort[] ReadBitsPerSample(
        Dictionary<ushort, (ushort Type, uint Count, uint ValueOffset)> entries,
        ReadOnlySpan<byte> data,
        bool isLittleEndian,
        int samplesPerPixel)
    {
        if (!entries.TryGetValue(TagBitsPerSample, out var bitsPerSampleEntry))
        {
            if (samplesPerPixel == 1)
                return [8];

            throw new InvalidDataException("Missing TIFF BitsPerSample tag.");
        }

        var values = ReadEntryValues(bitsPerSampleEntry, data, isLittleEndian);
        if (values.Length != samplesPerPixel)
            throw new InvalidDataException("Invalid TIFF BitsPerSample tag.");

        var bitsPerSample = new ushort[values.Length];
        for (var i = 0; i < values.Length; i++)
        {
            bitsPerSample[i] = checked((ushort)values[i]);
        }

        return bitsPerSample;
    }

    private static (int Offset, int ByteCount)[] ReadStripData(
        Dictionary<ushort, (ushort Type, uint Count, uint ValueOffset)> entries,
        ReadOnlySpan<byte> data,
        bool isLittleEndian)
    {
        if (!entries.TryGetValue(TagStripOffsets, out var stripOffsetsEntry))
            throw new InvalidDataException("Missing TIFF StripOffsets tag.");

        if (!entries.TryGetValue(TagStripByteCounts, out var stripByteCountsEntry))
            throw new InvalidDataException("Missing TIFF StripByteCounts tag.");

        var stripOffsets = ReadEntryValues(stripOffsetsEntry, data, isLittleEndian);
        var stripByteCounts = ReadEntryValues(stripByteCountsEntry, data, isLittleEndian);
        if (stripOffsets.Length == 0)
            throw new InvalidDataException("The TIFF does not contain strip data.");

        if (stripByteCounts.Length != stripOffsets.Length && stripByteCounts.Length != 1)
            throw new InvalidDataException("Invalid TIFF strip byte count data.");

        var strips = new (int Offset, int ByteCount)[stripOffsets.Length];
        for (var i = 0; i < stripOffsets.Length; i++)
        {
            var offset = checked((int)stripOffsets[i]);
            var byteCount = checked((int)(stripByteCounts.Length == 1 ? stripByteCounts[0] : stripByteCounts[i]));
            if (offset < 0 || byteCount <= 0)
                throw new InvalidDataException("Invalid TIFF strip metadata.");

            strips[i] = (offset, byteCount);
        }

        return strips;
    }

    private static byte[] ExtractStrip(
        ReadOnlySpan<byte> data,
        (int Offset, int ByteCount) strip,
        int expectedByteCount,
        ushort compression)
    {
        if (strip.Offset > data.Length - strip.ByteCount)
            throw new InvalidDataException("The TIFF strip data is truncated.");

        var stripData = data.Slice(strip.Offset, strip.ByteCount);
        return compression switch
        {
            CompressionNone => DecodeUncompressedStrip(stripData, expectedByteCount),
            CompressionPackBits => DecodePackBits(stripData, expectedByteCount),
            CompressionLzw => DecodeLzw(stripData, expectedByteCount),
            _ => throw new NotSupportedException("Unsupported TIFF compression."),
        };
    }

    private static byte[] DecodeUncompressedStrip(ReadOnlySpan<byte> data, int expectedByteCount)
    {
        if (data.Length < expectedByteCount)
            throw new InvalidDataException("The TIFF strip data is truncated.");

        return data[..expectedByteCount].ToArray();
    }

    private static byte[] DecodePackBits(ReadOnlySpan<byte> data, int expectedByteCount)
    {
        var output = new byte[expectedByteCount];
        var readOffset = 0;
        var writeOffset = 0;
        while (readOffset < data.Length && writeOffset < output.Length)
        {
            var control = unchecked((sbyte)data[readOffset]);
            readOffset++;
            switch (control)
            {
                case >= 0:
                    {
                        var count = control + 1;
                        if (readOffset > data.Length - count || writeOffset > output.Length - count)
                            throw new InvalidDataException("Invalid TIFF PackBits data.");

                        data.Slice(readOffset, count).CopyTo(output.AsSpan(writeOffset, count));
                        readOffset += count;
                        writeOffset += count;
                        break;
                    }

                case -128:
                    break;

                default:
                    {
                        if (readOffset >= data.Length)
                            throw new InvalidDataException("Invalid TIFF PackBits data.");

                        var value = data[readOffset];
                        readOffset++;

                        var count = 1 - control;
                        if (writeOffset > output.Length - count)
                            throw new InvalidDataException("Invalid TIFF PackBits data.");

                        output.AsSpan(writeOffset, count).Fill(value);
                        writeOffset += count;
                        break;
                    }
            }
        }

        if (writeOffset != output.Length)
            throw new InvalidDataException("The TIFF PackBits data is truncated.");

        return output;
    }

    private static byte[] DecodeLzw(ReadOnlySpan<byte> data, int expectedByteCount)
    {
        const int ClearCode = 256;
        const int EndOfInformationCode = 257;
        const int DictionarySize = 4096;

        Span<short> prefixes = stackalloc short[DictionarySize];
        Span<byte> suffixes = stackalloc byte[DictionarySize];
        Span<byte> stack = stackalloc byte[DictionarySize];

        ResetDictionary(prefixes, suffixes, out var nextCode, out var codeBitCount);

        var output = new byte[expectedByteCount];
        var outputOffset = 0;

        var previousCode = -1;
        var reader = new TiffLzwBitReader(data);
        while (reader.TryRead(codeBitCount, out var code))
        {
            if (code == ClearCode)
            {
                ResetDictionary(prefixes, suffixes, out nextCode, out codeBitCount);
                previousCode = -1;
                continue;
            }

            if (code == EndOfInformationCode)
                break;

            if (code < 0 || code >= DictionarySize || code > nextCode)
                throw new InvalidDataException("Invalid TIFF LZW data.");

            var currentCode = code;
            var stackLength = 0;
            byte firstValue;
            if (code == nextCode)
            {
                if (previousCode < 0 || !TryExpandCode(previousCode, prefixes, suffixes, stack, ref stackLength, out firstValue))
                    throw new InvalidDataException("Invalid TIFF LZW data.");

                if (stackLength >= stack.Length)
                    throw new InvalidDataException("Invalid TIFF LZW data.");

                stack[stackLength] = firstValue;
                stackLength++;
            }
            else if (!TryExpandCode(code, prefixes, suffixes, stack, ref stackLength, out firstValue))
            {
                throw new InvalidDataException("Invalid TIFF LZW data.");
            }

            for (var i = stackLength - 1; i >= 0; i--)
            {
                if (outputOffset >= output.Length)
                    throw new InvalidDataException("Invalid TIFF LZW data.");

                output[outputOffset] = stack[i];
                outputOffset++;
            }

            if (previousCode >= 0 && nextCode < DictionarySize)
            {
                prefixes[nextCode] = checked((short)previousCode);
                suffixes[nextCode] = firstValue;
                nextCode++;

                if (nextCode == (1 << codeBitCount) && codeBitCount < 12)
                    codeBitCount++;
            }

            previousCode = currentCode;
            if (outputOffset == output.Length)
                break;
        }

        if (outputOffset != output.Length)
            throw new InvalidDataException("The TIFF LZW data is truncated.");

        return output;
    }

    private static void ResetDictionary(Span<short> prefixes, Span<byte> suffixes, out int nextCode, out int codeBitCount)
    {
        for (var i = 0; i < 256; i++)
        {
            prefixes[i] = -1;
            suffixes[i] = (byte)i;
        }

        nextCode = 258;
        codeBitCount = 9;
    }

    private static bool TryExpandCode(
        int code,
        ReadOnlySpan<short> prefixes,
        ReadOnlySpan<byte> suffixes,
        Span<byte> stack,
        ref int stackLength,
        out byte firstValue)
    {
        firstValue = 0;
        if (code < 0 || code >= prefixes.Length)
            return false;

        var current = code;
        while (current >= 256)
        {
            if (current >= prefixes.Length || stackLength >= stack.Length)
                return false;

            stack[stackLength] = suffixes[current];
            stackLength++;

            current = prefixes[current];
            if (current < 0)
                return false;
        }

        firstValue = checked((byte)current);
        if (stackLength >= stack.Length)
            return false;

        stack[stackLength] = firstValue;
        stackLength++;
        return true;
    }

    private static void ApplyHorizontalPredictor(Span<byte> data, int bytesPerRow, int samplesPerPixel)
    {
        for (var row = 0; row < data.Length; row += bytesPerRow)
        {
            var rowSpan = data.Slice(row, bytesPerRow);
            for (var offset = samplesPerPixel; offset < rowSpan.Length; offset++)
            {
                rowSpan[offset] = (byte)(rowSpan[offset] + rowSpan[offset - samplesPerPixel]);
            }
        }
    }

    private static void DecodeStripPixels(
        ReadOnlySpan<byte> stripBytes,
        Span<Argb> pixels,
        int startRow,
        int rowCount,
        int width,
        int samplesPerPixel,
        ushort photometricInterpretation)
    {
        var sourceOffset = 0;
        for (var rowIndex = 0; rowIndex < rowCount; rowIndex++)
        {
            var destinationOffset = checked((startRow + rowIndex) * width);
            switch (photometricInterpretation)
            {
                case PhotometricRgb:
                    if (samplesPerPixel is not (3 or 4))
                        throw new NotSupportedException("Unsupported TIFF RGB sample count.");

                    for (var x = 0; x < width; x++)
                    {
                        var r = stripBytes[sourceOffset];
                        var g = stripBytes[sourceOffset + 1];
                        var b = stripBytes[sourceOffset + 2];
                        var a = samplesPerPixel == 4 ? stripBytes[sourceOffset + 3] : (byte)0xFF;
                        pixels[destinationOffset + x] = new Argb(a, r, g, b);
                        sourceOffset += samplesPerPixel;
                    }

                    break;

                case PhotometricBlackIsZero:
                case PhotometricWhiteIsZero:
                    if (samplesPerPixel != 1)
                        throw new NotSupportedException("Unsupported TIFF grayscale sample count.");

                    for (var x = 0; x < width; x++)
                    {
                        var value = stripBytes[sourceOffset];
                        sourceOffset++;
                        if (photometricInterpretation == PhotometricWhiteIsZero)
                            value = (byte)(255 - value);

                        pixels[destinationOffset + x] = new Argb(0xFF, value, value, value);
                    }

                    break;

                default:
                    throw new NotSupportedException("Unsupported TIFF photometric interpretation.");
            }
        }
    }

    private static Dictionary<ushort, (ushort Type, uint Count, uint ValueOffset)> ReadImageFileDirectory(ReadOnlySpan<byte> data, int offset, bool isLittleEndian)
    {
        if (offset > data.Length - 2)
            throw new InvalidDataException("The TIFF image file directory is truncated.");

        var entryCount = ReadUInt16(data, offset, isLittleEndian);
        var entriesOffset = checked(offset + 2);
        var directoryLength = checked(entryCount * 12 + 4);
        if (entriesOffset > data.Length - directoryLength)
            throw new InvalidDataException("The TIFF image file directory is truncated.");

        var entries = new Dictionary<ushort, (ushort Type, uint Count, uint ValueOffset)>(entryCount);
        for (var i = 0; i < entryCount; i++)
        {
            var entryOffset = entriesOffset + i * 12;
            var tag = ReadUInt16(data, entryOffset, isLittleEndian);
            var type = ReadUInt16(data, entryOffset + 2, isLittleEndian);
            var count = ReadUInt32(data, entryOffset + 4, isLittleEndian);
            var valueOffset = ReadUInt32(data, entryOffset + 8, isLittleEndian);
            entries[tag] = (type, count, valueOffset);
        }

        return entries;
    }

    private static uint ReadRequiredSingleValue(
        Dictionary<ushort, (ushort Type, uint Count, uint ValueOffset)> entries,
        ReadOnlySpan<byte> data,
        ushort tag,
        bool isLittleEndian)
    {
        if (!entries.TryGetValue(tag, out var entry))
            throw new InvalidDataException($"Missing TIFF tag {tag}.");

        var values = ReadEntryValues(entry, data, isLittleEndian);
        if (values.Length != 1)
            throw new InvalidDataException($"Invalid TIFF tag {tag}.");

        return values[0];
    }

    private static uint ReadOptionalSingleValue(
        Dictionary<ushort, (ushort Type, uint Count, uint ValueOffset)> entries,
        ReadOnlySpan<byte> data,
        ushort tag,
        bool isLittleEndian,
        uint defaultValue)
    {
        if (!entries.TryGetValue(tag, out var entry))
            return defaultValue;

        var values = ReadEntryValues(entry, data, isLittleEndian);
        if (values.Length != 1)
            throw new InvalidDataException($"Invalid TIFF tag {tag}.");

        return values[0];
    }

    private static uint[] ReadEntryValues((ushort Type, uint Count, uint ValueOffset) entry, ReadOnlySpan<byte> data, bool isLittleEndian)
    {
        if (entry.Count == 0)
            throw new InvalidDataException("Invalid TIFF tag count.");

        var typeSize = entry.Type switch
        {
            1 => 1,
            3 => 2,
            4 => 4,
            _ => throw new NotSupportedException("Unsupported TIFF field type."),
        };

        var valueCount = checked((int)entry.Count);
        var byteCount = checked(valueCount * typeSize);

        ReadOnlySpan<byte> source;
        var inlineBuffer = new byte[4];
        if (byteCount <= 4)
        {
            if (isLittleEndian)
                BinaryPrimitives.WriteUInt32LittleEndian(inlineBuffer, entry.ValueOffset);
            else
                BinaryPrimitives.WriteUInt32BigEndian(inlineBuffer, entry.ValueOffset);

            source = inlineBuffer.AsSpan(0, byteCount);
        }
        else
        {
            var offset = checked((int)entry.ValueOffset);
            if (offset < 0 || offset > data.Length - byteCount)
                throw new InvalidDataException("Invalid TIFF tag offset.");

            source = data.Slice(offset, byteCount);
        }

        var values = new uint[valueCount];
        for (var i = 0; i < valueCount; i++)
        {
            var valueOffset = i * typeSize;
            values[i] = entry.Type switch
            {
                1 => source[valueOffset],
                3 => isLittleEndian
                    ? BinaryPrimitives.ReadUInt16LittleEndian(source.Slice(valueOffset, 2))
                    : BinaryPrimitives.ReadUInt16BigEndian(source.Slice(valueOffset, 2)),
                4 => isLittleEndian
                    ? BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(valueOffset, 4))
                    : BinaryPrimitives.ReadUInt32BigEndian(source.Slice(valueOffset, 4)),
                _ => throw new NotSupportedException("Unsupported TIFF field type."),
            };
        }

        return values;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset, bool isLittleEndian)
    {
        if (offset > data.Length - 2)
            throw new InvalidDataException("The TIFF data is truncated.");

        return isLittleEndian
            ? BinaryPrimitives.ReadUInt16LittleEndian(data.Slice(offset, 2))
            : BinaryPrimitives.ReadUInt16BigEndian(data.Slice(offset, 2));
    }

    private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset, bool isLittleEndian)
    {
        if (offset > data.Length - 4)
            throw new InvalidDataException("The TIFF data is truncated.");

        return isLittleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(data.Slice(offset, 4))
            : BinaryPrimitives.ReadUInt32BigEndian(data.Slice(offset, 4));
    }

    private ref struct TiffLzwBitReader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> _data = data;
        private int _bitOffset;

        public bool TryRead(int bitCount, out int value)
        {
            if (_bitOffset > _data.Length * 8 - bitCount)
            {
                value = 0;
                return false;
            }

            value = 0;
            for (var i = 0; i < bitCount; i++)
            {
                var absoluteBitIndex = _bitOffset + i;
                var byteIndex = absoluteBitIndex / 8;
                var bitIndex = 7 - (absoluteBitIndex & 7);
                var bitValue = (_data[byteIndex] >> bitIndex) & 1;
                value = (value << 1) | bitValue;
            }

            _bitOffset += bitCount;
            return true;
        }
    }
}
