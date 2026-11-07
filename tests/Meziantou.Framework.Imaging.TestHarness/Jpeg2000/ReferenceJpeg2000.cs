using System.Buffers.Binary;
using System.Text;

namespace Meziantou.Framework.Imaging.TestHarness.Jpeg2000;

/// <summary>
/// An independent reader of the structure of a JPEG 2000 codestream or of a file of the JP2 family: the boxes of the
/// file, the marker segments of the main header and of the tile-part headers, and the chain of the tile-parts. It never
/// uses the library under test and never decodes a packet.
/// </summary>
public sealed class ReferenceJpeg2000
{
    private const int MarkerSoc = 0x4F;
    private const int MarkerCap = 0x50;
    private const int MarkerSiz = 0x51;
    private const int MarkerCod = 0x52;
    private const int MarkerCoc = 0x53;
    private const int MarkerTlm = 0x55;
    private const int MarkerPlm = 0x57;
    private const int MarkerPlt = 0x58;
    private const int MarkerQcd = 0x5C;
    private const int MarkerQcc = 0x5D;
    private const int MarkerRgn = 0x5E;
    private const int MarkerPoc = 0x5F;
    private const int MarkerPpm = 0x60;
    private const int MarkerPpt = 0x61;
    private const int MarkerCom = 0x64;
    private const int MarkerSot = 0x90;
    private const int MarkerSod = 0x93;
    private const int MarkerEoc = 0xD9;

    private static readonly byte[] FileSignature = [0x00, 0x00, 0x00, 0x0C, 0x6A, 0x50, 0x20, 0x20, 0x0D, 0x0A, 0x87, 0x0A];
    private static readonly byte[] XmpUuid = [0xBE, 0x7A, 0xCF, 0xCB, 0x97, 0xA9, 0x42, 0xE8, 0x9C, 0x71, 0x99, 0x94, 0x91, 0xE3, 0xAF, 0xAC];
    private static readonly byte[] ExifUuid = [0x4A, 0x70, 0x67, 0x54, 0x69, 0x66, 0x66, 0x45, 0x78, 0x69, 0x66, 0x2D, 0x3E, 0x4A, 0x50, 0x32];

    private readonly List<string> _boxTypes = [];
    private readonly List<ReferenceJpeg2000Component> _components = [];
    private readonly List<ReferenceJpeg2000CodingStyle> _codingStyles = [];
    private readonly List<ReferenceJpeg2000TilePart> _tileParts = [];
    private readonly List<ReferenceJp2Channel> _channelDefinitions = [];
    private readonly List<ReferenceJp2ComponentMapping> _componentMappings = [];
    private readonly List<int> _paletteDepths = [];
    private readonly List<(int Component, int Shift)> _regions = [];
    private readonly List<int> _mainHeaderMarkers = [];

    private ReferenceJpeg2000()
    {
    }

    /// <summary>Gets a value indicating whether the data is a file of the JP2 family (boxes), not a bare codestream.</summary>
    public bool IsFile { get; private set; }

    /// <summary>Gets the types of the boxes of the file, in file order; the boxes of the JP2 header box follow it.</summary>
    public IReadOnlyList<string> BoxTypes => _boxTypes;

    /// <summary>Gets the brand of the file type box.</summary>
    public string? Brand { get; private set; }

    /// <summary>Gets the component count of the image header box.</summary>
    public int? HeaderComponentCount { get; private set; }

    /// <summary>Gets the BPC field of the image header box: the precision minus one, with the sign in the bit 7, or 255 when the components differ.</summary>
    public int? HeaderBitsPerComponent { get; private set; }

    /// <summary>Gets the method of the first color specification box: 1 an enumerated color space, 2 a restricted ICC profile, 3 any ICC profile.</summary>
    public int? ColorMethod { get; private set; }

    /// <summary>Gets the enumerated color space of the first color specification box: 16 sRGB, 17 grayscale, 18 sYCC, 12 CMYK.</summary>
    public int? EnumeratedColorSpace { get; private set; }

    /// <summary>Gets the ICC profile of the first color specification box.</summary>
    public ReadOnlyMemory<byte>? IccProfile { get; private set; }

    /// <summary>Gets the number of color specification boxes.</summary>
    public int ColorSpecificationCount { get; private set; }

    /// <summary>Gets the number of entries of the palette box, or 0.</summary>
    public int PaletteEntryCount { get; private set; }

    /// <summary>Gets the depth of each column of the palette box.</summary>
    public IReadOnlyList<int> PaletteDepths => _paletteDepths;

    /// <summary>Gets the entries of the component mapping box.</summary>
    public IReadOnlyList<ReferenceJp2ComponentMapping> ComponentMappings => _componentMappings;

    /// <summary>Gets the entries of the channel definition box.</summary>
    public IReadOnlyList<ReferenceJp2Channel> ChannelDefinitions => _channelDefinitions;

    /// <summary>Gets the capture resolution box: the grid points per meter, vertically then horizontally, each as a numerator, a denominator and a decimal exponent.</summary>
    public (int VerticalNumerator, int VerticalDenominator, int HorizontalNumerator, int HorizontalDenominator, int VerticalExponent, int HorizontalExponent)? CaptureResolution { get; private set; }

    /// <summary>Gets the default display resolution box, with the fields of <see cref="CaptureResolution"/>.</summary>
    public (int VerticalNumerator, int VerticalDenominator, int HorizontalNumerator, int HorizontalDenominator, int VerticalExponent, int HorizontalExponent)? DisplayResolution { get; private set; }

    /// <summary>Gets the payload of the first UUID box of an XMP packet.</summary>
    public ReadOnlyMemory<byte>? Xmp { get; private set; }

    /// <summary>Gets the payload of the first UUID box of an EXIF profile: a TIFF structure.</summary>
    public ReadOnlyMemory<byte>? Exif { get; private set; }

    /// <summary>Gets the number of contiguous codestream boxes.</summary>
    public int CodestreamCount { get; private set; }

    /// <summary>Gets the first codestream: the data itself, or the payload of the first contiguous codestream box.</summary>
    public ReadOnlyMemory<byte> Codestream { get; private set; }

    /// <summary>Gets the offset of the first codestream in the data.</summary>
    public int CodestreamOffset { get; private set; }

    /// <summary>Gets Rsiz, the capabilities of the SIZ marker segment.</summary>
    public int Capabilities { get; private set; }

    /// <summary>Gets the width of the image area.</summary>
    public int Width { get; private set; }

    /// <summary>Gets the height of the image area.</summary>
    public int Height { get; private set; }

    /// <summary>Gets the horizontal offset of the image area on the reference grid.</summary>
    public int ImageXOffset { get; private set; }

    /// <summary>Gets the vertical offset of the image area on the reference grid.</summary>
    public int ImageYOffset { get; private set; }

    public int TileWidth { get; private set; }

    public int TileHeight { get; private set; }

    public int TileXOffset { get; private set; }

    public int TileYOffset { get; private set; }

    /// <summary>Gets the number of tiles of the tile grid.</summary>
    public int TileCount { get; private set; }

    public IReadOnlyList<ReferenceJpeg2000Component> Components => _components;

    /// <summary>Gets Pcap, the parts of ISO/IEC 15444 the CAP marker segment declares, or <see langword="null"/> without one.</summary>
    public uint? ExtendedCapabilities { get; private set; }

    /// <summary>Gets Ccap15, the capabilities of Rec. ITU-T T.814 | ISO/IEC 15444-15, or <see langword="null"/>.</summary>
    public int? HighThroughputCapabilities { get; private set; }

    /// <summary>Gets the progression order of the COD marker segment of the main header: 0 LRCP, 1 RLCP, 2 RPCL, 3 PCRL, 4 CPRL.</summary>
    public int ProgressionOrder { get; private set; }

    /// <summary>Gets the number of layers.</summary>
    public int LayerCount { get; private set; }

    /// <summary>Gets a value indicating whether the first three components go through the multiple component transformation.</summary>
    public bool UsesComponentTransform { get; private set; }

    /// <summary>Gets a value indicating whether the packets start with an SOP marker segment.</summary>
    public bool UsesSop { get; private set; }

    /// <summary>Gets a value indicating whether the packet headers end with an EPH marker.</summary>
    public bool UsesEph { get; private set; }

    /// <summary>Gets the COD marker segment of the main header, then every other COD and COC marker segment, in codestream order.</summary>
    public IReadOnlyList<ReferenceJpeg2000CodingStyle> CodingStyles => _codingStyles;

    /// <summary>Gets the quantization style of the QCD marker segment of the main header: 0 none, 1 scalar derived, 2 scalar expounded.</summary>
    public int QuantizationStyle { get; private set; }

    /// <summary>Gets the number of guard bits of the QCD marker segment of the main header.</summary>
    public int GuardBits { get; private set; }

    /// <summary>Gets the number of QCC marker segments.</summary>
    public int ComponentQuantizationCount { get; private set; }

    /// <summary>Gets the RGN marker segments: the component and the shift of its region of interest.</summary>
    public IReadOnlyList<(int Component, int Shift)> Regions => _regions;

    /// <summary>Gets the number of progression order changes of the POC marker segments.</summary>
    public int ProgressionChangeCount { get; private set; }

    /// <summary>Gets the marker segments of the main header, in order (the second byte of each marker).</summary>
    public IReadOnlyList<int> MainHeaderMarkers => _mainHeaderMarkers;

    public bool HasPackedHeadersInMainHeader => _mainHeaderMarkers.Contains(MarkerPpm);

    public bool HasPackedHeadersInTilePartHeaders => _tileParts.Any(part => part.Markers.Contains(MarkerPpt));

    public bool HasPacketLengths => _mainHeaderMarkers.Contains(MarkerPlm) || _tileParts.Any(part => part.Markers.Contains(MarkerPlt));

    public bool HasTilePartLengths => _mainHeaderMarkers.Contains(MarkerTlm);

    public bool HasComment => _mainHeaderMarkers.Contains(MarkerCom) || _tileParts.Any(part => part.Markers.Contains(MarkerCom));

    /// <summary>Gets the tile-parts, in codestream order.</summary>
    public IReadOnlyList<ReferenceJpeg2000TilePart> TileParts => _tileParts;

    /// <summary>Gets a value indicating whether the codestream ends with the EOC marker.</summary>
    public bool HasEndOfCodestream { get; private set; }

    /// <summary>Reads the structure of a codestream or of a file.</summary>
    /// <param name="data">The codestream or the file.</param>
    /// <exception cref="InvalidDataException">The structure is malformed.</exception>
    public static ReferenceJpeg2000 Parse(ReadOnlySpan<byte> data) => Parse(data.ToArray());

    /// <inheritdoc cref="Parse(ReadOnlySpan{byte})"/>
    public static ReferenceJpeg2000 Parse(byte[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        var result = new ReferenceJpeg2000();
        if (data.AsSpan().StartsWith(FileSignature))
        {
            result.IsFile = true;
            result.ReadBoxes(data, 0, data.Length, isTopLevel: true);
            if (result.CodestreamCount == 0)
                throw new InvalidDataException("The file has no contiguous codestream box.");
        }
        else
        {
            result.CodestreamCount = 1;
            result.Codestream = data;
        }

        result.ReadCodestream(result.Codestream.Span);
        return result;
    }

    private void ReadBoxes(byte[] data, int start, int end, bool isTopLevel)
    {
        var position = start;
        while (position < end)
        {
            if (end - position < 8)
                throw new InvalidDataException("A box header is cut.");

            long size = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(position));
            var type = Encoding.Latin1.GetString(data, position + 4, 4);
            var header = 8;
            if (size == 1)
            {
                if (end - position < 16)
                    throw new InvalidDataException("A box header is cut.");

                size = checked((long)BinaryPrimitives.ReadUInt64BigEndian(data.AsSpan(position + 8)));
                header = 16;
            }
            else if (size == 0)
            {
                size = end - position;
            }

            if (size < header || size > end - position)
                throw new InvalidDataException($"The box '{type}' has an invalid size.");

            _boxTypes.Add(type);
            var payload = data.AsMemory(position + header, (int)size - header);
            ReadBox(data, type, payload, position + header, isTopLevel);
            position += (int)size;
        }
    }

    private void ReadBox(byte[] data, string type, ReadOnlyMemory<byte> payload, int payloadOffset, bool isTopLevel)
    {
        var span = payload.Span;
        switch (type)
        {
            case "ftyp" when isTopLevel && span.Length >= 8:
                Brand = Encoding.Latin1.GetString(span[..4]);
                break;

            case "jp2h" when isTopLevel:
                ReadBoxes(data, payloadOffset, payloadOffset + payload.Length, isTopLevel: false);
                break;

            case "res " when !isTopLevel:
                ReadBoxes(data, payloadOffset, payloadOffset + payload.Length, isTopLevel: false);
                break;

            case "ihdr" when span.Length == 14:
                HeaderComponentCount = BinaryPrimitives.ReadUInt16BigEndian(span[8..]);
                HeaderBitsPerComponent = span[10];
                break;

            case "colr" when span.Length >= 3:
                ColorSpecificationCount++;
                if (ColorSpecificationCount == 1)
                {
                    ColorMethod = span[0];
                    if (span[0] == 1 && span.Length >= 7)
                    {
                        EnumeratedColorSpace = checked((int)BinaryPrimitives.ReadUInt32BigEndian(span[3..]));
                    }
                    else if (span[0] is 2 or 3)
                    {
                        IccProfile = payload[3..];
                    }
                }

                break;

            case "pclr" when span.Length >= 3:
                PaletteEntryCount = BinaryPrimitives.ReadUInt16BigEndian(span);
                for (var i = 0; i < span[2] && 3 + i < span.Length; i++)
                {
                    _paletteDepths.Add((span[3 + i] & 0x7F) + 1);
                }

                break;

            case "cmap":
                for (var i = 0; i + 4 <= span.Length; i += 4)
                {
                    _componentMappings.Add(new ReferenceJp2ComponentMapping(BinaryPrimitives.ReadUInt16BigEndian(span[i..]), span[i + 2] == 1, span[i + 3]));
                }

                break;

            case "cdef" when span.Length >= 2:
                for (var i = 2; i + 6 <= span.Length; i += 6)
                {
                    _channelDefinitions.Add(new ReferenceJp2Channel(BinaryPrimitives.ReadUInt16BigEndian(span[i..]), BinaryPrimitives.ReadUInt16BigEndian(span[(i + 2)..]), BinaryPrimitives.ReadUInt16BigEndian(span[(i + 4)..])));
                }

                break;

            case "resc" or "resd" when span.Length == 10:
                var resolution = (
                    (int)BinaryPrimitives.ReadUInt16BigEndian(span),
                    (int)BinaryPrimitives.ReadUInt16BigEndian(span[2..]),
                    (int)BinaryPrimitives.ReadUInt16BigEndian(span[4..]),
                    (int)BinaryPrimitives.ReadUInt16BigEndian(span[6..]),
                    (int)(sbyte)span[8],
                    (int)(sbyte)span[9]);
                if (type == "resc")
                {
                    CaptureResolution = resolution;
                }
                else
                {
                    DisplayResolution = resolution;
                }

                break;

            case "uuid" when isTopLevel && span.Length >= 16:
                if (span[..16].SequenceEqual(XmpUuid))
                {
                    Xmp ??= payload[16..];
                }
                else if (span[..16].SequenceEqual(ExifUuid))
                {
                    Exif ??= payload[16..];
                }

                break;

            case "jp2c" when isTopLevel:
                CodestreamCount++;
                if (CodestreamCount == 1)
                {
                    Codestream = payload;
                    CodestreamOffset = payloadOffset;
                }

                break;
        }
    }

    private void ReadCodestream(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4 || data[0] != 0xFF || data[1] != MarkerSoc || data[2] != 0xFF || data[3] != MarkerSiz)
            throw new InvalidDataException("The codestream does not start with the SOC and SIZ markers.");

        // The main header: marker segments up to the first SOT marker
        var position = 2;
        while (true)
        {
            var marker = ReadMarker(data, position);
            if (marker is MarkerSot or MarkerEoc)
                break;

            var segment = ReadSegment(data, position);
            _mainHeaderMarkers.Add(marker);
            ReadSegment(marker, segment, isInTilePartHeader: false);
            position += 2 + 2 + segment.Length;
        }

        if (_codingStyles.Count == 0 || _codingStyles[0].Component >= 0)
            throw new InvalidDataException("The main header has no COD marker segment.");

        // The tile-parts
        while (ReadMarker(data, position) == MarkerSot)
        {
            var segment = ReadSegment(data, position);
            if (segment.Length != 8)
                throw new InvalidDataException("The SOT marker segment has an invalid length.");

            int tile = BinaryPrimitives.ReadUInt16BigEndian(segment);
            var length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(segment[2..]));
            if (length == 0)
            {
                // The last tile-part extends to the EOC marker
                length = data.Length - 2 - position;
            }

            if (length < 14 || length > data.Length - position)
                throw new InvalidDataException("A tile-part has an invalid length.");

            var markers = new List<int>();
            var header = position + 12;
            while (true)
            {
                var marker = ReadMarker(data, header);
                if (marker == MarkerSod)
                    break;

                var headerSegment = ReadSegment(data, header);
                markers.Add(marker);
                ReadSegment(marker, headerSegment, isInTilePartHeader: true);
                header += 2 + 2 + headerSegment.Length;
                if (header > position + length)
                    throw new InvalidDataException("A tile-part header is longer than its tile-part.");
            }

            _tileParts.Add(new ReferenceJpeg2000TilePart(tile, segment[6], segment[7], position, length, position + length - (header + 2), markers));
            position += length;
        }

        HasEndOfCodestream = position + 2 <= data.Length && data[position] == 0xFF && data[position + 1] == MarkerEoc;
    }

    private void ReadSegment(int marker, ReadOnlySpan<byte> segment, bool isInTilePartHeader)
    {
        var wideComponents = _components.Count > 256;
        switch (marker)
        {
            case MarkerSiz when !isInTilePartHeader:
                if (segment.Length < 36)
                    throw new InvalidDataException("The SIZ marker segment is too short.");

                Capabilities = BinaryPrimitives.ReadUInt16BigEndian(segment);
                var xSize = ReadInt32(segment[2..]);
                var ySize = ReadInt32(segment[6..]);
                ImageXOffset = ReadInt32(segment[10..]);
                ImageYOffset = ReadInt32(segment[14..]);
                TileWidth = ReadInt32(segment[18..]);
                TileHeight = ReadInt32(segment[22..]);
                TileXOffset = ReadInt32(segment[26..]);
                TileYOffset = ReadInt32(segment[30..]);
                Width = xSize - ImageXOffset;
                Height = ySize - ImageYOffset;
                int count = BinaryPrimitives.ReadUInt16BigEndian(segment[34..]);
                if (segment.Length != 36 + (3 * count) || TileWidth <= 0 || TileHeight <= 0)
                    throw new InvalidDataException("The SIZ marker segment is malformed.");

                for (var i = 0; i < count; i++)
                {
                    var depth = segment[36 + (3 * i)];
                    _components.Add(new ReferenceJpeg2000Component((depth & 0x7F) + 1, (depth & 0x80) != 0, segment[37 + (3 * i)], segment[38 + (3 * i)]));
                }

                TileCount = checked((int)(DivideUp((long)xSize - TileXOffset, TileWidth) * DivideUp((long)ySize - TileYOffset, TileHeight)));
                break;

            case MarkerCap when !isInTilePartHeader && segment.Length >= 4:
                ExtendedCapabilities = BinaryPrimitives.ReadUInt32BigEndian(segment);

                // One Ccap field per bit of Pcap, in the order of the parts: the bit 31 is the part 1, the bit 17 the part 15
                var index = 0;
                for (var bit = 31; bit > 17; bit--)
                {
                    if ((ExtendedCapabilities & (1u << bit)) != 0)
                    {
                        index++;
                    }
                }

                if ((ExtendedCapabilities & (1u << 17)) != 0 && segment.Length >= 4 + (2 * (index + 1)))
                {
                    HighThroughputCapabilities = BinaryPrimitives.ReadUInt16BigEndian(segment[(4 + (2 * index))..]);
                }

                break;

            case MarkerCod when segment.Length >= 10:
                if (!isInTilePartHeader)
                {
                    UsesSop = (segment[0] & 2) != 0;
                    UsesEph = (segment[0] & 4) != 0;
                    ProgressionOrder = segment[1];
                    LayerCount = BinaryPrimitives.ReadUInt16BigEndian(segment[2..]);
                    UsesComponentTransform = segment[4] != 0;
                }

                _codingStyles.Add(new ReferenceJpeg2000CodingStyle(-1, segment[5], 1 << (segment[6] + 2), 1 << (segment[7] + 2), segment[8], segment[9] == 1, (segment[0] & 1) != 0, isInTilePartHeader));
                break;

            case MarkerCoc:
                var styleOffset = wideComponents ? 2 : 1;
                if (segment.Length >= styleOffset + 6)
                {
                    var component = wideComponents ? BinaryPrimitives.ReadUInt16BigEndian(segment) : segment[0];
                    var style = segment[styleOffset..];
                    _codingStyles.Add(new ReferenceJpeg2000CodingStyle(component, style[1], 1 << (style[2] + 2), 1 << (style[3] + 2), style[4], style[5] == 1, (style[0] & 1) != 0, isInTilePartHeader));
                }

                break;

            case MarkerQcd when !isInTilePartHeader && segment.Length >= 1:
                QuantizationStyle = segment[0] & 0x1F;
                GuardBits = segment[0] >> 5;
                break;

            case MarkerQcc:
                ComponentQuantizationCount++;
                break;

            case MarkerRgn:
                var shiftOffset = wideComponents ? 3 : 2;
                if (segment.Length > shiftOffset)
                {
                    _regions.Add((wideComponents ? BinaryPrimitives.ReadUInt16BigEndian(segment) : segment[0], segment[shiftOffset]));
                }

                break;

            case MarkerPoc:
                ProgressionChangeCount += segment.Length / (wideComponents ? 9 : 7);
                break;
        }
    }

    private static int ReadMarker(ReadOnlySpan<byte> data, int position)
    {
        if (position + 2 > data.Length || data[position] != 0xFF)
            throw new InvalidDataException($"A marker is expected at the offset {position} of the codestream.");

        return data[position + 1];
    }

    private static ReadOnlySpan<byte> ReadSegment(ReadOnlySpan<byte> data, int position)
    {
        if (position + 4 > data.Length)
            throw new InvalidDataException("A marker segment is cut.");

        int length = BinaryPrimitives.ReadUInt16BigEndian(data[(position + 2)..]);
        if (length < 2 || length > data.Length - position - 2)
            throw new InvalidDataException("A marker segment has an invalid length.");

        return data.Slice(position + 4, length - 2);
    }

    private static int ReadInt32(ReadOnlySpan<byte> data) => checked((int)BinaryPrimitives.ReadUInt32BigEndian(data));

    private static long DivideUp(long value, long divisor) => (value + divisor - 1) / divisor;
}
