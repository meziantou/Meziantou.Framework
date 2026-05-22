using System.Buffers.Binary;

namespace Meziantou.Framework.SnapshotTesting;

internal static class JpegImageLoader
{
    private const byte MarkerPrefix = 0xFF;
    private const byte StartOfImageMarker = 0xD8;
    private const byte EndOfImageMarker = 0xD9;
    private const byte StartOfFrameBaselineMarker = 0xC0;
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

    private static readonly int[] ZigZagOrder =
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

    private const double InverseSqrt2 = 0.7071067811865476;
    private static readonly double[][] CosineTable = CreateCosineTable();

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

    private static double[][] CreateCosineTable()
    {
        var table = new double[8][];
        for (var x = 0; x < 8; x++)
        {
            table[x] = new double[8];
            for (var u = 0; u < 8; u++)
            {
                table[x][u] = Math.Cos((2 * x + 1) * u * Math.PI / 16);
            }
        }

        return table;
    }

    private sealed class Decoder(byte[] data)
    {
        private readonly byte[] _data = data;
        private readonly short[][] _quantizationTables = new short[4][];
        private readonly HuffmanTable?[][] _huffmanTables =
        [
            new HuffmanTable?[4],
            new HuffmanTable?[4],
        ];

        private readonly Dictionary<byte, FrameComponent> _frameComponents = [];
        private int _width;
        private int _height;
        private int _maxHorizontalSamplingFactor = 1;
        private int _maxVerticalSamplingFactor = 1;
        private int _restartInterval;
        private bool _sawJfifMarker;
        private byte? _adobeTransform;
        private bool _scanDecoded;

        public Image Decode()
        {
            var position = 2;
            var sawEoi = false;
            while (position < _data.Length)
            {
                var marker = ReadMarker(ref position);
                if (marker == EndOfImageMarker)
                {
                    sawEoi = true;
                    break;
                }

                if (marker == StartOfImageMarker)
                    throw new InvalidDataException("Unexpected JPEG start marker.");

                if (marker is >= Restart0Marker and <= Restart7Marker)
                    throw new InvalidDataException("Unexpected JPEG restart marker outside scan data.");

                if (marker == StartOfScanMarker)
                {
                    if (_scanDecoded)
                        throw new NotSupportedException("Progressive or multi-scan JPEG images are not supported.");

                    var scanHeader = ReadSegment(ref position);
                    var scanComponents = ParseStartOfScan(scanHeader);
                    var nextMarkerOffset = FindEntropyDataEnd(position);
                    var entropyData = _data.AsSpan(position, nextMarkerOffset - position);
                    DecodeScan(entropyData, scanComponents);
                    _scanDecoded = true;
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
                    case StartOfFrameBaselineMarker:
                        ParseStartOfFrameBaseline(segment);
                        break;
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
                            throw new NotSupportedException("Only baseline JPEG images are supported.");

                        break;
                }
            }

            if (!_scanDecoded)
                throw new InvalidDataException("The JPEG image does not contain scan data.");

            if (!sawEoi)
                throw new InvalidDataException("The JPEG image is truncated.");

            return DecodeToImage();
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

                if (precision != 0)
                    throw new NotSupportedException("Only 8-bit JPEG quantization tables are supported.");

                if (position + 64 > segment.Length)
                    throw new InvalidDataException("The JPEG quantization table is truncated.");

                var table = new short[64];
                for (var i = 0; i < 64; i++)
                {
                    table[ZigZagOrder[i]] = segment[position++];
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

                if (position + symbolCount > segment.Length)
                    throw new InvalidDataException("The JPEG Huffman symbols are truncated.");

                var symbols = segment.Slice(position, symbolCount).ToArray();
                position += symbolCount;
                _huffmanTables[tableClass][tableIndex] = HuffmanTable.Create(lengths, symbols);
            }
        }

        private void ParseStartOfFrameBaseline(ReadOnlySpan<byte> segment)
        {
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

            if (componentCount is not 1 and not 3)
                throw new NotSupportedException("Only grayscale and YCbCr JPEG images are supported.");

            var expectedLength = checked(6 + componentCount * 3);
            if (segment.Length != expectedLength)
                throw new InvalidDataException("Invalid JPEG frame header length.");

            _frameComponents.Clear();
            _maxHorizontalSamplingFactor = 1;
            _maxVerticalSamplingFactor = 1;

            for (var i = 0; i < componentCount; i++)
            {
                var offset = 6 + i * 3;
                var id = segment[offset];
                var sampling = segment[offset + 1];
                var horizontalSamplingFactor = sampling >> 4;
                var verticalSamplingFactor = sampling & 0x0F;
                var quantizationTableIndex = segment[offset + 2];

                if (horizontalSamplingFactor == 0 || verticalSamplingFactor == 0)
                    throw new InvalidDataException("Invalid JPEG sampling factors.");

                if (horizontalSamplingFactor > 2 || verticalSamplingFactor > 2)
                    throw new NotSupportedException("Only 4:4:4, 4:2:2, and 4:2:0 JPEG sampling is supported.");

                if (quantizationTableIndex >= _quantizationTables.Length)
                    throw new NotSupportedException("Unsupported JPEG quantization table index.");

                var frameComponent = new FrameComponent(id, horizontalSamplingFactor, verticalSamplingFactor, quantizationTableIndex);
                if (!_frameComponents.TryAdd(id, frameComponent))
                    throw new InvalidDataException("Duplicate JPEG frame component identifier.");

                if (horizontalSamplingFactor > _maxHorizontalSamplingFactor)
                    _maxHorizontalSamplingFactor = horizontalSamplingFactor;

                if (verticalSamplingFactor > _maxVerticalSamplingFactor)
                    _maxVerticalSamplingFactor = verticalSamplingFactor;
            }

            if (componentCount == 3)
            {
                if (_maxHorizontalSamplingFactor > 2 || _maxVerticalSamplingFactor > 2)
                    throw new NotSupportedException("Only 4:4:4, 4:2:2, and 4:2:0 JPEG sampling is supported.");

                if (_maxHorizontalSamplingFactor == 1 && _maxVerticalSamplingFactor == 2)
                    throw new NotSupportedException("Unsupported JPEG component sampling layout.");

                var maxSamplingComponentCount = 0;
                foreach (var frameComponent in _frameComponents.Values)
                {
                    if (frameComponent.HorizontalSamplingFactor == _maxHorizontalSamplingFactor &&
                        frameComponent.VerticalSamplingFactor == _maxVerticalSamplingFactor)
                    {
                        maxSamplingComponentCount++;
                    }
                }

                if (_maxHorizontalSamplingFactor == 1 && _maxVerticalSamplingFactor == 1)
                {
                    if (maxSamplingComponentCount != 3)
                        throw new NotSupportedException("Unsupported JPEG component sampling layout.");
                }
                else if (_maxHorizontalSamplingFactor == 2 && (_maxVerticalSamplingFactor == 1 || _maxVerticalSamplingFactor == 2))
                {
                    if (maxSamplingComponentCount != 1)
                        throw new NotSupportedException("Unsupported JPEG component sampling layout.");

                    foreach (var frameComponent in _frameComponents.Values)
                    {
                        if (frameComponent.HorizontalSamplingFactor == _maxHorizontalSamplingFactor &&
                            frameComponent.VerticalSamplingFactor == _maxVerticalSamplingFactor)
                        {
                            continue;
                        }

                        if (frameComponent.HorizontalSamplingFactor != 1 || frameComponent.VerticalSamplingFactor != 1)
                            throw new NotSupportedException("Unsupported JPEG component sampling layout.");
                    }
                }
                else
                {
                    throw new NotSupportedException("Only 4:4:4, 4:2:2, and 4:2:0 JPEG sampling is supported.");
                }
            }
        }

        private ScanComponent[] ParseStartOfScan(ReadOnlySpan<byte> segment)
        {
            if (_frameComponents.Count == 0)
                throw new InvalidDataException("The JPEG frame header is missing.");

            if (segment.Length < 2)
                throw new InvalidDataException("The JPEG scan header is truncated.");

            var componentCount = segment[0];
            if (componentCount == 0)
                throw new InvalidDataException("The JPEG scan component count is invalid.");

            if (componentCount != _frameComponents.Count)
                throw new NotSupportedException("Only single-scan baseline JPEG images are supported.");

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

                if (!_frameComponents.TryGetValue(componentId, out var frameComponent))
                    throw new InvalidDataException("The JPEG scan references an unknown frame component.");

                var dcTable = _huffmanTables[0][dcTableIndex] ?? throw new InvalidDataException("The JPEG DC Huffman table is missing.");
                var acTable = _huffmanTables[1][acTableIndex] ?? throw new InvalidDataException("The JPEG AC Huffman table is missing.");
                scanComponents[i] = new ScanComponent(frameComponent, dcTable, acTable);
            }

            var spectralSelectionStart = segment[offset++];
            var spectralSelectionEnd = segment[offset++];
            var successiveApproximation = segment[offset];
            if (spectralSelectionStart != 0 || spectralSelectionEnd != 63 || successiveApproximation != 0)
                throw new NotSupportedException("Only baseline JPEG scans are supported.");

            ValidateColorModel(scanComponents);
            return scanComponents;
        }

        private void ValidateColorModel(ScanComponent[] scanComponents)
        {
            if (scanComponents.Length == 1)
                return;

            if (_adobeTransform == 2)
                throw new NotSupportedException("CMYK/YCCK JPEG images are not supported.");

            if (_adobeTransform == 0)
                throw new NotSupportedException("JPEG images with Adobe transform 0 are not supported.");

            if (_sawJfifMarker || _adobeTransform == 1)
                return;

            var hasY = _frameComponents.ContainsKey(1);
            var hasCb = _frameComponents.ContainsKey(2);
            var hasCr = _frameComponents.ContainsKey(3);
            if (!hasY || !hasCb || !hasCr)
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
                    throw new InvalidDataException("The JPEG scan data is truncated.");

                var marker = _data[position];
                if (marker == 0x00 || marker is >= Restart0Marker and <= Restart7Marker)
                {
                    position++;
                    continue;
                }

                return markerOffset;
            }

            throw new InvalidDataException("The JPEG scan data is truncated.");
        }

        private void DecodeScan(ReadOnlySpan<byte> entropyData, ScanComponent[] scanComponents)
        {
            var entropyReader = new EntropyReader(entropyData.ToArray());
            var mcuWidth = checked(8 * _maxHorizontalSamplingFactor);
            var mcuHeight = checked(8 * _maxVerticalSamplingFactor);
            var mcuCountX = (_width + mcuWidth - 1) / mcuWidth;
            var mcuCountY = (_height + mcuHeight - 1) / mcuHeight;

            var decodedMcus = new DecodedMcu[scanComponents.Length];
            var pixels = new Argb[checked(_width * _height)];

            var mcuSinceRestart = 0;
            var expectedRestartMarker = Restart0Marker;

            for (var mcuY = 0; mcuY < mcuCountY; mcuY++)
            {
                for (var mcuX = 0; mcuX < mcuCountX; mcuX++)
                {
                    if (_restartInterval > 0 && mcuSinceRestart == _restartInterval)
                    {
                        entropyReader.ConsumeRestartMarker(expectedRestartMarker);
                        expectedRestartMarker = (byte)(expectedRestartMarker == Restart7Marker ? Restart0Marker : expectedRestartMarker + 1);

                        foreach (var scanComponent in scanComponents)
                        {
                            scanComponent.FrameComponent.ResetPredictor();
                        }

                        mcuSinceRestart = 0;
                    }

                    for (var componentIndex = 0; componentIndex < scanComponents.Length; componentIndex++)
                    {
                        var scanComponent = scanComponents[componentIndex];
                        var blockCount = checked(scanComponent.FrameComponent.HorizontalSamplingFactor * scanComponent.FrameComponent.VerticalSamplingFactor);
                        var blocks = new byte[blockCount][];
                        for (var blockIndex = 0; blockIndex < blockCount; blockIndex++)
                        {
                            blocks[blockIndex] = DecodeBlock(entropyReader, scanComponent);
                        }

                        decodedMcus[componentIndex] = new DecodedMcu(scanComponent.FrameComponent, blocks);
                    }

                    WriteMcuPixels(decodedMcus, mcuX, mcuY, pixels);
                    mcuSinceRestart++;
                }
            }

            _decodedImage = Image.Create(_width, _height, pixels);
        }

        private Image? _decodedImage;

        private Image DecodeToImage()
        {
            return _decodedImage ?? throw new InvalidDataException("The JPEG image did not decode.");
        }

        private byte[] DecodeBlock(EntropyReader entropyReader, ScanComponent scanComponent)
        {
            var coefficients = new int[64];
            var quantizationTable = _quantizationTables[scanComponent.FrameComponent.QuantizationTableIndex];
            if (quantizationTable is null)
                throw new InvalidDataException("The JPEG quantization table is missing.");

            var dcCodeLength = scanComponent.DcTable.Decode(entropyReader);
            var dcDifference = entropyReader.ReceiveExtend(dcCodeLength);
            var dc = scanComponent.FrameComponent.PredictDc(dcDifference);
            coefficients[0] = checked(dc * quantizationTable[0]);

            var zigZagIndex = 1;
            while (zigZagIndex < 64)
            {
                var symbol = scanComponent.AcTable.Decode(entropyReader);
                if (symbol == 0x00)
                    break;

                var runLength = symbol >> 4;
                var codeLength = symbol & 0x0F;
                if (codeLength == 0)
                {
                    if (runLength != 0x0F)
                        throw new InvalidDataException("Invalid JPEG AC coefficient run-length.");

                    zigZagIndex += 16;
                    continue;
                }

                zigZagIndex += runLength;
                if (zigZagIndex >= 64)
                    throw new InvalidDataException("Invalid JPEG AC coefficient index.");

                var coefficient = entropyReader.ReceiveExtend(codeLength);
                var naturalIndex = ZigZagOrder[zigZagIndex];
                coefficients[naturalIndex] = checked(coefficient * quantizationTable[naturalIndex]);
                zigZagIndex++;
            }

            return InverseDct(coefficients);
        }

        private static byte[] InverseDct(int[] coefficients)
        {
            var output = new byte[64];
            for (var y = 0; y < 8; y++)
            {
                for (var x = 0; x < 8; x++)
                {
                    double value = 0;
                    for (var v = 0; v < 8; v++)
                    {
                        var verticalScale = v == 0 ? InverseSqrt2 : 1.0;
                        var cosineY = CosineTable[y][v];
                        for (var u = 0; u < 8; u++)
                        {
                            var horizontalScale = u == 0 ? InverseSqrt2 : 1.0;
                            var cosineX = CosineTable[x][u];
                            value += horizontalScale * verticalScale * coefficients[v * 8 + u] * cosineX * cosineY;
                        }
                    }

                    var pixel = (int)Math.Round(value * 0.25 + 128.0, MidpointRounding.AwayFromZero);
                    output[y * 8 + x] = (byte)Math.Clamp(pixel, 0, 255);
                }
            }

            return output;
        }

        private void WriteMcuPixels(DecodedMcu[] decodedMcus, int mcuX, int mcuY, Argb[] pixels)
        {
            if (decodedMcus.Length == 1)
            {
                var grayscale = decodedMcus[0];
                WriteMcuPixelsGrayscale(grayscale, mcuX, mcuY, pixels);
                return;
            }

            WriteMcuPixelsYcbcr(decodedMcus, mcuX, mcuY, pixels);
        }

        private void WriteMcuPixelsGrayscale(DecodedMcu grayscale, int mcuX, int mcuY, Argb[] pixels)
        {
            var mcuWidth = checked(8 * _maxHorizontalSamplingFactor);
            var mcuHeight = checked(8 * _maxVerticalSamplingFactor);
            var originX = mcuX * mcuWidth;
            var originY = mcuY * mcuHeight;

            for (var y = 0; y < mcuHeight; y++)
            {
                var absoluteY = originY + y;
                if (absoluteY >= _height)
                    break;

                for (var x = 0; x < mcuWidth; x++)
                {
                    var absoluteX = originX + x;
                    if (absoluteX >= _width)
                        break;

                    var sample = grayscale.GetSample(x, y, _maxHorizontalSamplingFactor, _maxVerticalSamplingFactor);
                    pixels[absoluteY * _width + absoluteX] = CreateArgb(sample, sample, sample);
                }
            }
        }

        private void WriteMcuPixelsYcbcr(DecodedMcu[] decodedMcus, int mcuX, int mcuY, Argb[] pixels)
        {
            var yComponent = decodedMcus[0];
            var cbComponent = decodedMcus[1];
            var crComponent = decodedMcus[2];
            if (_frameComponents.ContainsKey(1) && _frameComponents.ContainsKey(2) && _frameComponents.ContainsKey(3))
            {
                for (var i = 0; i < decodedMcus.Length; i++)
                {
                    var component = decodedMcus[i];
                    if (component.FrameComponent.Id == 1)
                        yComponent = component;
                    else if (component.FrameComponent.Id == 2)
                        cbComponent = component;
                    else if (component.FrameComponent.Id == 3)
                        crComponent = component;
                }
            }

            var mcuWidth = checked(8 * _maxHorizontalSamplingFactor);
            var mcuHeight = checked(8 * _maxVerticalSamplingFactor);
            var originX = mcuX * mcuWidth;
            var originY = mcuY * mcuHeight;

            for (var y = 0; y < mcuHeight; y++)
            {
                var absoluteY = originY + y;
                if (absoluteY >= _height)
                    break;

                for (var x = 0; x < mcuWidth; x++)
                {
                    var absoluteX = originX + x;
                    if (absoluteX >= _width)
                        break;

                    var ySample = yComponent.GetSample(x, y, _maxHorizontalSamplingFactor, _maxVerticalSamplingFactor);
                    var cbSample = cbComponent.GetSample(x, y, _maxHorizontalSamplingFactor, _maxVerticalSamplingFactor);
                    var crSample = crComponent.GetSample(x, y, _maxHorizontalSamplingFactor, _maxVerticalSamplingFactor);
                    pixels[absoluteY * _width + absoluteX] = ConvertYcbcrToArgb(ySample, cbSample, crSample);
                }
            }
        }

        private static Argb ConvertYcbcrToArgb(byte y, byte cb, byte cr)
        {
            var cbOffset = cb - 128.0;
            var crOffset = cr - 128.0;
            var red = ClampToByte((int)Math.Round(y + 1.40200 * crOffset, MidpointRounding.AwayFromZero));
            var green = ClampToByte((int)Math.Round(y - 0.344136 * cbOffset - 0.714136 * crOffset, MidpointRounding.AwayFromZero));
            var blue = ClampToByte((int)Math.Round(y + 1.77200 * cbOffset, MidpointRounding.AwayFromZero));
            return CreateArgb((byte)red, (byte)green, (byte)blue);
        }

        private static Argb CreateArgb(byte red, byte green, byte blue)
        {
            return new Argb((uint)(0xFF000000u | (uint)(red << 16) | (uint)(green << 8) | blue));
        }

        private static int ClampToByte(int value)
        {
            return Math.Clamp(value, 0, 255);
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
        private int _dcPredictor;

        public byte Id { get; } = id;
        public int HorizontalSamplingFactor { get; } = horizontalSamplingFactor;
        public int VerticalSamplingFactor { get; } = verticalSamplingFactor;
        public int QuantizationTableIndex { get; } = quantizationTableIndex;

        public int PredictDc(int difference)
        {
            _dcPredictor += difference;
            return _dcPredictor;
        }

        public void ResetPredictor()
        {
            _dcPredictor = 0;
        }
    }

    private readonly struct ScanComponent(FrameComponent frameComponent, HuffmanTable dcTable, HuffmanTable acTable)
    {
        public FrameComponent FrameComponent { get; } = frameComponent;
        public HuffmanTable DcTable { get; } = dcTable;
        public HuffmanTable AcTable { get; } = acTable;
    }

    private readonly struct DecodedMcu(FrameComponent frameComponent, byte[][] blocks)
    {
        public FrameComponent FrameComponent { get; } = frameComponent;
        public byte[][] Blocks { get; } = blocks;

        public byte GetSample(int xInMcu, int yInMcu, int maxHorizontalSamplingFactor, int maxVerticalSamplingFactor)
        {
            var sampleX = xInMcu * FrameComponent.HorizontalSamplingFactor / maxHorizontalSamplingFactor;
            var sampleY = yInMcu * FrameComponent.VerticalSamplingFactor / maxVerticalSamplingFactor;
            var blockX = sampleX / 8;
            var blockY = sampleY / 8;
            var block = Blocks[blockY * FrameComponent.HorizontalSamplingFactor + blockX];
            return block[(sampleY % 8) * 8 + sampleX % 8];
        }
    }

    private sealed class HuffmanTable
    {
        private readonly int[] _minCode = new int[17];
        private readonly int[] _maxCode = new int[17];
        private readonly int[] _valuePointer = new int[17];
        private readonly byte[] _symbols;

        private HuffmanTable(byte[] symbols)
        {
            _symbols = symbols;
        }

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
                if (count == 0)
                {
                    table._minCode[bitLength] = -1;
                    table._maxCode[bitLength] = -1;
                    table._valuePointer[bitLength] = symbolIndex;
                }
                else
                {
                    table._minCode[bitLength] = code;
                    table._maxCode[bitLength] = code + count - 1;
                    table._valuePointer[bitLength] = symbolIndex;
                    symbolIndex += count;
                    code += count;
                }

                code <<= 1;
            }

            return table;
        }

        public int Decode(EntropyReader reader)
        {
            var code = 0;
            for (var bitLength = 1; bitLength <= 16; bitLength++)
            {
                code = (code << 1) | reader.ReadBit();
                var maxCode = _maxCode[bitLength];
                if (maxCode == -1 || code > maxCode)
                    continue;

                var symbolIndex = _valuePointer[bitLength] + code - _minCode[bitLength];
                if ((uint)symbolIndex >= (uint)_symbols.Length)
                    throw new InvalidDataException("Invalid JPEG Huffman symbol index.");

                return _symbols[symbolIndex];
            }

            throw new InvalidDataException("Invalid JPEG Huffman code.");
        }
    }

    private sealed class EntropyReader(byte[] data)
    {
        private readonly byte[] _data = data;
        private int _position;
        private uint _bitBuffer;
        private int _bitsInBuffer;

        public int ReadBit()
        {
            return ReadBits(1);
        }

        public int ReadBits(int count)
        {
            if (count is < 0 or > 16)
                throw new ArgumentOutOfRangeException(nameof(count));

            if (count == 0)
                return 0;

            if (!TryEnsureBits(count))
                throw new InvalidDataException("The JPEG scan data is truncated.");

            var shift = _bitsInBuffer - count;
            var mask = (1 << count) - 1;
            var value = (int)((_bitBuffer >> shift) & (uint)mask);
            _bitsInBuffer -= count;
            return value;
        }

        public int ReceiveExtend(int count)
        {
            if (count == 0)
                return 0;

            var value = ReadBits(count);
            var signBit = 1 << (count - 1);
            if ((value & signBit) != 0)
                return value;

            var extension = (1 << count) - 1;
            return value - extension;
        }

        public void AlignToByte()
        {
            _bitsInBuffer -= _bitsInBuffer % 8;
        }

        public void ConsumeRestartMarker(byte expectedMarker)
        {
            AlignToByte();
            if (_position >= _data.Length)
                throw new InvalidDataException("The JPEG restart marker is missing.");

            if (_data[_position++] != MarkerPrefix)
                throw new InvalidDataException("Invalid JPEG restart marker.");

            while (_position < _data.Length && _data[_position] == MarkerPrefix)
            {
                _position++;
            }

            if (_position >= _data.Length)
                throw new InvalidDataException("The JPEG restart marker is missing.");

            var marker = _data[_position++];
            if (marker != expectedMarker)
                throw new InvalidDataException("Unexpected JPEG restart marker.");
        }

        private bool TryEnsureBits(int count)
        {
            while (_bitsInBuffer < count)
            {
                if (_position >= _data.Length)
                    return false;

                var value = _data[_position++];
                if (value == MarkerPrefix)
                {
                    if (_position >= _data.Length)
                        throw new InvalidDataException("Invalid marker in JPEG scan data.");

                    var marker = _data[_position++];
                    while (marker == MarkerPrefix)
                    {
                        if (_position >= _data.Length)
                            throw new InvalidDataException("Invalid marker in JPEG scan data.");

                        marker = _data[_position++];
                    }

                    if (marker == 0x00)
                    {
                        value = MarkerPrefix;
                    }
                    else if (marker is >= Restart0Marker and <= Restart7Marker)
                    {
                        throw new InvalidDataException("Unexpected JPEG restart marker.");
                    }
                    else
                    {
                        throw new InvalidDataException("Unexpected marker in JPEG scan data.");
                    }
                }

                _bitBuffer = (_bitBuffer << 8) | value;
                _bitsInBuffer += 8;
            }

            return true;
        }
    }
}
