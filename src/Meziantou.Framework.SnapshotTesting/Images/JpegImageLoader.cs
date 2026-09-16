using System.Buffers.Binary;

namespace Meziantou.Framework.SnapshotTesting;

/// <summary>
/// Decodes baseline and extended sequential (8-bit, Huffman) JPEG images. The output is meant to match
/// libjpeg-turbo's default decoding (<c>djpeg</c>, Pillow, browsers): accurate integer IDCT, "fancy" triangle
/// upsampling for 2:1 chroma and the fixed-point YCbCr to RGB conversion.
/// </summary>
internal static class JpegImageLoader
{
    private const byte MarkerPrefix = 0xFF;
    private const byte StartOfImageMarker = 0xD8;
    private const byte EndOfImageMarker = 0xD9;
    private const byte StartOfFrameBaselineMarker = 0xC0;
    private const byte StartOfFrameExtendedSequentialMarker = 0xC1;
    private const byte StartOfFrameProgressiveMarker = 0xC2;
    private const byte StartOfScanMarker = 0xDA;
    private const byte DefineHuffmanTableMarker = 0xC4;
    private const byte DefineQuantizationTableMarker = 0xDB;
    private const byte DefineRestartIntervalMarker = 0xDD;
    private const byte App0Marker = 0xE0;
    private const byte App1Marker = 0xE1;
    private const byte App2Marker = 0xE2;
    private const byte App14Marker = 0xEE;
    private const byte Restart0Marker = 0xD0;
    private const byte Restart7Marker = 0xD7;

    // The JPEG standard limits an interleaved MCU to 10 blocks.
    private const int MaxBlocksInMcu = 10;

    private static readonly byte[] ZigZagOrder =
    [
        0, 1, 8, 16, 9, 2, 3, 10,
        17, 24, 32, 25, 18, 11, 4, 5,
        12, 19, 26, 33, 40, 48, 41, 34,
        27, 20, 13, 6, 7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36,
        29, 22, 15, 23, 30, 37, 44, 51,
        58, 59, 52, 45, 38, 31, 39, 46,
        53, 60, 61, 54, 47, 55, 62, 63,
    ];

    // Constants of libjpeg's accurate integer IDCT (jidctint.c): FIX(x) = x * 2^13, rounded.
    private const int IdctConstBits = 13;
    private const int IdctPass1Bits = 2;
    private const int Fix0_298631336 = 2446;
    private const int Fix0_390180644 = 3196;
    private const int Fix0_541196100 = 4433;
    private const int Fix0_765366865 = 6270;
    private const int Fix0_899976223 = 7373;
    private const int Fix1_175875602 = 9633;
    private const int Fix1_501321110 = 12299;
    private const int Fix1_847759065 = 15137;
    private const int Fix1_961570560 = 16069;
    private const int Fix2_053119869 = 16819;
    private const int Fix2_562915447 = 20995;
    private const int Fix3_072711026 = 25172;

    // The IDCT output is masked to 10 bits and mapped through this table, exactly like libjpeg's
    // range-limit table: [0, 127] -> +128, [128, 511] -> 255, [512, 895] -> 0, [896, 1023] (negative) -> +128 - 1024.
    private static readonly byte[] IdctRangeLimit = CreateIdctRangeLimitTable();

    // Fixed-point YCbCr to RGB tables of libjpeg (jdcolor.c), 16 fractional bits.
    private const int ColorScaleBits = 16;
    private static readonly int[] CrToRed = CreateColorTable(1.40200, roundResult: true);
    private static readonly int[] CbToBlue = CreateColorTable(1.77200, roundResult: true);
    private static readonly int[] CrToGreen = CreateColorTable(-0.71414, roundResult: false, addHalf: false);
    private static readonly int[] CbToGreen = CreateColorTable(-0.34414, roundResult: false, addHalf: true);

    internal static bool IsJpeg(ReadOnlySpan<byte> data)
    {
        return data.Length >= 2 && data[0] == MarkerPrefix && data[1] == StartOfImageMarker;
    }

    internal static Image Load(ReadOnlySpan<byte> data)
    {
        if (!IsJpeg(data))
            throw new InvalidDataException("The JPEG signature is invalid.");

        var decoder = new Decoder(data.ToArray());
        return decoder.Decode();
    }

    private static byte[] CreateIdctRangeLimitTable()
    {
        var table = new byte[1024];
        for (var i = 0; i < table.Length; i++)
        {
            table[i] = i switch
            {
                < 128 => (byte)(i + 128),
                < 512 => 255,
                < 896 => 0,
                _ => (byte)(i - 896),
            };
        }

        return table;
    }

    private static int[] CreateColorTable(double factor, bool roundResult, bool addHalf = false)
    {
        var fixedFactor = (long)(factor * (1L << ColorScaleBits) + (factor < 0 ? -0.5 : 0.5));
        const long OneHalf = 1L << (ColorScaleBits - 1);
        var table = new int[256];
        for (var i = 0; i < table.Length; i++)
        {
            var x = i - 128;
            if (roundResult)
            {
                table[i] = (int)((fixedFactor * x + OneHalf) >> ColorScaleBits);
            }
            else
            {
                table[i] = (int)(fixedFactor * x + (addHalf ? OneHalf : 0));
            }
        }

        return table;
    }

    private sealed class Decoder(byte[] data)
    {
        private readonly byte[] _data = data;
        private readonly ushort[]?[] _quantizationTables = new ushort[4][];
        private readonly HuffmanTable?[][] _huffmanTables =
        [
            new HuffmanTable?[4],
            new HuffmanTable?[4],
        ];

        private readonly short[] _coefficients = new short[64];
        private readonly int[] _idctWorkspace = new int[64];
        private FrameComponent[] _frameComponents = [];
        private int _width;
        private int _height;
        private int _maxHorizontalSamplingFactor = 1;
        private int _maxVerticalSamplingFactor = 1;
        private int _mcuCountX;
        private int _mcuCountY;
        private int _restartInterval;
        private bool _sawJfifMarker;
        private byte? _adobeTransform;

        public Image Decode()
        {
            var position = 2;
            while (position < _data.Length)
            {
                var marker = ReadMarker(ref position);
                if (marker == EndOfImageMarker)
                    break;

                if (marker == StartOfImageMarker)
                    throw new InvalidDataException("Unexpected JPEG start marker.");

                if (marker is >= Restart0Marker and <= Restart7Marker)
                    throw new InvalidDataException("Unexpected JPEG restart marker outside scan data.");

                if (marker == StartOfScanMarker)
                {
                    var scanHeader = ReadSegment(ref position);
                    var scanComponents = ParseStartOfScan(scanHeader);
                    var nextMarkerOffset = FindEntropyDataEnd(position);
                    DecodeScan(position, nextMarkerOffset, scanComponents);
                    position = nextMarkerOffset;
                    continue;
                }

                if (marker == 0x01)
                    throw new NotSupportedException("Unsupported JPEG marker.");

                var segment = ReadSegment(ref position);

                switch (marker)
                {
                    case DefineQuantizationTableMarker:
                        ParseDefineQuantizationTable(segment);
                        break;
                    case DefineHuffmanTableMarker:
                        ParseDefineHuffmanTable(segment);
                        break;
                    case DefineRestartIntervalMarker:
                        ParseDefineRestartInterval(segment);
                        break;
                    case StartOfFrameBaselineMarker or StartOfFrameExtendedSequentialMarker:
                        ParseStartOfFrame(segment);
                        break;
                    case StartOfFrameProgressiveMarker:
                        throw new NotSupportedException("Progressive JPEG images are not supported.");
                    case App0Marker:
                        ParseApp0(segment);
                        break;
                    case App1Marker:
                        ParseApp1(segment);
                        break;
                    case App2Marker:
                        ParseApp2(segment);
                        break;
                    case App14Marker:
                        ParseApp14(segment);
                        break;
                    default:
                        if (IsStartOfFrameMarker(marker))
                            throw new NotSupportedException("Only baseline and extended sequential Huffman JPEG images are supported.");

                        break;
                }
            }

            // A missing EOI marker is accepted as long as every component was fully decoded: the entropy
            // decoder already reports scan data that ends before the last block.
            if (_frameComponents.Length == 0 || _frameComponents.All(component => component.Plane is null))
                throw new InvalidDataException("The JPEG image does not contain scan data.");

            if (_frameComponents.Any(component => component.Plane is null))
                throw new InvalidDataException("The JPEG image does not contain scan data for every component.");

            return _frameComponents.Length == 1 ? ComposeGrayscale() : ComposeYcbcr();
        }

        private void ParseApp0(ReadOnlySpan<byte> segment)
        {
            if (segment.Length >= 5 && segment[0] == (byte)'J' && segment[1] == (byte)'F' && segment[2] == (byte)'I' && segment[3] == (byte)'F' && segment[4] == 0)
            {
                _sawJfifMarker = true;
            }
        }

        private static void ParseApp1(ReadOnlySpan<byte> segment)
        {
            if (segment.Length < 6 || segment[0] != (byte)'E' || segment[1] != (byte)'x' || segment[2] != (byte)'i' || segment[3] != (byte)'f' || segment[4] != 0 || segment[5] != 0)
                return;

            var exifData = segment[6..];
            if (!TryReadExifOrientation(exifData, out var orientation))
                return;

            if (orientation != 1)
                throw new NotSupportedException("JPEG images with EXIF orientation are not supported.");
        }

        private static bool TryReadExifOrientation(ReadOnlySpan<byte> exifData, out ushort orientation)
        {
            orientation = 1;
            if (exifData.Length < 8)
                return false;

            var littleEndian = exifData[0] == (byte)'I' && exifData[1] == (byte)'I';
            var bigEndian = exifData[0] == (byte)'M' && exifData[1] == (byte)'M';
            if (!littleEndian && !bigEndian)
                return false;

            var magic = ReadUInt16(exifData, offset: 2, littleEndian);
            if (magic != 42)
                return false;

            var ifdOffset = ReadUInt32(exifData, offset: 4, littleEndian);
            if (ifdOffset > int.MaxValue)
                return false;

            var ifdPosition = checked((int)ifdOffset);
            if (ifdPosition + 2 > exifData.Length)
                return false;

            var entryCount = ReadUInt16(exifData, ifdPosition, littleEndian);
            ifdPosition += 2;

            for (var i = 0; i < entryCount; i++)
            {
                var entryOffset = ifdPosition + i * 12;
                if (entryOffset + 12 > exifData.Length)
                    return false;

                var tag = ReadUInt16(exifData, entryOffset, littleEndian);
                if (tag != 0x0112)
                    continue;

                var type = ReadUInt16(exifData, entryOffset + 2, littleEndian);
                var count = ReadUInt32(exifData, entryOffset + 4, littleEndian);
                if (type != 3 || count != 1)
                    return false;

                orientation = ReadUInt16(exifData, entryOffset + 8, littleEndian);
                return true;
            }

            return false;
        }

        private static void ParseApp2(ReadOnlySpan<byte> segment)
        {
            if (segment.Length >= 12 &&
                segment[0] == (byte)'I' &&
                segment[1] == (byte)'C' &&
                segment[2] == (byte)'C' &&
                segment[3] == (byte)'_' &&
                segment[4] == (byte)'P' &&
                segment[5] == (byte)'R' &&
                segment[6] == (byte)'O' &&
                segment[7] == (byte)'F' &&
                segment[8] == (byte)'I' &&
                segment[9] == (byte)'L' &&
                segment[10] == (byte)'E' &&
                segment[11] == 0)
            {
                throw new NotSupportedException("JPEG images with ICC profiles are not supported.");
            }
        }

        private void ParseApp14(ReadOnlySpan<byte> segment)
        {
            if (segment.Length < 12)
                return;

            if (segment[0] != (byte)'A' || segment[1] != (byte)'d' || segment[2] != (byte)'o' || segment[3] != (byte)'b' || segment[4] != (byte)'e')
                return;

            _adobeTransform = segment[11];
        }

        private void ParseDefineRestartInterval(ReadOnlySpan<byte> segment)
        {
            if (segment.Length != 2)
                throw new InvalidDataException("Invalid JPEG restart interval segment.");

            _restartInterval = BinaryPrimitives.ReadUInt16BigEndian(segment);
        }

        private void ParseDefineQuantizationTable(ReadOnlySpan<byte> segment)
        {
            var position = 0;
            while (position < segment.Length)
            {
                var info = segment[position++];
                var precision = info >> 4;
                var tableIndex = info & 0x0F;
                if (tableIndex >= _quantizationTables.Length)
                    throw new NotSupportedException("Unsupported JPEG quantization table index.");

                // Pq=1 (16-bit values) is legal in 8-bit images, and libjpeg writes it for low quality settings
                if (precision > 1)
                    throw new InvalidDataException("Invalid JPEG quantization table precision.");

                var valueSize = precision + 1;
                if (position + 64 * valueSize > segment.Length)
                    throw new InvalidDataException("The JPEG quantization table is truncated.");

                var table = new ushort[64];
                for (var i = 0; i < 64; i++)
                {
                    table[ZigZagOrder[i]] = precision == 0 ? segment[position] : BinaryPrimitives.ReadUInt16BigEndian(segment[position..]);
                    position += valueSize;
                }

                _quantizationTables[tableIndex] = table;
            }
        }

        private void ParseDefineHuffmanTable(ReadOnlySpan<byte> segment)
        {
            var position = 0;
            while (position < segment.Length)
            {
                var info = segment[position++];
                var tableClass = info >> 4;
                var tableIndex = info & 0x0F;
                if (tableClass >= _huffmanTables.Length || tableIndex >= _huffmanTables[tableClass].Length)
                    throw new NotSupportedException("Unsupported JPEG Huffman table index.");

                if (position + 16 > segment.Length)
                    throw new InvalidDataException("The JPEG Huffman table is truncated.");

                var lengths = segment.Slice(position, 16);
                position += 16;

                var symbolCount = 0;
                for (var i = 0; i < lengths.Length; i++)
                {
                    symbolCount += lengths[i];
                }

                if (symbolCount > 256)
                    throw new InvalidDataException("The JPEG Huffman table contains too many symbols.");

                if (position + symbolCount > segment.Length)
                    throw new InvalidDataException("The JPEG Huffman symbols are truncated.");

                var symbols = segment.Slice(position, symbolCount).ToArray();
                position += symbolCount;
                _huffmanTables[tableClass][tableIndex] = HuffmanTable.Create(lengths, symbols);
            }
        }

        private void ParseStartOfFrame(ReadOnlySpan<byte> segment)
        {
            if (_frameComponents.Length != 0)
                throw new InvalidDataException("The JPEG image contains more than one frame header.");

            if (segment.Length < 6)
                throw new InvalidDataException("The JPEG frame header is truncated.");

            var precision = segment[0];
            if (precision != 8)
                throw new NotSupportedException("Only 8-bit JPEG precision is supported.");

            _height = BinaryPrimitives.ReadUInt16BigEndian(segment[1..]);
            _width = BinaryPrimitives.ReadUInt16BigEndian(segment[3..]);
            var componentCount = segment[5];

            if (_width <= 0 || _height <= 0)
                throw new NotSupportedException("Unsupported JPEG dimensions.");

            if (!ImageLimits.IsValidSize(_width, _height))
                throw new InvalidDataException("The JPEG image dimensions exceed the supported limit.");

            if (componentCount is not 1 and not 3)
                throw new NotSupportedException("Only grayscale and YCbCr JPEG images are supported.");

            var expectedLength = checked(6 + componentCount * 3);
            if (segment.Length != expectedLength)
                throw new InvalidDataException("Invalid JPEG frame header length.");

            var components = new FrameComponent[componentCount];
            for (var i = 0; i < componentCount; i++)
            {
                var offset = 6 + i * 3;
                var id = segment[offset];
                var sampling = segment[offset + 1];
                var horizontalSamplingFactor = sampling >> 4;
                var verticalSamplingFactor = sampling & 0x0F;
                var quantizationTableIndex = segment[offset + 2];

                if (horizontalSamplingFactor is < 1 or > 4 || verticalSamplingFactor is < 1 or > 4)
                    throw new InvalidDataException("Invalid JPEG sampling factors.");

                if (quantizationTableIndex >= _quantizationTables.Length)
                    throw new NotSupportedException("Unsupported JPEG quantization table index.");

                for (var j = 0; j < i; j++)
                {
                    if (components[j].Id == id)
                        throw new InvalidDataException("Duplicate JPEG frame component identifier.");
                }

                // A single-component image is never subsampled: its only scan is non-interleaved, so the
                // declared factors have no effect on the decoded samples.
                if (componentCount == 1)
                {
                    horizontalSamplingFactor = 1;
                    verticalSamplingFactor = 1;
                }

                components[i] = new FrameComponent(id, horizontalSamplingFactor, verticalSamplingFactor, quantizationTableIndex);
            }

            _maxHorizontalSamplingFactor = components.Max(component => component.HorizontalSamplingFactor);
            _maxVerticalSamplingFactor = components.Max(component => component.VerticalSamplingFactor);
            _mcuCountX = DivideRoundUp(_width, 8 * _maxHorizontalSamplingFactor);
            _mcuCountY = DivideRoundUp(_height, 8 * _maxVerticalSamplingFactor);

            foreach (var component in components)
            {
                if (_maxHorizontalSamplingFactor % component.HorizontalSamplingFactor != 0 || _maxVerticalSamplingFactor % component.VerticalSamplingFactor != 0)
                    throw new NotSupportedException("Fractional JPEG sampling factors are not supported.");

                component.Width = DivideRoundUp(_width * component.HorizontalSamplingFactor, _maxHorizontalSamplingFactor);
                component.Height = DivideRoundUp(_height * component.VerticalSamplingFactor, _maxVerticalSamplingFactor);
                component.BlocksPerLine = DivideRoundUp(component.Width, 8);
                component.BlocksPerColumn = DivideRoundUp(component.Height, 8);
                component.Stride = checked(_mcuCountX * component.HorizontalSamplingFactor * 8);
                component.PlaneHeight = checked(_mcuCountY * component.VerticalSamplingFactor * 8);
            }

            _frameComponents = components;
        }

        private ScanComponent[] ParseStartOfScan(ReadOnlySpan<byte> segment)
        {
            if (_frameComponents.Length == 0)
                throw new InvalidDataException("The JPEG frame header is missing.");

            if (segment.Length < 2)
                throw new InvalidDataException("The JPEG scan header is truncated.");

            var componentCount = segment[0];
            if (componentCount == 0 || componentCount > _frameComponents.Length)
                throw new InvalidDataException("The JPEG scan component count is invalid.");

            var expectedLength = checked(1 + componentCount * 2 + 3);
            if (segment.Length != expectedLength)
                throw new InvalidDataException("Invalid JPEG scan header length.");

            var scanComponents = new ScanComponent[componentCount];
            var offset = 1;
            for (var i = 0; i < componentCount; i++)
            {
                var componentId = segment[offset++];
                var tableSelector = segment[offset++];
                var dcTableIndex = tableSelector >> 4;
                var acTableIndex = tableSelector & 0x0F;
                if (dcTableIndex >= 4 || acTableIndex >= 4)
                    throw new NotSupportedException("Unsupported JPEG Huffman table index.");

                var frameComponent = Array.Find(_frameComponents, component => component.Id == componentId) ?? throw new InvalidDataException("The JPEG scan references an unknown frame component.");
                for (var j = 0; j < i; j++)
                {
                    if (scanComponents[j].FrameComponent == frameComponent)
                        throw new InvalidDataException("The JPEG scan references a component more than once.");
                }

                // Sequential images code each component in exactly one scan; anything else is a
                // progressive-style refinement.
                if (frameComponent.Plane is not null)
                    throw new NotSupportedException("JPEG images with several scans for the same component are not supported.");

                var dcTable = _huffmanTables[0][dcTableIndex] ?? throw new InvalidDataException("The JPEG DC Huffman table is missing.");
                var acTable = _huffmanTables[1][acTableIndex] ?? throw new InvalidDataException("The JPEG AC Huffman table is missing.");
                var quantizationTable = _quantizationTables[frameComponent.QuantizationTableIndex] ?? throw new InvalidDataException("The JPEG quantization table is missing.");
                scanComponents[i] = new ScanComponent(frameComponent, dcTable, acTable, quantizationTable);
            }

            var spectralSelectionStart = segment[offset++];
            var spectralSelectionEnd = segment[offset++];
            var successiveApproximation = segment[offset];
            if (spectralSelectionStart != 0 || spectralSelectionEnd != 63 || successiveApproximation != 0)
                throw new NotSupportedException("Only sequential JPEG scans are supported.");

            ValidateColorModel();
            return scanComponents;
        }

        private void ValidateColorModel()
        {
            if (_frameComponents.Length == 1)
                return;

            if (_adobeTransform == 2)
                throw new NotSupportedException("CMYK/YCCK JPEG images are not supported.");

            if (_adobeTransform == 0)
                throw new NotSupportedException("JPEG images with Adobe transform 0 are not supported.");

            if (_sawJfifMarker || _adobeTransform == 1)
                return;

            // Without a JFIF or Adobe marker, libjpeg only treats the components as RGB when their identifiers
            // are 'R', 'G' and 'B'; any other identifiers, such as 0, 1 and 2, are assumed to be YCbCr.
            if (_frameComponents[0].Id == 'R' && _frameComponents[1].Id == 'G' && _frameComponents[2].Id == 'B')
                throw new NotSupportedException("Only YCbCr JPEG images are supported.");
        }

        private int FindEntropyDataEnd(int entropyStart)
        {
            var position = entropyStart;
            while (position + 1 < _data.Length)
            {
                if (_data[position] != MarkerPrefix)
                {
                    position++;
                    continue;
                }

                var markerOffset = position;
                position++;
                while (position < _data.Length && _data[position] == MarkerPrefix)
                {
                    position++;
                }

                if (position >= _data.Length)
                    return _data.Length;

                var marker = _data[position];
                if (marker == 0x00 || marker is >= Restart0Marker and <= Restart7Marker)
                {
                    position++;
                    continue;
                }

                return markerOffset;
            }

            // No marker follows the scan (the EOI marker is missing): the scan data runs to the end of the file.
            return _data.Length;
        }

        private void DecodeScan(int entropyStart, int entropyEnd, ScanComponent[] scanComponents)
        {
            // Non-interleaved scans (a single component) code one block per MCU over the component's own block
            // grid. Interleaved scans code H x V blocks of every component per MCU over the MCU grid of the frame.
            var interleaved = scanComponents.Length > 1;
            long blockCount;
            int mcuCountX;
            int mcuCountY;
            if (interleaved)
            {
                var blocksPerMcu = scanComponents.Sum(component => component.FrameComponent.HorizontalSamplingFactor * component.FrameComponent.VerticalSamplingFactor);
                if (blocksPerMcu > MaxBlocksInMcu)
                    throw new InvalidDataException("The JPEG scan has too many blocks per MCU.");

                mcuCountX = _mcuCountX;
                mcuCountY = _mcuCountY;
                blockCount = (long)mcuCountX * mcuCountY * blocksPerMcu;
            }
            else
            {
                mcuCountX = scanComponents[0].FrameComponent.BlocksPerLine;
                mcuCountY = scanComponents[0].FrameComponent.BlocksPerColumn;
                blockCount = (long)mcuCountX * mcuCountY;
            }

            // Every block codes at least a DC symbol and an end-of-block symbol, one bit each. Checking this
            // before allocating keeps a tiny file with a huge frame header from allocating the sample planes.
            if (blockCount * 2 > (long)(entropyEnd - entropyStart) * 8)
                throw new InvalidDataException("The JPEG scan data is truncated.");

            foreach (var scanComponent in scanComponents)
            {
                var component = scanComponent.FrameComponent;
                component.Plane = new byte[checked(component.Stride * component.PlaneHeight)];
                component.DcPredictor = 0;
            }

            var reader = new EntropyReader(_data, entropyStart, entropyEnd);
            var mcuSinceRestart = 0;
            var expectedRestartMarker = Restart0Marker;
            for (var mcuY = 0; mcuY < mcuCountY; mcuY++)
            {
                for (var mcuX = 0; mcuX < mcuCountX; mcuX++)
                {
                    if (_restartInterval > 0 && mcuSinceRestart == _restartInterval)
                    {
                        reader.ConsumeRestartMarker(expectedRestartMarker);
                        expectedRestartMarker = (byte)(expectedRestartMarker == Restart7Marker ? Restart0Marker : expectedRestartMarker + 1);
                        foreach (var scanComponent in scanComponents)
                        {
                            scanComponent.FrameComponent.DcPredictor = 0;
                        }

                        mcuSinceRestart = 0;
                    }

                    if (interleaved)
                    {
                        foreach (var scanComponent in scanComponents)
                        {
                            var component = scanComponent.FrameComponent;
                            for (var v = 0; v < component.VerticalSamplingFactor; v++)
                            {
                                for (var h = 0; h < component.HorizontalSamplingFactor; h++)
                                {
                                    DecodeBlock(ref reader, scanComponent, (mcuX * component.HorizontalSamplingFactor) + h, (mcuY * component.VerticalSamplingFactor) + v);
                                }
                            }
                        }
                    }
                    else
                    {
                        DecodeBlock(ref reader, scanComponents[0], mcuX, mcuY);
                    }

                    mcuSinceRestart++;
                }
            }
        }

        private void DecodeBlock(ref EntropyReader reader, ScanComponent scanComponent, int blockX, int blockY)
        {
            var coefficients = _coefficients;
            var component = scanComponent.FrameComponent;

            var dcCodeLength = reader.DecodeHuffman(scanComponent.DcTable);
            if (dcCodeLength > 16)
                throw new InvalidDataException("Invalid JPEG DC coefficient length.");

            component.DcPredictor += reader.ReceiveExtend(dcCodeLength);
            coefficients[0] = (short)component.DcPredictor;

            for (var zigZagIndex = 1; zigZagIndex < 64; zigZagIndex++)
            {
                var symbol = reader.DecodeHuffman(scanComponent.AcTable);
                var runLength = symbol >> 4;
                var codeLength = symbol & 0x0F;
                if (codeLength == 0)
                {
                    // End of block, or a run of 16 zeros
                    if (runLength != 0x0F)
                        break;

                    zigZagIndex += 15;
                    continue;
                }

                zigZagIndex += runLength;
                if (zigZagIndex >= 64)
                    throw new InvalidDataException("Invalid JPEG AC coefficient index.");

                coefficients[ZigZagOrder[zigZagIndex]] = (short)reader.ReceiveExtend(codeLength);
            }

            InverseDct(coefficients, scanComponent.QuantizationTable, _idctWorkspace, component.Plane.AsSpan((blockY * 8 * component.Stride) + (blockX * 8)), component.Stride);
            Array.Clear(coefficients);
        }

        /// <summary>libjpeg's accurate integer IDCT (jpeg_idct_islow), including its range limiting.</summary>
        private static void InverseDct(short[] coefficients, ushort[] quantizationTable, int[] workspace, Span<byte> output, int stride)
        {
            const int Pass1Shift = IdctConstBits - IdctPass1Bits;
            const int Pass1Round = 1 << (Pass1Shift - 1);
            const int Pass2Shift = IdctConstBits + IdctPass1Bits + 3;
            const int Pass2Round = 1 << (Pass2Shift - 1);
            const int RangeMask = 1023;

            // Pass 1: process the columns, storing into the work array
            for (var column = 0; column < 8; column++)
            {
                if (coefficients[8 + column] == 0 && coefficients[16 + column] == 0 && coefficients[24 + column] == 0 && coefficients[32 + column] == 0 &&
                    coefficients[40 + column] == 0 && coefficients[48 + column] == 0 && coefficients[56 + column] == 0)
                {
                    var dcValue = (coefficients[column] * quantizationTable[column]) << IdctPass1Bits;
                    for (var row = 0; row < 64; row += 8)
                    {
                        workspace[row + column] = dcValue;
                    }

                    continue;
                }

                var z2 = coefficients[16 + column] * quantizationTable[16 + column];
                var z3 = coefficients[48 + column] * quantizationTable[48 + column];
                var z1 = (z2 + z3) * Fix0_541196100;
                var tmp2 = z1 + (z2 * Fix0_765366865);
                var tmp3 = z1 - (z3 * Fix1_847759065);

                z2 = coefficients[column] * quantizationTable[column];
                z3 = coefficients[32 + column] * quantizationTable[32 + column];
                var tmp0 = (z2 + z3) << IdctConstBits;
                var tmp1 = (z2 - z3) << IdctConstBits;

                var tmp10 = tmp0 + tmp2;
                var tmp13 = tmp0 - tmp2;
                var tmp11 = tmp1 + tmp3;
                var tmp12 = tmp1 - tmp3;

                tmp0 = coefficients[56 + column] * quantizationTable[56 + column];
                tmp1 = coefficients[40 + column] * quantizationTable[40 + column];
                tmp2 = coefficients[24 + column] * quantizationTable[24 + column];
                tmp3 = coefficients[8 + column] * quantizationTable[8 + column];
                OddPart(ref tmp0, ref tmp1, ref tmp2, ref tmp3);

                workspace[column] = (tmp10 + tmp3 + Pass1Round) >> Pass1Shift;
                workspace[56 + column] = (tmp10 - tmp3 + Pass1Round) >> Pass1Shift;
                workspace[8 + column] = (tmp11 + tmp2 + Pass1Round) >> Pass1Shift;
                workspace[48 + column] = (tmp11 - tmp2 + Pass1Round) >> Pass1Shift;
                workspace[16 + column] = (tmp12 + tmp1 + Pass1Round) >> Pass1Shift;
                workspace[40 + column] = (tmp12 - tmp1 + Pass1Round) >> Pass1Shift;
                workspace[24 + column] = (tmp13 + tmp0 + Pass1Round) >> Pass1Shift;
                workspace[32 + column] = (tmp13 - tmp0 + Pass1Round) >> Pass1Shift;
            }

            // Pass 2: process the rows from the work array, storing into the output
            var rangeLimit = IdctRangeLimit;
            for (var row = 0; row < 8; row++)
            {
                var ws = workspace.AsSpan(row * 8, 8);
                var outputRow = output.Slice(row * stride, 8);
                if (ws[1] == 0 && ws[2] == 0 && ws[3] == 0 && ws[4] == 0 && ws[5] == 0 && ws[6] == 0 && ws[7] == 0)
                {
                    outputRow.Fill(rangeLimit[((ws[0] + (1 << (IdctPass1Bits + 2))) >> (IdctPass1Bits + 3)) & RangeMask]);
                    continue;
                }

                var z2 = ws[2];
                var z3 = ws[6];
                var z1 = (z2 + z3) * Fix0_541196100;
                var tmp2 = z1 + (z2 * Fix0_765366865);
                var tmp3 = z1 - (z3 * Fix1_847759065);

                var tmp0 = (ws[0] + ws[4]) << IdctConstBits;
                var tmp1 = (ws[0] - ws[4]) << IdctConstBits;

                var tmp10 = tmp0 + tmp2;
                var tmp13 = tmp0 - tmp2;
                var tmp11 = tmp1 + tmp3;
                var tmp12 = tmp1 - tmp3;

                tmp0 = ws[7];
                tmp1 = ws[5];
                tmp2 = ws[3];
                tmp3 = ws[1];
                OddPart(ref tmp0, ref tmp1, ref tmp2, ref tmp3);

                outputRow[0] = rangeLimit[((tmp10 + tmp3 + Pass2Round) >> Pass2Shift) & RangeMask];
                outputRow[7] = rangeLimit[((tmp10 - tmp3 + Pass2Round) >> Pass2Shift) & RangeMask];
                outputRow[1] = rangeLimit[((tmp11 + tmp2 + Pass2Round) >> Pass2Shift) & RangeMask];
                outputRow[6] = rangeLimit[((tmp11 - tmp2 + Pass2Round) >> Pass2Shift) & RangeMask];
                outputRow[2] = rangeLimit[((tmp12 + tmp1 + Pass2Round) >> Pass2Shift) & RangeMask];
                outputRow[5] = rangeLimit[((tmp12 - tmp1 + Pass2Round) >> Pass2Shift) & RangeMask];
                outputRow[3] = rangeLimit[((tmp13 + tmp0 + Pass2Round) >> Pass2Shift) & RangeMask];
                outputRow[4] = rangeLimit[((tmp13 - tmp0 + Pass2Round) >> Pass2Shift) & RangeMask];
            }
        }

        /// <summary>Odd part of the IDCT; on input tmp0..tmp3 are the coefficients 7, 5, 3 and 1.</summary>
        private static void OddPart(ref int tmp0, ref int tmp1, ref int tmp2, ref int tmp3)
        {
            var z1 = tmp0 + tmp3;
            var z2 = tmp1 + tmp2;
            var z3 = tmp0 + tmp2;
            var z4 = tmp1 + tmp3;
            var z5 = (z3 + z4) * Fix1_175875602;

            tmp0 *= Fix0_298631336;
            tmp1 *= Fix2_053119869;
            tmp2 *= Fix3_072711026;
            tmp3 *= Fix1_501321110;
            z1 *= -Fix0_899976223;
            z2 *= -Fix2_562915447;
            z3 = (z3 * -Fix1_961570560) + z5;
            z4 = (z4 * -Fix0_390180644) + z5;

            tmp0 += z1 + z3;
            tmp1 += z2 + z4;
            tmp2 += z2 + z3;
            tmp3 += z1 + z4;
        }

        private Image ComposeGrayscale()
        {
            var component = _frameComponents[0];
            var plane = component.Plane;
            var pixels = new Argb[_width * _height];
            for (var y = 0; y < _height; y++)
            {
                var row = plane.AsSpan(y * component.Stride, _width);
                var destination = pixels.AsSpan(y * _width, _width);
                for (var x = 0; x < row.Length; x++)
                {
                    var sample = row[x];
                    destination[x] = new Argb(255, sample, sample, sample);
                }
            }

            return Image.Create(_width, _height, pixels);
        }

        private Image ComposeYcbcr()
        {
            // Like libjpeg, the components are Y, Cb and Cr in frame order whatever their identifiers.
            var rowBuffers = new byte[3][];
            for (var i = 0; i < rowBuffers.Length; i++)
            {
                rowBuffers[i] = new byte[(_width + 2) & ~1];
            }

            var pixels = new Argb[_width * _height];
            for (var y = 0; y < _height; y++)
            {
                var yRow = GetUpsampledRow(_frameComponents[0], y, rowBuffers[0]);
                var cbRow = GetUpsampledRow(_frameComponents[1], y, rowBuffers[1]);
                var crRow = GetUpsampledRow(_frameComponents[2], y, rowBuffers[2]);
                var destination = pixels.AsSpan(y * _width, _width);
                for (var x = 0; x < destination.Length; x++)
                {
                    int luma = yRow[x];
                    var cb = cbRow[x];
                    var cr = crRow[x];
                    var red = luma + CrToRed[cr];
                    var green = luma + ((CbToGreen[cb] + CrToGreen[cr]) >> ColorScaleBits);
                    var blue = luma + CbToBlue[cb];
                    destination[x] = new Argb(255, ClampToByte(red), ClampToByte(green), ClampToByte(blue));
                }
            }

            return Image.Create(_width, _height, pixels);
        }

        /// <summary>
        /// Returns the samples of one output row for a component, upsampled to the image width. 2:1 horizontal
        /// and/or vertical factors use libjpeg's "fancy" triangle filter (jdsample.c), other factors replicate samples.
        /// </summary>
        private ReadOnlySpan<byte> GetUpsampledRow(FrameComponent component, int y, byte[] buffer)
        {
            var plane = component.Plane;
            var horizontalFactor = _maxHorizontalSamplingFactor / component.HorizontalSamplingFactor;
            var verticalFactor = _maxVerticalSamplingFactor / component.VerticalSamplingFactor;
            if (horizontalFactor == 1 && verticalFactor == 1)
                return plane.AsSpan(y * component.Stride, _width);

            var componentWidth = component.Width;
            if (horizontalFactor == 2 && verticalFactor == 1 && componentWidth > 2)
            {
                var input = plane.AsSpan(y * component.Stride, componentWidth);
                for (var i = 0; i < componentWidth; i++)
                {
                    var nearer = input[i] * 3;
                    buffer[2 * i] = (byte)((nearer + input[i == 0 ? 0 : i - 1] + 1) >> 2);
                    buffer[(2 * i) + 1] = (byte)((nearer + input[i == componentWidth - 1 ? i : i + 1] + 2) >> 2);
                }

                return buffer.AsSpan(0, _width);
            }

            if (verticalFactor == 2 && (horizontalFactor == 1 || (horizontalFactor == 2 && componentWidth > 2)))
            {
                // Even output rows lean on the input row above, odd rows on the row below. The edge rows are replicated.
                var inputRow = y / 2;
                var fartherRow = (y & 1) == 0 ? Math.Max(inputRow - 1, 0) : Math.Min(inputRow + 1, component.Height - 1);
                var nearer = plane.AsSpan(inputRow * component.Stride, componentWidth);
                var farther = plane.AsSpan(fartherRow * component.Stride, componentWidth);
                if (horizontalFactor == 1)
                {
                    var bias = (y & 1) == 0 ? 1 : 2;
                    for (var i = 0; i < componentWidth; i++)
                    {
                        buffer[i] = (byte)(((nearer[i] * 3) + farther[i] + bias) >> 2);
                    }

                    return buffer.AsSpan(0, _width);
                }

                var previousSum = (nearer[0] * 3) + farther[0];
                var currentSum = previousSum;
                for (var i = 0; i < componentWidth; i++)
                {
                    var nextSum = i == componentWidth - 1 ? currentSum : (nearer[i + 1] * 3) + farther[i + 1];
                    buffer[2 * i] = (byte)(((currentSum * 3) + previousSum + 8) >> 4);
                    buffer[(2 * i) + 1] = (byte)(((currentSum * 3) + nextSum + 7) >> 4);
                    previousSum = currentSum;
                    currentSum = nextSum;
                }

                return buffer.AsSpan(0, _width);
            }

            var source = plane.AsSpan((y / verticalFactor) * component.Stride, componentWidth);
            for (var x = 0; x < _width; x++)
            {
                buffer[x] = source[x / horizontalFactor];
            }

            return buffer.AsSpan(0, _width);
        }

        private static byte ClampToByte(int value)
        {
            return (byte)Math.Clamp(value, 0, 255);
        }

        private static int DivideRoundUp(int value, int divisor)
        {
            return (value + divisor - 1) / divisor;
        }

        private static bool IsStartOfFrameMarker(byte marker)
        {
            return marker is >= 0xC0 and <= 0xCF and not (DefineHuffmanTableMarker or 0xC8 or 0xCC);
        }

        private byte ReadMarker(ref int position)
        {
            while (position < _data.Length && _data[position] != MarkerPrefix)
            {
                position++;
            }

            if (position >= _data.Length)
                throw new InvalidDataException("Invalid JPEG marker stream.");

            while (position < _data.Length && _data[position] == MarkerPrefix)
            {
                position++;
            }

            if (position >= _data.Length)
                throw new InvalidDataException("Invalid JPEG marker stream.");

            var marker = _data[position++];
            if (marker == 0x00)
                throw new InvalidDataException("Invalid JPEG marker stream.");

            return marker;
        }

        private ReadOnlySpan<byte> ReadSegment(ref int position)
        {
            if (position + 2 > _data.Length)
                throw new InvalidDataException("The JPEG segment header is truncated.");

            var length = BinaryPrimitives.ReadUInt16BigEndian(_data.AsSpan(position, 2));
            position += 2;

            if (length < 2)
                throw new InvalidDataException("The JPEG segment length is invalid.");

            var payloadLength = length - 2;
            if (position + payloadLength > _data.Length)
                throw new InvalidDataException("The JPEG segment is truncated.");

            var segment = _data.AsSpan(position, payloadLength);
            position += payloadLength;
            return segment;
        }

        private static ushort ReadUInt16(ReadOnlySpan<byte> data, int offset, bool littleEndian)
        {
            if (offset + 2 > data.Length)
                throw new InvalidDataException("The EXIF metadata is truncated.");

            var value = data[offset..(offset + 2)];
            return littleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(value) : BinaryPrimitives.ReadUInt16BigEndian(value);
        }

        private static uint ReadUInt32(ReadOnlySpan<byte> data, int offset, bool littleEndian)
        {
            if (offset + 4 > data.Length)
                throw new InvalidDataException("The EXIF metadata is truncated.");

            var value = data[offset..(offset + 4)];
            return littleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(value) : BinaryPrimitives.ReadUInt32BigEndian(value);
        }
    }

    private sealed class FrameComponent(byte id, int horizontalSamplingFactor, int verticalSamplingFactor, int quantizationTableIndex)
    {
        public byte Id { get; } = id;
        public int HorizontalSamplingFactor { get; } = horizontalSamplingFactor;
        public int VerticalSamplingFactor { get; } = verticalSamplingFactor;
        public int QuantizationTableIndex { get; } = quantizationTableIndex;

        /// <summary>Gets or sets the number of samples per line, ceil(image width * H / Hmax).</summary>
        public int Width { get; set; }

        /// <summary>Gets or sets the number of lines, ceil(image height * V / Vmax).</summary>
        public int Height { get; set; }

        /// <summary>Gets or sets the block count of a line in a non-interleaved scan.</summary>
        public int BlocksPerLine { get; set; }

        /// <summary>Gets or sets the block count of a column in a non-interleaved scan.</summary>
        public int BlocksPerColumn { get; set; }

        /// <summary>Gets or sets the width of <see cref="Plane"/>, which covers every block of the interleaved MCU grid.</summary>
        public int Stride { get; set; }

        public int PlaneHeight { get; set; }

        public byte[]? Plane { get; set; }

        public int DcPredictor { get; set; }
    }

    private sealed record ScanComponent(FrameComponent FrameComponent, HuffmanTable DcTable, HuffmanTable AcTable, ushort[] QuantizationTable);

    private sealed class HuffmanTable
    {
        public const int LookupBits = 9;

        // (code length << 8) | symbol for every code of at most LookupBits bits, indexed by the next LookupBits
        // bits of the stream; 0 when the code is longer.
        private readonly ushort[] _lookup = new ushort[1 << LookupBits];
        private readonly int[] _maxCode = new int[17];
        private readonly int[] _valueOffset = new int[17];
        private readonly byte[] _symbols;

        private HuffmanTable(byte[] symbols)
        {
            _symbols = symbols;
        }

        public ReadOnlySpan<ushort> Lookup => _lookup;

        public static HuffmanTable Create(ReadOnlySpan<byte> codeLengths, byte[] symbols)
        {
            if (codeLengths.Length != 16)
                throw new InvalidDataException("Invalid JPEG Huffman table lengths.");

            var table = new HuffmanTable(symbols);
            var code = 0;
            var symbolIndex = 0;
            for (var bitLength = 1; bitLength <= 16; bitLength++)
            {
                var count = codeLengths[bitLength - 1];
                table._valueOffset[bitLength] = symbolIndex - code;
                if (count == 0)
                {
                    table._maxCode[bitLength] = -1;
                }
                else
                {
                    if (code + count > (1 << bitLength))
                        throw new InvalidDataException("Invalid JPEG Huffman table.");

                    if (bitLength <= LookupBits)
                    {
                        for (var i = 0; i < count; i++)
                        {
                            var entry = (ushort)((bitLength << 8) | symbols[symbolIndex + i]);
                            var first = (code + i) << (LookupBits - bitLength);
                            table._lookup.AsSpan(first, 1 << (LookupBits - bitLength)).Fill(entry);
                        }
                    }

                    table._maxCode[bitLength] = code + count - 1;
                    symbolIndex += count;
                    code += count;
                }

                code <<= 1;
            }

            return table;
        }

        /// <summary>Decodes a code longer than <see cref="LookupBits"/> from the next 16 bits of the stream.</summary>
        public int DecodeLongCode(int next16Bits, out int length)
        {
            for (length = LookupBits + 1; length <= 16; length++)
            {
                var code = next16Bits >> (16 - length);
                if (code <= _maxCode[length])
                {
                    var symbolIndex = _valueOffset[length] + code;
                    if ((uint)symbolIndex >= (uint)_symbols.Length)
                        throw new InvalidDataException("Invalid JPEG Huffman symbol index.");

                    return _symbols[symbolIndex];
                }
            }

            throw new InvalidDataException("Invalid JPEG Huffman code.");
        }
    }

    /// <summary>
    /// Reads the entropy-coded segment most significant bit first, removing the stuffed zero bytes. Once a
    /// marker or the end of the data is reached the reader supplies zero bits, like libjpeg, so a code can be
    /// looked ahead; consuming any of those padding bits means the scan data is truncated.
    /// </summary>
    private struct EntropyReader(byte[] data, int start, int end)
    {
        private readonly byte[] _data = data;
        private readonly int _end = end;
        private int _position = start;
        private ulong _bitBuffer;
        private int _bitsInBuffer;
        private int _paddingBits;
        private bool _reachedMarker;

        public int DecodeHuffman(HuffmanTable table)
        {
            if (_bitsInBuffer < 16)
                Fill();

            var lookupEntry = table.Lookup[(int)(_bitBuffer >> (_bitsInBuffer - HuffmanTable.LookupBits)) & ((1 << HuffmanTable.LookupBits) - 1)];
            if (lookupEntry != 0)
            {
                Consume(lookupEntry >> 8);
                return lookupEntry & 0xFF;
            }

            var symbol = table.DecodeLongCode((int)(_bitBuffer >> (_bitsInBuffer - 16)) & 0xFFFF, out var length);
            Consume(length);
            return symbol;
        }

        public int ReceiveExtend(int count)
        {
            if (count == 0)
                return 0;

            if (_bitsInBuffer < count)
                Fill();

            var value = (int)(_bitBuffer >> (_bitsInBuffer - count)) & ((1 << count) - 1);
            Consume(count);
            return value < (1 << (count - 1)) ? value - (1 << count) + 1 : value;
        }

        public void ConsumeRestartMarker(byte expectedMarker)
        {
            // The bits left in the buffer are the padding of the last byte before the marker
            _bitBuffer = 0;
            _bitsInBuffer = 0;
            _paddingBits = 0;
            _reachedMarker = false;

            while (_position + 1 < _end && !(_data[_position] == MarkerPrefix && _data[_position + 1] != 0x00))
            {
                _position += _data[_position] == MarkerPrefix ? 2 : 1;
            }

            while (_position < _end && _data[_position] == MarkerPrefix)
            {
                _position++;
            }

            if (_position >= _end)
                throw new InvalidDataException("The JPEG restart marker is missing.");

            var marker = _data[_position++];
            if (marker != expectedMarker)
                throw new InvalidDataException("Unexpected JPEG restart marker.");
        }

        private void Consume(int count)
        {
            _bitsInBuffer -= count;
            if (_bitsInBuffer < _paddingBits)
                throw new InvalidDataException("The JPEG scan data is truncated.");
        }

        private void Fill()
        {
            while (_bitsInBuffer <= 56)
            {
                int value;
                if (_reachedMarker || _position >= _end)
                {
                    value = 0;
                    _paddingBits += 8;
                }
                else
                {
                    value = _data[_position];
                    if (value == MarkerPrefix)
                    {
                        if (_position + 1 < _end && _data[_position + 1] == 0x00)
                        {
                            _position += 2;
                        }
                        else
                        {
                            _reachedMarker = true;
                            value = 0;
                            _paddingBits += 8;
                        }
                    }
                    else
                    {
                        _position++;
                    }
                }

                _bitBuffer = (_bitBuffer << 8) | (uint)value;
                _bitsInBuffer += 8;
            }
        }
    }
}
